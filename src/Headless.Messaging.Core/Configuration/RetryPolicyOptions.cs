// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Messaging.Messages;
using Headless.Messaging.Retry;

namespace Headless.Messaging.Configuration;

/// <summary>
/// Configures message retry behavior across inline and persisted retry paths.
/// </summary>
/// <remarks>
/// Total observable delivery attempts = <c>(MaxInlineRetries + 1) × (MaxPersistedRetries + 1)</c>.
/// Inline retries burst on each persisted pickup. To disable retry entirely set both
/// <see cref="MaxInlineRetries"/> and <see cref="MaxPersistedRetries"/> to 0.
/// </remarks>
[PublicAPI]
public sealed class RetryPolicyOptions
{
    /// <summary>
    /// Gets or sets the maximum number of extra inline attempts performed on each delivery
    /// (after the first attempt) before persisting the message for a later pickup. Default is 2.
    /// </summary>
    /// <remarks>
    /// With <c>MaxInlineRetries = 2</c> each pickup runs up to 3 attempts total before persisting.
    /// </remarks>
    public int MaxInlineRetries { get; set; } = 2;

    /// <summary>
    /// Gets or sets the maximum number of persisted-retry pickups the retry processor will
    /// attempt for a failed message. Default is 15.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Persisted pickups burst inline retries on each pickup. Total observable attempts =
    /// <c>(MaxInlineRetries + 1) × (MaxPersistedRetries + 1)</c>; with defaults this is
    /// <c>(2 + 1) × (15 + 1) = 48</c> attempts before <see cref="OnExhausted"/> fires.
    /// </para>
    /// <para>
    /// Set to 0 to disable persisted retry (failure becomes terminal after inline budget is consumed).
    /// </para>
    /// </remarks>
    public int MaxPersistedRetries { get; set; } = 15;

    /// <summary>
    /// Gets or sets the delay applied to <c>NextRetryAt</c> when a message is first stored.
    /// Default is 30 seconds.
    /// </summary>
    /// <remarks>
    /// On initial store the message's <c>NextRetryAt</c> is set to <c>UtcNow + InitialDispatchGrace</c>.
    /// The persisted retry processor will not pick the row up until that timestamp elapses,
    /// which gives the normal dispatch + inline-retry path room to complete first. After that grace
    /// window the processor treats the row as crash-recovery work (a never-dispatched or
    /// dispatch-crashed message) and picks it up.
    /// Lower this for faster crash-recovery; raise it to reduce storage scan pressure during
    /// burst publishes.
    /// </remarks>
    public TimeSpan InitialDispatchGrace { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets the backoff strategy used to compute per-attempt delay.
    /// Defaults to exponential backoff.
    /// </summary>
    public IRetryBackoffStrategy BackoffStrategy { get; set; } = new ExponentialBackoffStrategy();

    /// <summary>
    /// Copies all properties of this instance to <paramref name="target"/>.
    /// </summary>
    internal void CopyTo(RetryPolicyOptions target)
    {
        target.MaxInlineRetries = MaxInlineRetries;
        target.MaxPersistedRetries = MaxPersistedRetries;
        target.InitialDispatchGrace = InitialDispatchGrace;
        target.BackoffStrategy = BackoffStrategy;
        target.OnExhausted = OnExhausted;
    }

    /// <summary>
    /// Gets or sets the callback invoked once retry attempts are exhausted
    /// (the multiplicative budget <c>(MaxInlineRetries + 1) × (MaxPersistedRetries + 1)</c>
    /// is consumed, or the configured <see cref="BackoffStrategy"/> signals no further delay).
    /// Permanent failures (for example argument validation errors or subscriber-not-found) and
    /// cancellations short-circuit the retry budget entirely and do not invoke this callback.
    /// The callback runs inside the live dispatch scope carried by
    /// <see cref="FailedInfo.ServiceProvider"/> and is awaited before the dispatch scope is disposed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Delivery is <b>at-least-once</b>: under broker redelivery, partial failures, or crash-recover
    /// this callback MAY fire more than once for the same message. The handler MUST be idempotent
    /// — use <c>Message.GetId()</c> as the dedupe key. The framework makes best-effort to skip
    /// re-firing when storage proves the row is already in a terminal state, but does not
    /// guarantee single-fire under all broker semantics.
    /// </para>
    /// <para>
    /// The supplied <see cref="CancellationToken"/> reflects host shutdown — handle it to fail
    /// fast on stop. Throwing from the callback is caught and logged; it does not crash the
    /// dispatch loop.
    /// </para>
    /// </remarks>
    public Func<FailedInfo, CancellationToken, Task>? OnExhausted { get; set; }
}

internal sealed class RetryPolicyOptionsValidator : AbstractValidator<RetryPolicyOptions>
{
    public RetryPolicyOptionsValidator()
    {
        RuleFor(x => x.MaxInlineRetries).GreaterThanOrEqualTo(0);
        RuleFor(x => x.MaxPersistedRetries).GreaterThanOrEqualTo(0);
        RuleFor(x => x.InitialDispatchGrace)
            .GreaterThan(TimeSpan.Zero)
            .WithMessage("InitialDispatchGrace must be greater than zero.")
            .LessThanOrEqualTo(TimeSpan.FromHours(1))
            .WithMessage("InitialDispatchGrace must not exceed 1 hour.");
        RuleFor(x => x.BackoffStrategy).NotNull();
    }
}
