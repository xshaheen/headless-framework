// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Polly;
using Polly.Retry;

namespace Headless.Messaging.Retry;

/// <summary>
/// Runs one delivery's in-process retry burst and hands each retryable failure to the caller, which persists the
/// state and decides whether the burst goes on.
/// </summary>
/// <remarks>
/// The publish and consume pipelines differ only in how a failure is classified, how retries are spaced, and whether a
/// user observer runs, so both compose this one execution loop instead of carrying a copy each.
/// </remarks>
/// <typeparam name="TContext">The per-execution data the owning pipeline's hooks read.</typeparam>
internal sealed class MessagingRetryEngine<TContext>
{
    private static readonly ResiliencePropertyKey<ExecutionState> _ExecutionKey = new("headless.messaging.retry");

    private readonly Func<
        Exception,
        RetryPredicateArguments<MessagingRetryAttempt>,
        ExecutionState,
        ValueTask<bool>
    > _classify;

    private readonly Func<Exception, OnRetryArguments<MessagingRetryAttempt>, ExecutionState, ValueTask>? _observe;
    private readonly ResiliencePipeline<MessagingRetryAttempt> _pipeline;

    /// <summary>Builds the reusable retry pipeline around the owning pipeline's spacing and hooks.</summary>
    /// <param name="strategy">
    /// The retry spacing. The engine owns <c>ShouldHandle</c> and <c>OnRetry</c> and overwrites them.
    /// </param>
    /// <param name="timeProvider">The clock the retry delays run on.</param>
    /// <param name="classify">Decides whether a failure that is neither completed nor bypassed is retried.</param>
    /// <param name="observe">Runs after the caller chose to continue the burst; <see langword="null"/> for none.</param>
    public MessagingRetryEngine(
        RetryStrategyOptions<MessagingRetryAttempt> strategy,
        TimeProvider timeProvider,
        Func<Exception, RetryPredicateArguments<MessagingRetryAttempt>, ExecutionState, ValueTask<bool>> classify,
        Func<Exception, OnRetryArguments<MessagingRetryAttempt>, ExecutionState, ValueTask>? observe
    )
    {
        _classify = classify;
        _observe = observe;
        strategy.ShouldHandle = _ShouldHandleAsync;
        strategy.OnRetry = _OnRetryAsync;

#pragma warning disable RS0030 // Fix makes code worse: the burst paces the user's configured retry policy and hands off to the persisted retry processor on the app clock; a system-clock wait would put one step of that schedule on a second clock.
        _pipeline = new ResiliencePipelineBuilder<MessagingRetryAttempt> { TimeProvider = timeProvider }
#pragma warning restore RS0030
            .AddRetry(strategy)
            .Build();
    }

    public static ExecutionState GetState(ResilienceContext context)
    {
        return context.Properties.GetValue(_ExecutionKey, null!);
    }

    public async Task<OperateResult> ExecuteAsync(
        Func<int, CancellationToken, Task<MessagingRetryAttempt>> attempt,
        Func<int, Exception, TimeSpan, bool, CancellationToken, Task<bool>> onRetry,
        Func<int, Exception, CancellationToken, Task> onNonRetryable,
        TContext executionContext,
        CancellationToken cancellationToken
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
        var state = new ExecutionState(onRetry, pipelineCts, executionContext);
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

        return await _classify(exception, args, GetState(args.Context)).ConfigureAwait(false);
    }

    private async ValueTask _OnRetryAsync(OnRetryArguments<MessagingRetryAttempt> args)
    {
        var state = GetState(args.Context);
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

        if (continueInline && _observe is not null)
        {
            await _observe(exception, args, state).ConfigureAwait(false);
        }

        if (!continueInline)
        {
            state.StopRequested = true;
            await state.PipelineCts.CancelAsync().ConfigureAwait(false);
        }
    }

    internal sealed class ExecutionState(
        Func<int, Exception, TimeSpan, bool, CancellationToken, Task<bool>> onRetry,
        CancellationTokenSource pipelineCts,
        TContext context
    )
    {
        public TContext Context { get; } = context;
        public Func<int, Exception, TimeSpan, bool, CancellationToken, Task<bool>> OnRetry { get; } = onRetry;
        public CancellationTokenSource PipelineCts { get; } = pipelineCts;
        public int AttemptNumber { get; set; }
        public int LastAttemptNumber { get; set; }
        public MessagingRetryAttempt LastAttempt { get; set; }
        public bool StateWritten { get; set; }
        public bool StopRequested { get; set; }

        /// <summary>Set when the user retry strategy threw while classifying or spacing the current attempt.</summary>
        public bool StrategyFailed { get; set; }
    }
}
