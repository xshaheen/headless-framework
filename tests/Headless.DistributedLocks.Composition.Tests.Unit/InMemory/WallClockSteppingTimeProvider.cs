// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Time.Testing;

namespace Tests.InMemory;

/// <summary>
/// Simulates an NTP or manual wall-clock step: <see cref="WallClockOffset"/> moves <see cref="GetUtcNow"/>
/// only, while the monotonic timestamp keeps following the wrapped <see cref="FakeTimeProvider"/>.
/// <see cref="FakeTimeProvider"/> alone cannot do this because it derives its timestamp from its UTC clock
/// and refuses to move backwards.
/// </summary>
internal sealed class WallClockSteppingTimeProvider(FakeTimeProvider inner) : TimeProvider
{
    public TimeSpan WallClockOffset { get; set; }

    public override DateTimeOffset GetUtcNow() => inner.GetUtcNow() + WallClockOffset;

    public override long GetTimestamp() => inner.GetTimestamp();

    public override long TimestampFrequency => inner.TimestampFrequency;

    public override TimeZoneInfo LocalTimeZone => inner.LocalTimeZone;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
        inner.CreateTimer(callback, state, dueTime, period);
}
