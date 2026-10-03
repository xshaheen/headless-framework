// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace Headless.Messaging.Retry;

/// <summary>
/// Runs one delivery's in-process retry burst and hands each retryable failure to the caller, which persists the
/// state and decides whether the burst goes on.
/// </summary>
/// <remarks>
/// The publish pipeline is configured by <see cref="RetryPolicyOptions.RetryStrategy"/>: its <c>ShouldHandle</c>
/// classifies, and its delay settings space the retries. The consume pipeline ignores that strategy. Immediate retries
/// run back-to-back, and each execution supplies its own classifier, because the classification belongs to the
/// consumer's failure policy and one pipeline instance serves every consumer.
/// </remarks>
internal sealed class MessagingRetryPipeline
{
    private static readonly TimeSpan _MaxCustomDelay = TimeSpan.FromHours(24);
    private static readonly ResiliencePropertyKey<ExecutionState> _ExecutionKey = new("headless.messaging.retry");
    private readonly RetryPolicyOptions? _policy;
    private readonly ILogger _logger;
    private readonly ResiliencePipeline<MessagingRetryAttempt> _pipeline;

    /// <summary>Creates the consume pipeline: no delay between attempts, classification supplied per execution.</summary>
    public MessagingRetryPipeline(TimeProvider timeProvider, ILogger logger)
    {
        _logger = logger;
        var strategy = new RetryStrategyOptions<MessagingRetryAttempt>
        {
            // The durable InlineAttempts counter is the dynamic per-message ceiling. The
            // OnRetry callback cancels this reusable pipeline when the consumer's burst ends.
            MaxRetryAttempts = int.MaxValue,
            Delay = TimeSpan.Zero,
            BackoffType = DelayBackoffType.Constant,
            UseJitter = false,
            ShouldHandle = _ShouldHandleAsync,
            OnRetry = _OnRetryAsync,
        };

        _pipeline = new ResiliencePipelineBuilder<MessagingRetryAttempt> { TimeProvider = timeProvider }
            .AddRetry(strategy)
            .Build();
    }

