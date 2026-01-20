// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Messages.Retry;

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
        return _interval;
    }

    /// <inheritdoc />
    public bool ShouldRetry(Exception exception)
    {
        // Always retry with fixed interval (maintains legacy behavior)
        return true;
    }
}
