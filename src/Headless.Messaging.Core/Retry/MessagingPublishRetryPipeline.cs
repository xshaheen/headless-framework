// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace Headless.Messaging.Retry;

/// <summary>
/// Runs one publish's in-process retry burst, classified and spaced by <see cref="RetryPolicyOptions.RetryStrategy"/>.
/// </summary>
/// <remarks>
/// The strategy's <c>ShouldHandle</c> classifies, and its delay settings space the retries. A throwing user predicate
/// or delay generator does not abort the burst: the failure is retried with no delay and the caller's onRetry is told
/// the strategy failed, so it can force exhaustion instead of trusting a broken policy.
/// </remarks>
internal sealed class MessagingPublishRetryPipeline
{
    private static readonly TimeSpan _MaxCustomDelay = TimeSpan.FromHours(24);
    private readonly RetryPolicyOptions _policy;
    private readonly ILogger _logger;
    private readonly MessagingRetryEngine<Guid> _engine;

    public MessagingPublishRetryPipeline(RetryPolicyOptions policy, TimeProvider timeProvider, ILogger logger)
    {
        _policy = policy;
        _logger = logger;
        var configured = policy.RetryStrategy;
        var strategy = new RetryStrategyOptions<MessagingRetryAttempt>
        {
            // The durable InlineAttempts counter is the dynamic per-message ceiling. The
            // OnRetry callback cancels this reusable pipeline when the configured burst ends.
            MaxRetryAttempts = int.MaxValue,
            Delay = configured.Delay,
            BackoffType = configured.BackoffType,
            UseJitter = configured.UseJitter,
            Randomizer = configured.Randomizer,
            MaxDelay = configured.MaxDelay,
        };
        if (configured.DelayGenerator is not null)
        {
            strategy.DelayGenerator = _DelayAsync;
        }

        _engine = new MessagingRetryEngine<Guid>(strategy, timeProvider, _ShouldRetryAsync, _ObserveAsync);
    }

    /// <summary>Runs attempts until one completes, the strategy rejects a failure, or onRetry ends the burst.</summary>
    /// <param name="attempt">Runs one attempt, given its zero-based number within this burst.</param>
    /// <param name="onRetry">
    /// Persists a retryable failure and returns whether the burst continues in process. Its <see langword="bool"/>
    /// argument is <see langword="true"/> when the user retry strategy threw for this failure.
    /// </param>
    /// <param name="onNonRetryable">Persists a failure the strategy classified as not retryable.</param>
    /// <param name="storageId">The stored message, for logging a throwing user strategy.</param>
    /// <param name="cancellationToken">Cancels the burst.</param>
    public Task<OperateResult> ExecuteAsync(
        Func<int, CancellationToken, Task<MessagingRetryAttempt>> attempt,
        Func<int, Exception, TimeSpan, bool, CancellationToken, Task<bool>> onRetry,
        Func<int, Exception, CancellationToken, Task> onNonRetryable,
        Guid storageId,
        CancellationToken cancellationToken
    )
    {
        return _engine.ExecuteAsync(attempt, onRetry, onNonRetryable, storageId, cancellationToken);
    }

    private async ValueTask<bool> _ShouldRetryAsync(
        Exception exception,
        RetryPredicateArguments<MessagingRetryAttempt> args,
        MessagingRetryEngine<Guid>.ExecutionState state
    )
    {
        try
        {
            return await _policy
                .RetryStrategy.ShouldHandle(
                    new RetryPredicateArguments<object>(
                        args.Context,
                        Outcome.FromException<object>(exception),
                        args.AttemptNumber
                    )
                )
                .ConfigureAwait(false);
        }
        catch (Exception strategyException)
        {
            state.StrategyFailed = true;
            _logger.RetryStrategyThrew(strategyException, state.Context, strategyException.GetType().Name);
            return true;
        }
    }

    private async ValueTask<TimeSpan?> _DelayAsync(RetryDelayGeneratorArguments<MessagingRetryAttempt> args)
    {
        var exception = args.Outcome.Result.Result.Exception;
        TimeSpan? generated;
        try
        {
            generated = await _policy
                .RetryStrategy.DelayGenerator!(
                    new RetryDelayGeneratorArguments<object>(
                        args.Context,
                        exception is null
                            ? Outcome.FromResult<object>(value: null)
                            : Outcome.FromException<object>(exception),
                        args.AttemptNumber
                    )
                )
                .ConfigureAwait(false);
        }
        catch (Exception strategyException)
        {
            var state = MessagingRetryEngine<Guid>.GetState(args.Context);
            state.StrategyFailed = true;
            _logger.RetryStrategyThrew(strategyException, state.Context, strategyException.GetType().Name);
            return TimeSpan.Zero;
        }

        if (generated is null)
        {
            return null;
        }

        var delay = generated.Value < TimeSpan.Zero ? TimeSpan.Zero : generated.Value;
        var configuredCap = _policy.RetryStrategy.MaxDelay ?? _MaxCustomDelay;
        var cap = configuredCap < _MaxCustomDelay ? configuredCap : _MaxCustomDelay;
        return delay > cap ? cap : delay;
    }

    private async ValueTask _ObserveAsync(
        Exception exception,
        OnRetryArguments<MessagingRetryAttempt> args,
        MessagingRetryEngine<Guid>.ExecutionState state
    )
    {
        if (_policy.RetryStrategy.OnRetry is not { } userOnRetry)
        {
            return;
        }

        // Contain observer failures: a throwing user OnRetry must not abort the in-flight
        // inline burst (the row is already persisted as Scheduled with the lease held, so an
        // escaped throw would strand it until DispatchTimeout). Mirrors JobsRetryPipeline.
        try
        {
            await userOnRetry(
                    new OnRetryArguments<object>(
                        args.Context,
                        Outcome.FromException<object>(exception),
                        args.AttemptNumber,
                        args.RetryDelay,
                        args.Duration
                    )
                )
                .ConfigureAwait(false);
        }
        catch (Exception observerException)
        {
            _logger.RetryStrategyThrew(observerException, state.Context, observerException.GetType().Name);
        }
    }
}
