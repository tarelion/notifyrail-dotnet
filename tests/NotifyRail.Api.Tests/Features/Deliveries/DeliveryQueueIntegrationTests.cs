using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NotifyRail.Api.Features.Deliveries.Queue;
using NotifyRail.Api.Features.Messages.CreateMessage;
using NotifyRail.Api.Infrastructure.Persistence;

namespace NotifyRail.Api.Tests;

public sealed class DeliveryQueueIntegrationTests
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public DeliveryQueueIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ClaimDueAsync_ClaimsDueDeliveryAndReturnsProviderRequest()
    {
        await ResetDatabaseAsync();
        await CreateMessageAsync();

        await using var scope = _factory.Services.CreateAsyncScope();
        var queue = scope.ServiceProvider.GetRequiredService<DeliveryQueue>();

        var jobs = await queue.ClaimDueAsync(
            "worker-a",
            limit: 10,
            DateTimeOffset.UtcNow,
            CancellationToken.None);

        var job = Assert.Single(jobs);
        Assert.EndsWith("-attempt-1", job.Request.IdempotencyKey);
        Assert.Equal("+905551111111", job.Request.Recipient);
        Assert.Equal("sms", job.Request.Channel);
        Assert.Equal("NotifyRail", job.Request.SenderTitle);
        Assert.Equal("Your order is ready.", job.Request.Body);
    }

    [Fact]
    public async Task ClaimDueAsync_RejectsBlankWorkerId()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var queue = scope.ServiceProvider.GetRequiredService<DeliveryQueue>();

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            queue.ClaimDueAsync(
                "   ",
                limit: 10,
                DateTimeOffset.UtcNow,
                CancellationToken.None));

        Assert.Equal("workerId", exception.ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task ClaimDueAsync_RejectsNonPositiveLimit(int limit)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var queue = scope.ServiceProvider.GetRequiredService<DeliveryQueue>();

        var exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            queue.ClaimDueAsync(
                "worker-a",
                limit,
                DateTimeOffset.UtcNow,
                CancellationToken.None));

        Assert.Equal("limit", exception.ParamName);
    }

    [Fact]
    public async Task ClaimDueAsync_RecoversClaimWhenFiveMinuteLeaseExpires()
    {
        await ResetDatabaseAsync();
        await CreateMessageAsync();

        await using var scope = _factory.Services.CreateAsyncScope();
        var queue = scope.ServiceProvider.GetRequiredService<DeliveryQueue>();
        var firstClaimTime = DateTimeOffset.UtcNow;

        var firstJobs = await queue.ClaimDueAsync(
            "worker-a",
            limit: 1,
            firstClaimTime,
            CancellationToken.None);
        var recoveredJobs = await queue.ClaimDueAsync(
            "worker-b",
            limit: 1,
            firstClaimTime.AddMinutes(5),
            CancellationToken.None);

        var firstJob = Assert.Single(firstJobs);
        var recoveredJob = Assert.Single(recoveredJobs);
        Assert.Equal(firstJob.Request.IdempotencyKey, recoveredJob.Request.IdempotencyKey);
    }

    [Fact]
    public async Task ClaimDueAsync_DoesNotReturnExpiredDelivery()
    {
        await ResetDatabaseAsync();
        await CreateMessageAsync();
        var claimTime = DateTimeOffset.UtcNow;
        await SetDeliveryExpiryAsync(claimTime);

        await using var scope = _factory.Services.CreateAsyncScope();
        var queue = scope.ServiceProvider.GetRequiredService<DeliveryQueue>();

        var jobs = await queue.ClaimDueAsync(
            "worker-a",
            limit: 10,
            claimTime,
            CancellationToken.None);

        Assert.Empty(jobs);
    }

    [Fact]
    public async Task ClaimDueAsync_SkipsLockedDeliveryAndClaimsAvailableDelivery()
    {
        await ResetDatabaseAsync();
        await CreateMessageAsync(["+905551111111", "+905552222222"]);

        await using var lockScope = _factory.Services.CreateAsyncScope();
        var lockContext = lockScope.ServiceProvider.GetRequiredService<NotifyRailDbContext>();
        await using var lockTransaction =
            await lockContext.Database.BeginTransactionAsync(CancellationToken.None);
        await lockContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            SELECT id
            FROM deliveries
            WHERE recipient = {"+905551111111"}
            FOR UPDATE
            """);

        await using var claimScope = _factory.Services.CreateAsyncScope();
        var queue = claimScope.ServiceProvider.GetRequiredService<DeliveryQueue>();
        var jobs = await queue.ClaimDueAsync(
            "worker-a",
            limit: 10,
            DateTimeOffset.UtcNow,
            CancellationToken.None);

        var job = Assert.Single(jobs);
        Assert.Equal("+905552222222", job.Request.Recipient);
    }

    private async Task ResetDatabaseAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotifyRailDbContext>();

        await dbContext.Database.MigrateAsync(CancellationToken.None);
        await dbContext.Database.ExecuteSqlRawAsync(
            "TRUNCATE delivery_attempts, deliveries, messages;",
            CancellationToken.None);
    }

    private async Task CreateMessageAsync(string[]? recipients = null)
    {
        using var client = _factory.CreateClient();
        using var response = await client.PostAsJsonAsync(
            "/messages",
            new CreateMessageRequest(
                Type: "transactional",
                Channel: "sms",
                SenderTitle: "NotifyRail",
                Body: "Your order is ready.",
                Recipients: recipients ?? ["+905551111111"],
                IdempotencyKey: $"claim-due-{Guid.NewGuid()}"),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    private async Task SetDeliveryExpiryAsync(DateTimeOffset expiresAt)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotifyRailDbContext>();

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE deliveries SET expires_at = {expiresAt}");
    }
}
