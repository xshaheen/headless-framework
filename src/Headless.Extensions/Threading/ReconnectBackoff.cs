// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Threading;

/// <summary>
/// The delay a long-lived connection waits before it tries again to reconnect, after a failed attempt or a lost
/// connection alike: about one second at first, doubling up to thirty seconds, and back to about one second once the
/// connection is up. Each delay is jittered, so the processes of a fleet that loses its server at once do not reconnect
/// in step.
/// </summary>
/// <remarks>
/// The waits run on <see cref="TimeProvider.System"/> unless a clock is passed. A reconnect waits for a server to come
/// back in real time; on an app clock that a test host fakes and never advances, a lost connection would never
/// reconnect. Pass a clock only to step through the delays in a test of the loop itself. An instance is not
/// thread-safe: it belongs to the one loop that reconnects.
/// </remarks>
/// <param name="clock">The clock the waits run on; <see cref="TimeProvider.System"/> when omitted.</param>
/// <param name="jitter">The jitter source; <see cref="Random.Shared"/> when omitted.</param>
[PublicAPI]
public sealed class ReconnectBackoff(TimeProvider? clock = null, Random? jitter = null)
{
    /// <summary>The nominal first delay, and the shortest delay a reconnect waits.</summary>
    public static readonly TimeSpan FirstDelay = TimeSpan.FromSeconds(1);

    /// <summary>The longest delay a reconnect waits.</summary>
    public static readonly TimeSpan Ceiling = TimeSpan.FromSeconds(30);

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly Random _jitter = jitter ?? Random.Shared;
    private TimeSpan _nominal = FirstDelay;
    private TimeSpan? _delay;

    /// <summary>The jittered delay the next <see cref="WaitAsync"/> waits.</summary>
    public TimeSpan Delay => _delay ??= Jitter(_nominal, FirstDelay, _jitter);

    /// <summary>Starts the sequence over, once the connection is up again.</summary>
    public void Reset()
    {
        _nominal = FirstDelay;
        _delay = null;
    }

    /// <summary>Waits <see cref="Delay"/>, then doubles the delay up to <see cref="Ceiling"/>.</summary>
    /// <param name="cancellationToken">The loop's stopping token.</param>
    /// <returns><see langword="false"/> when the token was cancelled during the wait; otherwise <see langword="true"/>.</returns>
    public async ValueTask<bool> WaitAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(Delay, _clock, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        _nominal = TimeSpan.FromTicks(Math.Min(_nominal.Ticks * 2, Ceiling.Ticks));
        _delay = null;
        return true;
    }

    /// <summary>Returns a jittered delay within <c>[floor, 30s]</c> around <paramref name="nominal"/>.</summary>
    /// <remarks>
    /// Jitter is not cosmetic here: without it, a fleet of clients that all lose the connection at the same instant (a
    /// server restart) retries in lockstep at exactly 1s/2s/4s/8s/16s/30s and hammers the server in synchronized waves.
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
    /// <returns>The jittered delay.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="random"/> is <see langword="null"/>.</exception>
    public static TimeSpan Jitter(TimeSpan nominal, TimeSpan floor, Random random)
    {
        Argument.IsNotNull(random);

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
