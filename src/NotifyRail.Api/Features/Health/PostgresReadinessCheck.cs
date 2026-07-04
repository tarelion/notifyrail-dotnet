using Npgsql;

namespace NotifyRail.Api.Features.Health;

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
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
