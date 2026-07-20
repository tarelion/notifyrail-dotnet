using Npgsql;

namespace NotifyRail.Api.Features.Webhooks.Queue;

public sealed class WebhookSigningLease : IAsyncDisposable
{
    private readonly NpgsqlConnection _connection;
    private readonly Guid _apiClientId;
    private bool _disposed;

    internal WebhookSigningLease(
        NpgsqlConnection connection,
        Guid apiClientId,
        byte[] protectedSecret)
    {
        _connection = connection;
        _apiClientId = apiClientId;
        ProtectedSecret = protectedSecret;
    }

    public byte[] ProtectedSecret { get; }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            await using var command = _connection.CreateCommand();
            command.CommandText =
                "SELECT pg_advisory_unlock_shared(hashtextextended(@api_client_id, 0))";
            command.Parameters.AddWithValue("api_client_id", _apiClientId.ToString());
            await command.ExecuteNonQueryAsync();
        }
        finally
        {
            await _connection.DisposeAsync();
        }
    }
}
