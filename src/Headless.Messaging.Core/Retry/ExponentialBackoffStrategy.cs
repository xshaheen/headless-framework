// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Retry;

/// <summary>
/// Implements exponential backoff with jitter for message retry delays.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="ExponentialBackoffStrategy"/> class.
/// </remarks>
/// <param name="initialDelay">The initial delay for the first retry. Defaults to 1 second.</param>
/// <param name="maxDelay">The maximum delay between retries. Defaults to 5 minutes.</param>
/// <param name="backoffMultiplier">The multiplier applied to the delay for each retry. Defaults to 2.0 (exponential).</param>
[PublicAPI]
public sealed class ExponentialBackoffStrategy(
    TimeSpan? initialDelay = null,
    TimeSpan? maxDelay = null,
    double backoffMultiplier = 2.0
) : IRetryBackoffStrategy
{
    private readonly TimeSpan _initialDelay = initialDelay ?? TimeSpan.FromSeconds(1);
    private readonly TimeSpan _maxDelay = maxDelay ?? TimeSpan.FromMinutes(5);

    /// <inheritdoc />
    public RetryDecision Compute(int persistedRetryCount, int inlineRetryCount, Exception exception)
    {
        if (RetryExceptionClassifier.IsPermanent(exception))
        {
            return RetryDecision.Stop;
        }

        // Calculate exponential delay: initialDelay * (backoffMultiplier ^ persistedRetryCount)
        var exponentialDelay = _initialDelay.TotalMilliseconds * Math.Pow(backoffMultiplier, persistedRetryCount);

        // Cap at max delay
        var delayMs = Math.Min(exponentialDelay, _maxDelay.TotalMilliseconds);

        // Add jitter (±25% randomization to prevent thundering herd), then re-clamp so the
        // final delay never exceeds _maxDelay — otherwise the +25% upside silently violates
        // the documented "maximum delay between retries" contract.
        var jitter = ((Random.Shared.NextDouble() * 0.5) - 0.25) * delayMs;
        var finalDelayMs = Math.Clamp(delayMs + jitter, 0, _maxDelay.TotalMilliseconds);

        return RetryDecision.Continue(TimeSpan.FromMilliseconds(finalDelayMs));
    }
}
