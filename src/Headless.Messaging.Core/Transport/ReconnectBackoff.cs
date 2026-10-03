// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Transport;

/// <summary>Spreads a transport's reconnect delays so a fleet that loses its broker at once does not retry in step.</summary>
internal static class ReconnectBackoff
{
    /// <summary>The longest delay a reconnect waits.</summary>
    public static readonly TimeSpan Ceiling = TimeSpan.FromSeconds(30);

    /// <summary>Returns a jittered delay within <c>[floor, 30s]</c> around <paramref name="nominal"/>.</summary>
    /// <remarks>
    /// Jitter is not cosmetic here: without it, a fleet of consumers that all lose the connection at the same instant (a
    /// broker restart) retries in lockstep at exactly 1s/2s/4s/8s/16s/30s and hammers the broker in synchronized waves.
    /// <para>
    /// Three properties must hold together, and the obvious implementations each break one of them. Adding jitter on top
    /// of the capped value overshoots the 30s ceiling. Clamping that additive result back down to the ceiling collapses
    /// every caller to exactly 30s once the exponential curve saturates — killing the spread at precisely the moment the
    /// herd is largest. Subtracting jitter unconditionally undercuts the <paramref name="floor"/>, which callers use to
    /// guarantee a minimum wait.
    /// </para>
    /// <para>
    /// So the jitter band is computed explicitly and is always non-degenerate: normally it spreads DOWN from the capped
    /// value (<c>[0.75x, 1x]</c>), and when the floor is what pins the delay — leaving no room below — it spreads UP from
    /// the floor instead, still bounded by the ceiling. The result is therefore never above 30s, never below the floor,
    /// and never a single lockstep value. That last case matters: an error that hits every client at once makes the floor
    /// path itself a herd, which needs the spread as much as the reconnect path does.
    /// </para>
    /// </remarks>
    /// <param name="nominal">The un-jittered delay, normally doubled after each failed attempt.</param>
    /// <param name="floor">The shortest delay the caller accepts.</param>
    /// <param name="random">The jitter source; tests pass one that pins the result to an edge of the band.</param>
    public static TimeSpan Jitter(TimeSpan nominal, TimeSpan floor, Random random)
    {
        var next = nominal > Ceiling ? Ceiling : nominal;
        var capped = floor > next ? floor : next;

        var jitterBudget = TimeSpan.FromTicks(capped.Ticks / 4);
        var lower = capped - jitterBudget < floor ? floor : capped - jitterBudget;

        // When the floor pins the delay there is no room to jitter downward, so spread upward instead — still capped by
        // the ceiling, so the "never above 30s" guarantee holds in every branch.
        var upper =
            lower < capped ? capped
            : capped + jitterBudget > Ceiling ? Ceiling
            : capped + jitterBudget;

        if (upper <= lower)
        {
            return lower;
        }

#pragma warning disable CA5394 // Non-security jitter for retry backoff; cryptographic RNG is unnecessary here.
        var offsetMs = random.Next(0, (int)Math.Max(1, (upper - lower).TotalMilliseconds));
#pragma warning restore CA5394

        return lower + TimeSpan.FromMilliseconds(offsetMs);
    }
}
