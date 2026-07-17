namespace NotifyRail.Api.Infrastructure.Persistence;

public static class PostgresTimestamp
{
    public static DateTimeOffset Normalize(DateTimeOffset value)
    {
        var utcValue = value.ToUniversalTime();
        var ticks = utcValue.Ticks - utcValue.Ticks % 10;
        return new DateTimeOffset(ticks, TimeSpan.Zero);
    }
}
