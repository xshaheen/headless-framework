// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

/// <summary>
/// Defines a strategy that classifies a failed delivery attempt and computes the next retry delay.
/// </summary>
public interface IRetryBackoffStrategy
{
    /// <summary>
    /// Decides what to do after a failure. Strategies fold permanent-vs-transient classification and
    /// delay computation into a single call, returning <see cref="RetryDecision.Stop"/> for
    /// non-retryable exceptions or <see cref="RetryDecision.Continue"/> with the delay otherwise.
    /// </summary>
    /// <param name="retryCount">The number of retries already performed for this message (0-based).</param>
    /// <param name="exception">The exception that caused the failure.</param>
    RetryDecision Compute(int retryCount, Exception exception);
}
