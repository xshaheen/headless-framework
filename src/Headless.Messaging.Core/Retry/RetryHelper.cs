// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;

namespace Headless.Messaging.Retry;

/// <summary>
/// Shared retry decision logic used by consume and publish retry paths.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IRetryBackoffStrategy.Compute"/> should only return <see cref="RetryDecision.Stop"/>
/// or <see cref="RetryDecision.Continue"/>. <see cref="RetryDecision.Exhausted"/> is the
/// framework's signal — emitted by <see cref="RecordAttemptAndComputeDecision"/> when the persisted-retry
/// budget is consumed. Strategies that return <see cref="RetryDecision.Kind.Exhausted"/> are
/// treated as a no-Retries-increment stop (same convention as <see cref="RetryDecision.Stop"/>).
/// </para>
/// <para>
/// <c>MediumMessage.Retries</c> counts persisted-retry pickups only — inline iterations do not
/// advance it. The call site (not this helper) increments the counter when the resulting transition
/// is "persist for a later pickup". This helper is pure with respect to <see cref="MediumMessage"/>.
/// </para>
/// </remarks>
internal static class RetryHelper
{
    /// <summary>
    /// Upper bound applied to delays returned by <see cref="IRetryBackoffStrategy.Compute"/>.
    /// Guards against negative or overflowing values that would crash <see cref="Task.Delay(TimeSpan)"/>
    /// or overflow <see cref="DateTime"/> arithmetic when computing NextRetryAt.
    /// </summary>
    private static readonly TimeSpan _MaxDelay = TimeSpan.FromHours(24);

    /// <summary>
    /// Classifies a failed delivery attempt into <see cref="RetryDecision.Stop"/>,
    /// <see cref="RetryDecision.Exhausted"/>, or <see cref="RetryDecision.Continue"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This method does NOT mutate <paramref name="message"/>. The persisted-pickup counter
    /// (<c>MediumMessage.Retries</c>) is advanced by the call site after consulting
    /// <see cref="ResolveNextState"/> — the increment happens only when the transition routes through
    /// persistence (inline budget consumed AND persisted budget remains).
    /// </para>
    /// <para>
    /// <see cref="RetryDecision.Kind.Exhausted"/> is returned only when BOTH the inline budget
    /// is consumed on the current dispatch (<c>inlineRetries + 1 &gt; policy.MaxInlineRetries</c>)
    /// AND the persisted budget is consumed (<c>message.Retries &gt;= policy.MaxPersistedRetries</c>).
    /// While inline budget remains the helper returns Continue so the inline retry loop can
    /// continue burst-retrying on the current pickup before terminal.
    /// </para>
    /// </remarks>
    public static RetryDecision RecordAttemptAndComputeDecision(
        MediumMessage message,
        Exception exception,
        RetryPolicyOptions policy,
        int inlineRetries,
        bool isCancellation
    )
    {
        // Diagnostic guard: validator runs at startup only; post-startup mutation to null would
        // otherwise produce a bare NullReferenceException with no actionable context.
        Argument.IsNotNull(policy.BackoffStrategy);

        if (isCancellation)
        {
            return RetryDecision.Stop;
        }

        var decision = policy.BackoffStrategy.Compute(message.Retries, exception);

        if (decision.Outcome == RetryDecision.Kind.Stop)
        {
            return RetryDecision.Stop;
        }

        if (decision.Outcome == RetryDecision.Kind.Exhausted)
        {
            return RetryDecision.Exhausted;
        }

        // Exhaust only when both axes are consumed. The inline retry loop will burst attempts
        // up to MaxInlineRetries on each pickup; the persisted retry processor will pick the row
        // up at most MaxPersistedRetries times after the initial dispatch. The two budgets compose
        // multiplicatively.
        var hasInlineBudget = inlineRetries + 1 <= policy.MaxInlineRetries;
        if (!hasInlineBudget && message.Retries >= policy.MaxPersistedRetries)
        {
            return RetryDecision.Exhausted;
        }

        // Strategy returned Continue: clamp the delay then surface it.
        // Defensive clamp because IRetryBackoffStrategy is a public-extension point and
        // custom strategies may return negative / overflowing values that would crash
        // Task.Delay or DateTime.Add. Negative -> Zero; > 24h -> 24h.
        var clamped = decision.Delay;
        if (clamped < TimeSpan.Zero)
        {
            clamped = TimeSpan.Zero;
        }
        else if (clamped > _MaxDelay)
        {
            clamped = _MaxDelay;
        }

        return RetryDecision.Continue(clamped);
    }

    /// <summary>
    /// Returns <see langword="true"/> only when all three conditions hold: the
    /// <paramref name="cancellationToken"/> was cancelled, the exception is an
    /// <see cref="OperationCanceledException"/>, and its embedded token matches
    /// <paramref name="cancellationToken"/> exactly. Token-matching prevents treating
    /// a timeout OCE (e.g. from an inner <c>HttpClient</c> timeout) as a host-shutdown
    /// cancellation, which would suppress retries for transient failures.
    /// </summary>
    public static bool IsCancellation(Exception ex, CancellationToken cancellationToken) =>
        cancellationToken.IsCancellationRequested
        && ex is OperationCanceledException oce
        && oce.CancellationToken == cancellationToken;

    /// <summary>
    /// Derives the persistence state from a retry decision and inline-retry counters.
    /// Both the consume path (SubscribeExecutor) and the publish path (MessageSender) share
    /// identical logic; a single definition prevents the two from drifting.
    /// </summary>
    public static RetryNextState ResolveNextState(
        RetryDecision decision,
        int inlineRetries,
        RetryPolicyOptions policy,
        TimeProvider timeProvider
    )
    {
        var isInlineRetryInFlight =
            decision.Outcome == RetryDecision.Kind.Continue && inlineRetries + 1 <= policy.MaxInlineRetries;

        var nextStatus = isInlineRetryInFlight ? StatusName.Scheduled : StatusName.Failed;

        // Always persist NextRetryAt for any Continue decision so a crash during the inline
        // delay leaves the row in Scheduled/NextRetryAt state — visible to the polling query
        // on restart. During normal operation the inline loop retries before the deadline.
        var nextRetryAt =
            decision.Outcome == RetryDecision.Kind.Continue
                ? timeProvider.GetUtcNow().UtcDateTime.Add(decision.Delay)
                : (DateTime?)null;

        return new RetryNextState(isInlineRetryInFlight, nextRetryAt, nextStatus);
    }
}

/// <summary>
/// Value returned by <see cref="RetryHelper.ResolveNextState"/> describing the persistence state
/// for a single failed delivery attempt.
/// </summary>
internal readonly record struct RetryNextState(
    bool IsInlineRetryInFlight,
    DateTime? NextRetryAt,
    StatusName NextStatus
);
