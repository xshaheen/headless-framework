// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Framework.Testing.Helpers;

public sealed class TestClock : IClock
{
    public TimeProvider TimeProvider { get; set; } = new FakeTimeProvider();

    private static DateTimeKind NormalizeKind => DateTimeKind.Utc;

    public TimeZoneInfo LocalTimeZone => TimeProvider.LocalTimeZone;

    public long Ticks => Environment.TickCount64;

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
        if (NormalizeKind == v.Kind)
        {
            return v;
        }

        return NormalizeKind switch
        {
            DateTimeKind.Unspecified => v,
            DateTimeKind.Local when v.Kind is DateTimeKind.Utc => v.ToLocalTime(),
            DateTimeKind.Utc when v.Kind is DateTimeKind.Local => v.ToUniversalTime(),
            _ => DateTime.SpecifyKind(v, NormalizeKind),
        };
    }
}
