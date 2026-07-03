namespace NotifyRail.Api.Features.Messages.Persistence;

public sealed class Message
{
    private Message()
    {
    }

    public Guid Id { get; private set; }

    public string Type { get; private set; } = null!;

    public string Channel { get; private set; } = null!;

    public string SenderTitle { get; private set; } = null!;

    public string Body { get; private set; } = null!;

    public string IdempotencyKey { get; private set; } = null!;

    public string? ReportLabel { get; private set; }

    public string? Encoding { get; private set; }

    public DateTimeOffset? ScheduledAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }
}
