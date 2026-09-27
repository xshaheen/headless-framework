// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.RateLimiting;

/// <summary>A fixed-window quota: at most <see cref="Limit"/> attempts per <see cref="Window"/>.</summary>
/// <remarks>
/// The window is counted in whole seconds and aligned to absolute time, so every process agrees on the same bucket
/// without coordinating. Changing <see cref="Limit"/> applies to the running window on the next attempt; changing
/// <see cref="Window"/> starts every subject on a fresh counter, because the window length is part of the key.
/// </remarks>
[PublicAPI]
public sealed record AttemptQuota
{
    /// <summary>Initializes a quota.</summary>
    /// <param name="limit">The number of attempts admitted per window; at least one.</param>
    /// <param name="window">The window length; a positive whole number of seconds.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="limit"/> is below one, or <paramref name="window"/> is not positive.
    /// </exception>
    /// <exception cref="ArgumentException"><paramref name="window"/> is not a whole number of seconds.</exception>
    public AttemptQuota(int limit, TimeSpan window)
    {
        Argument.IsPositive(limit);
        Argument.IsPositive(window);

        // Windows are bucketed by whole Unix seconds; a fractional window would round to a different length in
        // every process that computed it, and a sub-second one would divide by zero.
        Argument.IsTrue(
            window.Ticks % TimeSpan.TicksPerSecond == 0,
            "The window must be a whole number of seconds.",
            nameof(window)
        );

        Limit = limit;
        Window = window;
    }

    /// <summary>The number of attempts admitted per window.</summary>
    public int Limit { get; }

    /// <summary>The window length.</summary>
    public TimeSpan Window { get; }
}
