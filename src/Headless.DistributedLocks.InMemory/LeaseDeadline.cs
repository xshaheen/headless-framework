// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;

namespace Headless.DistributedLocks.InMemory;

/// <summary>
/// A lease expiry measured on the <see cref="TimeProvider"/>'s monotonic timestamp rather than its wall clock,
/// so an NTP or manual clock step neither expires a lease early nor keeps it alive past its time-to-live.
/// </summary>
/// <param name="StartTimestamp">The <see cref="TimeProvider.GetTimestamp"/> value when the TTL started.</param>
/// <param name="Ttl">The time-to-live measured from <paramref name="StartTimestamp"/>.</param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct LeaseDeadline(long StartTimestamp, TimeSpan Ttl)
{
    public static LeaseDeadline StartNow(TimeProvider timeProvider, TimeSpan ttl)
    {
        return new(timeProvider.GetTimestamp(), ttl);
    }

    public static LeaseDeadline? StartNow(TimeProvider timeProvider, TimeSpan? ttl)
    {
        return ttl is { } value ? StartNow(timeProvider, value) : null;
    }

    /// <summary>Returns the time left before expiry, clamped to <see cref="TimeSpan.Zero"/>.</summary>
    public TimeSpan GetRemaining(TimeProvider timeProvider)
    {
        var remaining = Ttl - timeProvider.GetElapsedTime(StartTimestamp);

        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    public bool IsExpired(TimeProvider timeProvider)
    {
        return timeProvider.GetElapsedTime(StartTimestamp) >= Ttl;
    }

    /// <summary>Returns <see langword="true"/> when this deadline falls after <paramref name="other"/>.</summary>
    public bool IsLaterThan(LeaseDeadline other, TimeProvider timeProvider)
    {
        return GetRemaining(timeProvider) > other.GetRemaining(timeProvider);
    }
}
