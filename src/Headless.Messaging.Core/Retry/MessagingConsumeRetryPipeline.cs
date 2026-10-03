// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Configuration;
using Polly;
using Polly.Retry;

namespace Headless.Messaging.Retry;

/// <summary>
/// Runs one consume's in-process retry burst: immediate retries, classified by a classifier each execution supplies.
/// </summary>
/// <remarks>
/// The consume pipeline ignores <see cref="RetryPolicyOptions.RetryStrategy"/>. Immediate retries run back-to-back, and
/// each execution supplies its own classifier, because the classification belongs to the consumer's failure policy and
/// one pipeline instance serves every consumer.
/// </remarks>
internal sealed class MessagingConsumeRetryPipeline
{
    private readonly MessagingRetryEngine<Func<Exception, bool>> _engine;

    public MessagingConsumeRetryPipeline(TimeProvider timeProvider)
    {
        var strategy = new RetryStrategyOptions<MessagingRetryAttempt>
        {
            // The durable InlineAttempts counter is the dynamic per-message ceiling. The
            // OnRetry callback cancels this reusable pipeline when the consumer's burst ends.
            MaxRetryAttempts = int.MaxValue,
            Delay = TimeSpan.Zero,
            BackoffType = DelayBackoffType.Constant,
            UseJitter = false,
        };

        _engine = new MessagingRetryEngine<Func<Exception, bool>>(
            strategy,
            timeProvider,
            static (exception, _, state) => ValueTask.FromResult(state.Context(exception)),
            observe: null
        );
    }

    /// <summary>Runs attempts until one completes, the classifier rejects a failure, or onRetry ends the burst.</summary>
    /// <param name="attempt">Runs one attempt, given its zero-based number within this burst.</param>
    /// <param name="onRetry">Persists a retryable failure and returns whether the burst continues in process.</param>
    /// <param name="onNonRetryable">Persists a failure <paramref name="isRetryable"/> rejected.</param>
    /// <param name="isRetryable">
    /// The consumer's classifier. It runs for every failure an attempt does not mark as bypassing classification.
    /// </param>
    /// <param name="cancellationToken">Cancels the burst.</param>
    public Task<OperateResult> ExecuteAsync(
        Func<int, CancellationToken, Task<MessagingRetryAttempt>> attempt,
        Func<int, Exception, TimeSpan, CancellationToken, Task<bool>> onRetry,
        Func<int, Exception, CancellationToken, Task> onNonRetryable,
        Func<Exception, bool> isRetryable,
        CancellationToken cancellationToken
    )
    {
        // A user strategy never runs on consume, so the strategy-failed flag is always false and is not surfaced.
        return _engine.ExecuteAsync(
            attempt,
            (attemptNumber, exception, delay, _, ct) => onRetry(attemptNumber, exception, delay, ct),
            onNonRetryable,
            isRetryable,
            cancellationToken
        );
    }
}
