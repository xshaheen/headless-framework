// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Threading;

/// <summary>Lock-free helpers that complement <see cref="Interlocked"/>.</summary>
[PublicAPI]
public static class InterlockedExtensions
{
    /// <summary>
    /// Monotonically raises <paramref name="location"/> to <paramref name="value"/> with a lock-free
    /// compare-and-swap loop; never lowers it (raise-only). Returns the value observed in
    /// <paramref name="location"/> when the loop settled: <paramref name="value"/> when the raise won, otherwise
    /// the equal-or-greater value another thread already published.
    /// </summary>
    public static long InterlockedRaiseTo(this ref long location, long value)
    {
        long current;

        while ((current = Volatile.Read(ref location)) < value)
        {
            if (Interlocked.CompareExchange(ref location, value, current) == current)
            {
                return value;
            }
        }

        return current;
    }
}
