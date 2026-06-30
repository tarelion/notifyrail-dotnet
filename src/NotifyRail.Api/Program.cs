using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<IReadinessCheck>(services =>
{
    var configuration = services.GetRequiredService<IConfiguration>();
    var connectionString = configuration.GetConnectionString("Postgres");

    return string.IsNullOrWhiteSpace(connectionString)
        ? new MissingConfigurationReadinessCheck()
        : new PostgresReadinessCheck(connectionString);
});

var app = builder.Build();

app.MapGet("/healthz", () => Results.Ok(new HealthResponse("ok")));
app.MapGet("/readyz", async (IReadinessCheck readinessCheck, CancellationToken cancellationToken) =>
{
    var isReady = await readinessCheck.IsReadyAsync(cancellationToken);

    return isReady
        ? Results.Ok(new ReadinessResponse("ready"))
        : Results.Json(
            new ReadinessResponse("not_ready"),
            statusCode: StatusCodes.Status503ServiceUnavailable);
});

app.Run();

public sealed record HealthResponse(string Status);

public sealed record ReadinessResponse(string Status);

public interface IReadinessCheck
{
    Task<bool> IsReadyAsync(CancellationToken cancellationToken);
}

public sealed class MissingConfigurationReadinessCheck : IReadinessCheck
{
    public Task<bool> IsReadyAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(false);
    }
}

public sealed class PostgresReadinessCheck : IReadinessCheck
{
    private readonly string _connectionString;

    public PostgresReadinessCheck(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<bool> IsReadyAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = new NpgsqlCommand("SELECT 1", connection);
            var result = await command.ExecuteScalarAsync(cancellationToken);

            return result is 1;
        }
        catch (Exception)
        {
            return false;
        }
    }
}

public partial class Program;
