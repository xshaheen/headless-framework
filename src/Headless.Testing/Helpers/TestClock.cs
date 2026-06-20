// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Headless.Testing.Helpers;

[PublicAPI]
public sealed class TestClock(TimeProvider? timeProvider = null) : IClock
{
    public TimeProvider TimeProvider { get; init; } = timeProvider ?? new FakeTimeProvider();

    public TimeZoneInfo LocalTimeZone => TimeProvider.LocalTimeZone;

    public DateTimeOffset UtcNow => TimeProvider.GetUtcNow();

    public DateTimeOffset LocalNow => TimeProvider.GetLocalNow();

    public long GetTimestamp() => TimeProvider.GetTimestamp();

    public TimeSpan GetElapsedTime(long startingTimestamp, long endingTimestamp)
    {
        return TimeProvider.GetElapsedTime(startingTimestamp, endingTimestamp);
    }

    public TimeSpan GetElapsedTime(long startingTimestamp)
    {
        return TimeProvider.GetElapsedTime(startingTimestamp);
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
