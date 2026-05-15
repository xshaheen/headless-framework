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
/// framework's signal — emitted by <see cref="RecordAttemptAndComputeDecision"/> when the max-attempts
/// budget is consumed. Strategies that return <see cref="RetryDecision.Kind.Exhausted"/> are
/// treated as a no-Retries-increment stop (same convention as <see cref="RetryDecision.Stop"/>).
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
    /// Computes the next retry decision for a failed delivery attempt.
    /// On <see cref="RetryDecision.Kind.Continue"/>, increments
    /// <paramref name="message"/>.<see cref="MediumMessage.Retries"/>.
    /// On <see cref="RetryDecision.Kind.Stop"/> and <see cref="RetryDecision.Kind.Exhausted"/>,
    /// leaves <c>Retries</c> unchanged — both terminal paths agree that the attempt count reflects
    /// completed retries, not the final failing attempt.
    /// Do not call more than once per failure event — each call may advance the retry counter.
    /// </summary>
    public static RetryDecision RecordAttemptAndComputeDecision(
        MediumMessage message,
        Exception exception,
        RetryPolicyOptions policy,
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

        // Check budget before committing the increment: both Exhausted paths (strategy-returned
        // and framework-emitted) must leave Retries unchanged so callers can rely on Retries
        // reflecting the number of attempts already made, not counting the terminal one.
        var nextAttemptCount = message.Retries + 1;
        var remainingAttempts = policy.MaxAttempts - nextAttemptCount;

        if (remainingAttempts <= 0)
        {
            return RetryDecision.Exhausted;
        }

        message.Retries = nextAttemptCount;

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

        var nextRetryAt =
            decision.Outcome == RetryDecision.Kind.Continue && inlineRetries + 1 > policy.MaxInlineRetries
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
