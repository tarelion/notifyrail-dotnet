using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;
using NotifyRail.Api.Features.ApiClients.Persistence;
using NotifyRail.Api.Infrastructure.Persistence;

namespace NotifyRail.Api.Tests;

public sealed class ApiClientFoundationMigrationTests
{
    private const string MvpMigration = "20260705080100_CreateOtpChallenges";

    [Fact]
    public async Task Migration_PreservesMvpRelationshipsAndAssignsLegacyOwnership()
    {
        var sourceConnectionString = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build()
            .GetConnectionString("Postgres")
            ?? "Host=localhost;Port=5432;Database=notifyrail;Username=notifyrail;Password=notifyrail";
        var databaseName = $"notifyrail_migration_test_{Guid.NewGuid():N}";
        var targetBuilder = new NpgsqlConnectionStringBuilder(sourceConnectionString)
        {
            Database = databaseName,
        };
        var adminBuilder = new NpgsqlConnectionStringBuilder(sourceConnectionString)
        {
            Database = "postgres",
        };

        await CreateDatabaseAsync(adminBuilder.ConnectionString, databaseName);

        try
        {
            var options = new DbContextOptionsBuilder<NotifyRailDbContext>()
                .UseNpgsql(targetBuilder.ConnectionString)
                .Options;

            await using var dbContext = new NotifyRailDbContext(options);
            await dbContext.Database.MigrateAsync(MvpMigration);
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO messages (
                    id, type, channel, sender_title, body, idempotency_key,
                    created_at, updated_at)
                VALUES (
                    '10000000-0000-0000-0000-000000000001', 'otp', 'sms',
                    'NotifyRail', 'Code: 123456', 'existing-mvp-key',
                    '2026-07-01T10:00:00Z', '2026-07-01T10:00:00Z');

                INSERT INTO deliveries (
                    id, message_id, recipient, status, attempt_count,
                    provider_message_id, expires_at, created_at, updated_at)
                VALUES (
                    '20000000-0000-0000-0000-000000000001',
                    '10000000-0000-0000-0000-000000000001', '+905551111111',
                    'sent', 1, 'provider-existing', '2026-07-01T10:05:00Z',
                    '2026-07-01T10:00:00Z', '2026-07-01T10:00:01Z');

                INSERT INTO delivery_attempts (
                    id, delivery_id, attempt_number, provider, outcome,
                    provider_message_id, attempted_at)
                VALUES (
                    '30000000-0000-0000-0000-000000000001',
                    '20000000-0000-0000-0000-000000000001', 1, 'mock',
                    'accepted', 'provider-existing', '2026-07-01T10:00:01Z');

                INSERT INTO otp_challenges (
                    id, message_id, recipient, code_hash, expires_at,
                    failed_attempt_count, max_attempts, created_at, updated_at)
                VALUES (
                    '40000000-0000-0000-0000-000000000001',
                    '10000000-0000-0000-0000-000000000001', '+905551111111',
                    decode(repeat('00', 32), 'hex'), '2026-07-01T10:05:00Z',
                    0, 3, '2026-07-01T10:00:00Z', '2026-07-01T10:00:00Z');
                """);

            await dbContext.Database.MigrateAsync();

            var message = await dbContext.Messages.AsNoTracking().SingleAsync();
            var delivery = await dbContext.Deliveries.AsNoTracking().SingleAsync();
            var attempt = await dbContext.DeliveryAttempts.AsNoTracking().SingleAsync();
            var challenge = await dbContext.OtpChallenges.AsNoTracking().SingleAsync();

            Assert.Equal(Guid.Parse("10000000-0000-0000-0000-000000000001"), message.Id);
            Assert.Equal(ApiClient.LegacyId, message.ApiClientId);
            Assert.Equal(message.Id, delivery.MessageId);
            Assert.Equal(delivery.Id, attempt.DeliveryId);
            Assert.Equal(message.Id, challenge.MessageId);
            Assert.Empty(await dbContext.ApiKeys
                .Where(apiKey => apiKey.ApiClientId == ApiClient.LegacyId)
                .ToArrayAsync());

            var secondClient = ApiClient.Create("Second Client", DateTimeOffset.UtcNow);
            dbContext.ApiClients.Add(secondClient);
            await dbContext.SaveChangesAsync();
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO messages (
                    id, api_client_id, type, channel, sender_title, body,
                    idempotency_key, created_at, updated_at)
                VALUES (
                    {Guid.NewGuid()}, {secondClient.Id}, 'transactional', 'sms',
                    'NotifyRail', 'Second client message', 'existing-mvp-key',
                    NOW(), NOW())
                """);
        }
        finally
        {
            await DropDatabaseAsync(adminBuilder.ConnectionString, databaseName);
        }
    }

    private static async Task CreateDatabaseAsync(string connectionString, string databaseName)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task DropDatabaseAsync(string connectionString, string databaseName)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)",
            connection);
        await command.ExecuteNonQueryAsync();
    }
}
