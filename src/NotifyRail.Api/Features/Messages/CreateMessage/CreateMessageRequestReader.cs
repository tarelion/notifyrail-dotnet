using System.Text.Json;
using System.Text.Json.Serialization;

namespace NotifyRail.Api.Features.Messages.CreateMessage;

internal static class CreateMessageRequestReader
{
    private const int MaxCreateMessageBodyBytes = 1 << 20;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static async Task<CreateMessageReadResult> ReadAsync(
        HttpRequest httpRequest,
        CancellationToken cancellationToken)
    {
        // Content-Length can be absent or inaccurate (for example, with chunked
        // requests), so the same limit is enforced again while reading the stream.
        if (httpRequest.ContentLength > MaxCreateMessageBodyBytes)
        {
            return CreateMessageReadResult.Failure(
                "invalid JSON body: request body is too large");
        }

        await using var body = new MemoryStream();
        var buffer = new byte[81920];
        while (true)
        {
            var bytesRead = await httpRequest.Body.ReadAsync(
                buffer.AsMemory(),
                cancellationToken);
            if (bytesRead == 0)
            {
                break;
            }

            if (body.Length + bytesRead > MaxCreateMessageBodyBytes)
            {
                return CreateMessageReadResult.Failure(
                    "invalid JSON body: request body is too large");
            }

            body.Write(buffer.AsSpan(0, bytesRead));
        }

        if (body.Length == 0)
        {
            return CreateMessageReadResult.Failure(
                "invalid JSON body: request body is required");
        }

        try
        {
            var reader = new Utf8JsonReader(
                body.ToArray(),
                isFinalBlock: true,
                state: default);
            var request = JsonSerializer.Deserialize<CreateMessageRequest>(
                ref reader,
                JsonOptions);
            if (request is null)
            {
                return CreateMessageReadResult.Failure(
                    "invalid JSON body: request body is required");
            }
            if (reader.Read())
            {
                return CreateMessageReadResult.Failure(
                    "invalid JSON body: request body must contain one JSON object");
            }

            return CreateMessageReadResult.Success(request);
        }
        catch (JsonException exception)
        {
            return CreateMessageReadResult.Failure(
                $"invalid JSON body: {exception.Message}");
        }
    }
}
