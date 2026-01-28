// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Exceptions;

namespace Headless.Messaging.Retry;

/// <summary>
/// Implements exponential backoff with jitter for message retry delays.
/// </summary>
public sealed class ExponentialBackoffStrategy : IRetryBackoffStrategy
{
    private readonly TimeSpan _initialDelay;
    private readonly TimeSpan _maxDelay;
    private readonly double _backoffMultiplier;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExponentialBackoffStrategy"/> class.
    /// </summary>
    /// <param name="initialDelay">The initial delay for the first retry. Defaults to 1 second.</param>
    /// <param name="maxDelay">The maximum delay between retries. Defaults to 5 minutes.</param>
    /// <param name="backoffMultiplier">The multiplier applied to the delay for each retry. Defaults to 2.0 (exponential).</param>
    public ExponentialBackoffStrategy(
        TimeSpan? initialDelay = null,
        TimeSpan? maxDelay = null,
        double backoffMultiplier = 2.0
    )
    {
        _initialDelay = initialDelay ?? TimeSpan.FromSeconds(1);
        _maxDelay = maxDelay ?? TimeSpan.FromMinutes(5);
        _backoffMultiplier = backoffMultiplier;
    }

    /// <inheritdoc />
    public TimeSpan? GetNextDelay(int retryAttempt, Exception? exception = null)
    {
        if (exception != null && !ShouldRetry(exception))
        {
            return null;
        }

        // Calculate exponential delay: initialDelay * (backoffMultiplier ^ retryAttempt)
        var exponentialDelay = _initialDelay.TotalMilliseconds * Math.Pow(_backoffMultiplier, retryAttempt);

        // Cap at max delay
        var delayMs = Math.Min(exponentialDelay, _maxDelay.TotalMilliseconds);

        // Add jitter (±25% randomization to prevent thundering herd)
        var jitter = ((Random.Shared.NextDouble() * 0.5) - 0.25) * delayMs;
        var finalDelayMs = Math.Max(0, delayMs + jitter);

        return TimeSpan.FromMilliseconds(finalDelayMs);
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
