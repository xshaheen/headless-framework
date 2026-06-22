// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

/// <summary>Utility helpers for comparing keepalive cadence <see cref="TimeSpan"/> values.</summary>
internal static class TimeSpanCadence
{
    /// <summary>
    /// Compares two cadences, treating <see cref="Timeout.InfiniteTimeSpan"/> as larger than any finite value
    /// (an infinite cadence means "never", which is the longest possible interval).
    /// </summary>
    public static int CompareWithInfinite(TimeSpan a, TimeSpan b)
    {
        var aInfinite = a == Timeout.InfiniteTimeSpan;
        var bInfinite = b == Timeout.InfiniteTimeSpan;

        if (aInfinite || bInfinite)
        {
            return aInfinite == bInfinite ? 0 : (aInfinite ? 1 : -1);
        }

        return a.CompareTo(b);
    }
}
