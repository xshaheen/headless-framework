namespace Framework.Kernel.BuildingBlocks.Abstractions;

public interface IClock
{
    TimeZoneInfo LocalTimeZone { get; }

    long Ticks { get; }

    DateTimeOffset UtcNow { get; }

    long GetTimestamp();

    TimeSpan GetElapsedTime(long startingTimestamp, long endingTimestamp);

    TimeSpan GetElapsedTime(long startingTimestamp);

    DateTimeOffset Normalize(DateTimeOffset v);

    DateTime Normalize(DateTime v);
}

public sealed class Clock(TimeProvider timeProvider) : IClock
{
    private static DateTimeKind NormalizeKind => DateTimeKind.Utc;

    public TimeZoneInfo LocalTimeZone => timeProvider.LocalTimeZone;

    public long Ticks => Environment.TickCount64;

    public DateTimeOffset UtcNow => timeProvider.GetUtcNow();

    public DateTimeOffset LocalNow => timeProvider.GetLocalNow();

    public long GetTimestamp() => timeProvider.GetTimestamp();

    public TimeSpan GetElapsedTime(long startingTimestamp, long endingTimestamp)
    {
        return timeProvider.GetElapsedTime(startingTimestamp, endingTimestamp);
    }

    public TimeSpan GetElapsedTime(long startingTimestamp)
    {
        return timeProvider.GetElapsedTime(startingTimestamp);
    }

    public DateTimeOffset Normalize(DateTimeOffset v) => v.ToUniversalTime();

    public DateTime Normalize(DateTime v)
    {
        if (NormalizeKind == v.Kind)
        {
            return v;
        }

        return NormalizeKind switch
        {
            DateTimeKind.Unspecified => v,
            DateTimeKind.Local when v.Kind is DateTimeKind.Utc => v.ToLocalTime(),
            DateTimeKind.Utc when v.Kind is DateTimeKind.Local => v.ToUniversalTime(),
            _ => DateTime.SpecifyKind(v, NormalizeKind)
        };
    }
}