    /// <summary>Creates the publish pipeline, classified and spaced by <paramref name="policy"/>'s retry strategy.</summary>
    public MessagingRetryPipeline(RetryPolicyOptions policy, TimeProvider timeProvider, ILogger logger)
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
            ShouldHandle = _ShouldHandleAsync,
            OnRetry = _OnRetryAsync,
        };
        if (configured.DelayGenerator is not null)
        {
            strategy.DelayGenerator = _DelayAsync;
        }

        _pipeline = new ResiliencePipelineBuilder<MessagingRetryAttempt> { TimeProvider = timeProvider }
            .AddRetry(strategy)
            .Build();
    }

    public async Task<OperateResult> ExecuteAsync(
        Func<int, CancellationToken, Task<MessagingRetryAttempt>> attempt,
        Func<int, Exception, TimeSpan, bool, CancellationToken, Task<bool>> onRetry,
        Func<int, Exception, CancellationToken, Task> onNonRetryable,
        Guid storageId,
        CancellationToken cancellationToken,
        Func<Exception, bool>? isRetryable = null
    )
    {
        if (cancellationToken.IsCancellationRequested)
        {
            // Polly correctly refuses to enter a pipeline with an already-cancelled context.
            // Messaging still needs one observation pass so transports/consumers can surface the
            // cancellation as an OperateResult without writing failure or exhaustion state.
            return (await attempt(0, cancellationToken).ConfigureAwait(false)).Result;
        }

        using var pipelineCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var state = new ExecutionState(onRetry, pipelineCts, storageId, isRetryable);
        var context = ResilienceContextPool.Shared.Get(pipelineCts.Token);
        context.Properties.Set(_ExecutionKey, state);

        try
        {
            MessagingRetryAttempt result;
            try
            {
                result = await _pipeline
                    .ExecuteAsync(
                        async resilienceContext =>
                        {
                            state.StateWritten = false;
                            state.StrategyFailed = false;
                            state.LastAttemptNumber = state.AttemptNumber++;
                            state.LastAttempt = await attempt(
                                    state.LastAttemptNumber,
                                    resilienceContext.CancellationToken
                                )
                                .ConfigureAwait(false);
                            return state.LastAttempt;
                        },
                        context
                    )
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (state.StopRequested && !cancellationToken.IsCancellationRequested)
            {
                result = state.LastAttempt;
            }

            if (!state.StateWritten && result.CanRetry && result.Result.Exception is { } nonRetryableException)
            {
                await onNonRetryable(state.LastAttemptNumber, nonRetryableException, cancellationToken)
                    .ConfigureAwait(false);
            }

            return result.Result;
        }
        finally
        {
            ResilienceContextPool.Shared.Return(context);
        }
    }

    private async ValueTask<bool> _ShouldHandleAsync(RetryPredicateArguments<MessagingRetryAttempt> args)
    {
        var attempt = args.Outcome.Result;
        if (attempt is not { CanRetry: true } || attempt.Result.Exception is not { } exception)
        {
            return false;
        }

        if (attempt.BypassClassification)
        {
            return true;
        }

        var state = args.Context.Properties.GetValue(_ExecutionKey, null!);
        if (state.IsRetryable is { } isRetryable)
        {
            return isRetryable(exception);
        }

        if (_policy is null)
        {
            throw new InvalidOperationException("The consume retry pipeline requires a per-execution classifier.");
        }

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
            _logger.RetryStrategyThrew(strategyException, state.StorageId, strategyException.GetType().Name);
            return true;
        }
    }

    private async ValueTask<TimeSpan?> _DelayAsync(RetryDelayGeneratorArguments<MessagingRetryAttempt> args)
    {
        var exception = args.Outcome.Result.Result.Exception;
        TimeSpan? generated;
        try
        {
            generated = await _policy!
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
            var state = args.Context.Properties.GetValue(_ExecutionKey, null!);
            state.StrategyFailed = true;
            _logger.RetryStrategyThrew(strategyException, state.StorageId, strategyException.GetType().Name);
            return TimeSpan.Zero;
        }

        if (generated is null)
        {
            return null;
        }

        var delay = generated.Value < TimeSpan.Zero ? TimeSpan.Zero : generated.Value;
        var configuredCap = _policy!.RetryStrategy.MaxDelay ?? _MaxCustomDelay;
        var cap = configuredCap < _MaxCustomDelay ? configuredCap : _MaxCustomDelay;
        return delay > cap ? cap : delay;
    }

    private async ValueTask _OnRetryAsync(OnRetryArguments<MessagingRetryAttempt> args)
    {
        var state = args.Context.Properties.GetValue(_ExecutionKey, null!);
        var exception = args.Outcome.Result.Result.Exception!;
        state.StateWritten = true;
        var continueInline = await state
            .OnRetry(
                state.LastAttemptNumber,
                exception,
                args.RetryDelay,
                state.StrategyFailed,
                args.Context.CancellationToken
            )
            .ConfigureAwait(false);

        if (continueInline && _policy?.RetryStrategy.OnRetry is not null)
        {
            // Contain observer failures: a throwing user OnRetry must not abort the in-flight
            // inline burst (the row is already persisted as Scheduled with the lease held, so an
            // escaped throw would strand it until DispatchTimeout). Mirrors JobsRetryPipeline.
            try
            {
                await _policy
                    .RetryStrategy.OnRetry(
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
                _logger.RetryStrategyThrew(observerException, state.StorageId, observerException.GetType().Name);
            }
        }

        if (!continueInline)
        {
            state.StopRequested = true;
            await state.PipelineCts.CancelAsync().ConfigureAwait(false);
        }
    }

    private sealed class ExecutionState(
        Func<int, Exception, TimeSpan, bool, CancellationToken, Task<bool>> onRetry,
        CancellationTokenSource pipelineCts,
        Guid storageId,
        Func<Exception, bool>? isRetryable
    )
    {
        public Func<Exception, bool>? IsRetryable { get; } = isRetryable;
        public Func<int, Exception, TimeSpan, bool, CancellationToken, Task<bool>> OnRetry { get; } = onRetry;
        public CancellationTokenSource PipelineCts { get; } = pipelineCts;
        public Guid StorageId { get; } = storageId;
        public int AttemptNumber { get; set; }
        public int LastAttemptNumber { get; set; }
        public MessagingRetryAttempt LastAttempt { get; set; }
        public bool StateWritten { get; set; }
        public bool StopRequested { get; set; }
        public bool StrategyFailed { get; set; }
    }
}

internal readonly record struct MessagingRetryAttempt(
    OperateResult Result,
    bool CanRetry,
    bool BypassClassification = false
)
{
    public static MessagingRetryAttempt Completed(OperateResult result)
    {
        return new(result, CanRetry: false);
    }

    public static MessagingRetryAttempt Retryable(OperateResult result, bool bypassClassification = false)
    {
        return new(result, CanRetry: true, bypassClassification);
    }
}
