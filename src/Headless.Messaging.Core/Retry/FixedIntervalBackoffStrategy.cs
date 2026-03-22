// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Exceptions;

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
    public TimeSpan? GetNextDelay(int retryAttempt, Exception? exception = null)
    {
        if (exception != null && !ShouldRetry(exception))
        {
            return null;
        }

        return _interval;
    }

    /// <inheritdoc />
    public bool ShouldRetry(Exception exception)
    {
        // Permanent failures - don't retry
        return exception switch
        {
            SubscriberNotFoundException => false,
            ArgumentNullException => false,
            ArgumentException => false,
            InvalidOperationException => false,
            NotSupportedException => false,
            _ => true, // All other exceptions are considered transient
        };
    }
}
