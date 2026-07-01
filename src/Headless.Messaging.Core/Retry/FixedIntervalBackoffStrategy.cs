// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Retry;

/// <summary>Implements a fixed interval backoff strategy for message retry delays.</summary>
/// <remarks>Initializes a new instance of the <see cref="FixedIntervalBackoffStrategy"/> class.</remarks>
/// <param name="interval">The fixed interval between retries.</param>
[PublicAPI]
public sealed class FixedIntervalBackoffStrategy(TimeSpan interval) : IRetryBackoffStrategy
{
    /// <inheritdoc />
    public RetryDecision Compute(int persistedRetryCount, int inlineRetryCount, Exception exception)
    {
        if (RetryExceptionClassifier.IsPermanent(exception))
        {
            return RetryDecision.Stop;
        }

        return RetryDecision.Continue(interval);
    }
}
