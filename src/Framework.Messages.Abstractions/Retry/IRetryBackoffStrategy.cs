// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Messages.Retry;

/// <summary>
/// Defines a strategy for calculating retry delays and determining retry eligibility for failed messages.
/// </summary>
public interface IRetryBackoffStrategy
{
    /// <summary>
    /// Calculates the next retry delay based on the current retry attempt.
    /// </summary>
    /// <param name="retryAttempt">The current retry attempt number (0-based).</param>
    /// <param name="exception">The exception that caused the failure, if available.</param>
    /// <returns>The delay before the next retry attempt, or <c>null</c> if retries should be stopped.</returns>
    TimeSpan? GetNextDelay(int retryAttempt, Exception? exception = null);

    /// <summary>
    /// Determines whether a failure should be retried based on the exception.
    /// </summary>
    /// <param name="exception">The exception that caused the failure.</param>
    /// <returns><c>true</c> if the failure is transient and should be retried; otherwise, <c>false</c>.</returns>
    bool ShouldRetry(Exception exception);
}
