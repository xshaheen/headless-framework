// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Abstractions;

public interface IClock
{
    TimeZoneInfo LocalTimeZone { get; }

    DateTimeOffset UtcNow { get; }

    DateTimeOffset LocalNow { get; }

    long GetTimestamp();

    TimeSpan GetElapsedTime(long startingTimestamp, long endingTimestamp);

    TimeSpan GetElapsedTime(long startingTimestamp);

    DateTimeOffset Normalize(DateTimeOffset v);

    DateTime Normalize(DateTime v);
}

public sealed class Clock(TimeProvider timeProvider) : IClock
{
    public TimeZoneInfo LocalTimeZone => timeProvider.LocalTimeZone;

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
        // Normalizes to UTC. Unspecified is assumed to already be UTC and is stamped without conversion.
        return v.Kind switch
        {
            DateTimeKind.Utc => v,
            DateTimeKind.Local => v.ToUniversalTime(),
            _ => DateTime.SpecifyKind(v, DateTimeKind.Utc),
        };
    }
}
