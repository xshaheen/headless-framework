// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.Configuration;
using Headless.Messaging.Messages;

namespace Headless.Messaging.Retry;

/// <summary>
/// Shared retry decision logic used by consume and publish retry paths.
/// </summary>
internal static class RetryHelper
{
    /// <summary>
    /// Upper bound applied to delays returned by <see cref="IRetryBackoffStrategy.Compute"/>.
    /// Guards against negative or overflowing values that would crash <see cref="Task.Delay(TimeSpan)"/>
    /// or overflow <see cref="DateTime"/> arithmetic when computing NextRetryAt.
    /// </summary>
    private static readonly TimeSpan _MaxDelay = TimeSpan.FromHours(24);

    public static RetryDecision ComputeRetryDecision(
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

        message.Retries++;
        var remainingAttempts = policy.MaxAttempts - message.Retries;

        if (remainingAttempts <= 0)
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

    public static bool IsCancellation(Exception ex, CancellationToken cancellationToken) =>
        cancellationToken.IsCancellationRequested
        && ex is OperationCanceledException oce
        && oce.CancellationToken == cancellationToken;
}
