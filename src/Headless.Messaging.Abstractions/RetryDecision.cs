// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;

namespace Headless.Messaging;

/// <summary>
/// Describes how the retry pipeline should react to a single failed delivery attempt.
/// Returned by <see cref="IRetryBackoffStrategy.Compute"/> and produced by the shared
/// retry helper for both consume and publish paths.
/// </summary>
[PublicAPI]
[StructLayout(LayoutKind.Auto)]
public readonly record struct RetryDecision
{
    /// <summary>The high-level outcome of a retry computation.</summary>
    public enum Kind
    {
        /// <summary>
        /// Stop retrying immediately. The failure is permanent (non-transient exception) or the
        /// operation was cancelled. The exhausted callback does NOT fire.
        /// </summary>
        Stop,

        /// <summary>
        /// The retry budget is exhausted. The failure was transient but no further attempt is
        /// allowed (max attempts reached, or the strategy returned no delay).
        /// </summary>
        Exhausted,

        /// <summary>Retry after <see cref="Delay"/>.</summary>
        Continue,
    }

    /// <summary>Gets the high-level outcome.</summary>
    public Kind Outcome { get; init; }

    /// <summary>Gets the delay before the next attempt. Meaningful only when <see cref="Outcome"/> is <see cref="Kind.Continue"/>.</summary>
    public TimeSpan Delay { get; init; }

    /// <summary>The retry pipeline should stop. Used for permanent failures and cancellation.</summary>
    public static RetryDecision Stop { get; } = new() { Outcome = Kind.Stop };

    /// <summary>The retry budget is exhausted; the exhausted callback (if any) should fire.</summary>
    public static RetryDecision Exhausted { get; } = new() { Outcome = Kind.Exhausted };

    /// <summary>Continue retrying after <paramref name="delay"/>.</summary>
    public static RetryDecision Continue(TimeSpan delay) => new() { Outcome = Kind.Continue, Delay = delay };

    /// <summary>True when the decision is <see cref="Kind.Continue"/>.</summary>
    public bool ShouldRetry => Outcome == Kind.Continue;
}
