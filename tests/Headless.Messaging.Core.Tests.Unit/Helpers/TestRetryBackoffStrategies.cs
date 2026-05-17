// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Internal;
using Headless.Messaging.Retry;

namespace Tests.Helpers;

/// <summary>Zero-delay continue strategy — every failure retries immediately.</summary>
internal sealed class ZeroDelayRetryBackoffStrategy : IRetryBackoffStrategy
{
    public RetryDecision Compute(int retryCount, int inlineRetryCount, Exception exception) =>
        RetryDecision.Continue(TimeSpan.Zero);
}

/// <summary>Fixed-delay continue strategy — every failure retries after <paramref name="delay"/>.</summary>
internal sealed class FixedDelayRetryBackoffStrategy(TimeSpan delay) : IRetryBackoffStrategy
{
    public RetryDecision Compute(int retryCount, int inlineRetryCount, Exception exception) =>
        RetryDecision.Continue(delay);
}

/// <summary>
/// Treats <see cref="ArgumentException"/> as permanent and everything else as retryable with zero delay.
/// Unwraps <see cref="SubscriberExecutionFailedException"/> to reach the original handler exception
/// before classifying.
/// </summary>
internal sealed class PermanentForArgumentExceptionStrategy : IRetryBackoffStrategy
{
    public RetryDecision Compute(int retryCount, int inlineRetryCount, Exception exception)
    {
        var inner = exception is SubscriberExecutionFailedException { InnerException: { } i } ? i : exception;
        return inner is ArgumentException ? RetryDecision.Stop : RetryDecision.Continue(TimeSpan.Zero);
    }
}
