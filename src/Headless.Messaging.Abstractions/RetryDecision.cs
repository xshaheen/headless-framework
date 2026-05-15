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
    [PublicAPI]
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
        /// <remarks>
        /// This value is emitted exclusively by the framework when the max-attempts budget is
        /// consumed. <see cref="IRetryBackoffStrategy"/> implementations should NOT return this
        /// value — strategies must return only <see cref="Stop"/> or <see cref="RetryDecision.Continue"/>.
        /// </remarks>
        Exhausted,

        /// <summary>Retry after <see cref="Delay"/>.</summary>
        Continue,
    }

    /// <summary>Gets the high-level outcome.</summary>
    public Kind Outcome { get; init; }

    /// <summary>Gets the delay before the next attempt. Meaningful only when <see cref="Outcome"/> is <see cref="Kind.Continue"/>.</summary>
    public TimeSpan Delay { get; init; }

    /// <summary>The retry pipeline should stop. Used for permanent failures and cancellation.</summary>
    /// <remarks>
    /// <para>
    /// Stop preserves <c>MediumMessage.Retries</c>; permanent-failure rows therefore have
    /// <c>Retries &lt;= MaxPersistedRetries</c> and remain in a terminal <c>Failed/null-NextRetryAt</c>
    /// state. The retry-pickup query excludes this combination because its
    /// <c>NextRetryAt IS NOT NULL</c> clause filters out null-NextRetryAt rows. Any change to the
    /// pickup predicate must preserve this exclusion.
    /// </para>
    /// </remarks>
    public static RetryDecision Stop { get; } = new() { Outcome = Kind.Stop };

    /// <summary>The retry budget is exhausted; the exhausted callback (if any) should fire.</summary>
    /// <remarks>
    /// <para>
    /// Both exhaustion paths — strategy-returned and framework-emitted — leave
    /// <c>MediumMessage.Retries</c> unchanged. Retries therefore reflects the number of
    /// attempts already completed, not a count that includes the terminal failing attempt.
    /// </para>
    /// </remarks>
    public static RetryDecision Exhausted { get; } = new() { Outcome = Kind.Exhausted };

    /// <summary>Continue retrying after <paramref name="delay"/>.</summary>
    public static RetryDecision Continue(TimeSpan delay) => new() { Outcome = Kind.Continue, Delay = delay };

    /// <summary>True when the decision is <see cref="Kind.Continue"/>.</summary>
    public bool ShouldRetry => Outcome == Kind.Continue;
}
