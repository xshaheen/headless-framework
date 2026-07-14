// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Models;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace Headless.Jobs;

internal sealed class JobsRetryPipeline
{
    private static readonly ResiliencePropertyKey<ExecutionState> _ExecutionKey = new("headless.jobs.retry");
    private readonly JobsRetryOptions _options;
    private readonly ILogger _logger;
    private readonly ResiliencePipeline _pipeline;

    public JobsRetryPipeline(JobsRetryOptions options, TimeProvider timeProvider, ILogger logger)
    {
        _options = options;
        _logger = logger;
        _pipeline = new ResiliencePipelineBuilder { TimeProvider = timeProvider }
            .AddRetry(
                new RetryStrategyOptions
                {
                    MaxRetryAttempts = int.MaxValue,
                    Delay = options.RetryStrategy.Delay,
                    BackoffType = options.RetryStrategy.BackoffType,
                    UseJitter = options.RetryStrategy.UseJitter,
                    Randomizer = options.RetryStrategy.Randomizer,
                    MaxDelay = options.RetryStrategy.MaxDelay,
                    ShouldHandle = _ShouldHandleAsync,
                    DelayGenerator = _DelayAsync,
                    OnRetry = _OnRetryAsync,
                }
            )
            .Build();
    }

    public async Task ExecuteAsync(
        JobExecutionState job,
        Func<int, CancellationToken, ValueTask> attempt,
        Func<int, Exception, CancellationToken, ValueTask> onRetry,
        Action<bool> onFailureClassified,
        CancellationToken cancellationToken
    )
    {
        var resilienceContext = ResilienceContextPool.Shared.Get(cancellationToken);
        resilienceContext.Properties.Set(
            _ExecutionKey,
            new ExecutionState(job, job.RetryCount, attempt, onRetry, onFailureClassified)
        );
        try
        {
            await _pipeline
                .ExecuteAsync(
                    static async context =>
                    {
                        var execution = context.Properties.GetValue(_ExecutionKey, null!);
                        await execution
                            .Attempt(execution.Job.RetryCount, context.CancellationToken)
                            .ConfigureAwait(false);
                    },
                    resilienceContext
                )
                .ConfigureAwait(false);
        }
        finally
        {
            ResilienceContextPool.Shared.Return(resilienceContext);
        }
    }

    private async ValueTask<bool> _ShouldHandleAsync(RetryPredicateArguments<object> args)
    {
        var execution = args.Context.Properties.GetValue(_ExecutionKey, null!);
        var shouldHandle = await _options
            .RetryStrategy.ShouldHandle(
                new RetryPredicateArguments<object>(args.Context, args.Outcome, args.AttemptNumber)
            )
            .ConfigureAwait(false);
        execution.OnFailureClassified(shouldHandle);
        if (!shouldHandle)
        {
            return false;
        }

        if (
            execution.StartingRetryCount + args.AttemptNumber >= execution.Job.Retries
            || execution.StartingRetryCount + args.AttemptNumber >= _options.RetryStrategy.MaxRetryAttempts
        )
        {
            return false;
        }

        return true;
    }

    private async ValueTask<TimeSpan?> _DelayAsync(RetryDelayGeneratorArguments<object> args)
    {
        var execution = args.Context.Properties.GetValue(_ExecutionKey, null!);
        var retryIndex = execution.StartingRetryCount + args.AttemptNumber;
        if (execution.Job.RetryIntervals is { Length: > 0 } intervals)
        {
            // Clamp per-row intervals exactly like the DelayGenerator branch below. RetryIntervals is an
            // unvalidated public int[] on the job entity, so a negative value (a plausible typo) would otherwise
            // reach Polly as a negative TimeSpan and turn a scheduling mistake into a hard failure of the retry
            // mechanism itself.
            return _ClampCustomDelay(TimeSpan.FromSeconds(intervals[Math.Min(retryIndex, intervals.Length - 1)]));
        }

        if (_options.RetryStrategy.DelayGenerator is not null)
        {
            var custom = await _options
                .RetryStrategy.DelayGenerator(
                    new RetryDelayGeneratorArguments<object>(args.Context, args.Outcome, args.AttemptNumber)
                )
                .ConfigureAwait(false);
            if (custom is not null)
            {
                return _ClampCustomDelay(custom.Value);
            }
        }

        // Returning null delegates built-in fixed/linear/exponential and jitter calculation to Polly.
        return null;
    }

    private TimeSpan _ClampCustomDelay(TimeSpan delay)
    {
        var nonNegative = delay < TimeSpan.Zero ? TimeSpan.Zero : delay;
        return _options.RetryStrategy.MaxDelay is { } maxDelay && nonNegative > maxDelay ? maxDelay : nonNegative;
    }

    private async ValueTask _OnRetryAsync(OnRetryArguments<object> args)
    {
        var execution = args.Context.Properties.GetValue(_ExecutionKey, null!);
        var exception = args.Outcome.Exception!;
        var retryCount = execution.StartingRetryCount + args.AttemptNumber + 1;
        await execution.OnRetry(retryCount, exception, args.Context.CancellationToken).ConfigureAwait(false);

        if (_options.RetryStrategy.OnRetry is null)
        {
            return;
        }

        try
        {
            await _options
                .RetryStrategy.OnRetry(
                    new OnRetryArguments<object>(
                        args.Context,
                        args.Outcome,
                        args.AttemptNumber,
                        args.RetryDelay,
                        args.Duration
                    )
                )
                .ConfigureAwait(false);
        }
        catch (Exception observerException)
        {
            _logger.LogJobsRetryObserverFailed(observerException, execution.Job.JobId, execution.Job.FunctionName);
        }
    }

    private sealed record ExecutionState(
        JobExecutionState Job,
        int StartingRetryCount,
        Func<int, CancellationToken, ValueTask> Attempt,
        Func<int, Exception, CancellationToken, ValueTask> OnRetry,
        Action<bool> OnFailureClassified
    );
}
