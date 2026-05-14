// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Configuration;
using Headless.Messaging.Messages;

namespace Headless.Messaging.Retry;

/// <summary>
/// Shared retry decision logic used by consume and publish retry paths.
/// </summary>
internal static class RetryHelper
{
    public static RetryDecision ComputeRetryDecision(
        MediumMessage message,
        Exception exception,
        RetryPolicyOptions policy,
        bool isCancellation
    )
    {
        if (isCancellation)
        {
            return RetryDecision.Stop;
        }

        if (!policy.BackoffStrategy.ShouldRetry(exception))
        {
            return RetryDecision.Stop;
        }

        message.Retries++;
        var remainingAttempts = policy.MaxAttempts - message.Retries;

        if (remainingAttempts <= 0)
        {
            return RetryDecision.Exhausted;
        }

        var delay = policy.BackoffStrategy.GetNextDelay(message.Retries, exception);

        return delay is null ? RetryDecision.Exhausted : RetryDecision.Continue(delay.Value);
    }

    public static bool IsCancellation(Exception ex, CancellationToken cancellationToken) =>
        ex is OperationCanceledException oce
        && (oce.CancellationToken == cancellationToken || cancellationToken.IsCancellationRequested);
}
