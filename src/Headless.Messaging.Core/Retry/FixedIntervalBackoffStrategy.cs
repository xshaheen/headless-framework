// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Retry;

/// <summary>
/// Implements a fixed interval backoff strategy for message retry delays.
/// </summary>
public sealed class FixedIntervalBackoffStrategy : IRetryBackoffStrategy
{
    private readonly TimeSpan _interval;

    /// <summary>
    /// Initializes a new instance of the <see cref="FixedIntervalBackoffStrategy"/> class.
    /// </summary>
    /// <param name="interval">The fixed interval between retries.</param>
    public FixedIntervalBackoffStrategy(TimeSpan interval)
    {
        _interval = interval;
    }

    /// <inheritdoc />
    public RetryDecision Compute(int retryCount, Exception exception)
    {
        if (RetryExceptionClassifier.IsPermanent(exception))
        {
            return RetryDecision.Stop;
        }

        return RetryDecision.Continue(_interval);
    }
}
