// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Exceptions;
using Headless.Jobs.Models;
using Headless.Reliability;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace Headless.Jobs;

/// <summary>
/// Runs a job's attempts in process. The job's failure policy classifies each failure and paces retries the row stores
/// no interval for; the row's durable <c>Retries</c> budget decides how many retries remain. No host setting caps or
/// overrides either, so the policy a job declares is the policy it runs with.
/// </summary>
internal sealed class JobsRetryPipeline
{
    private static readonly ResiliencePropertyKey<ExecutionState> _ExecutionKey = new("headless.jobs.retry");
    private readonly JobFunctionRegistry _registry;
    private readonly ILogger _logger;
    private readonly ResiliencePipeline _pipeline;

    public JobsRetryPipeline(JobFunctionRegistry registry, TimeProvider timeProvider, ILogger logger)
    {
        _registry = registry;
        _logger = logger;

        // Polly only sequences the attempts: the retry count is bounded by the row budget in ShouldHandle and every
        // delay comes from DelayGenerator, so the attempt cap and base delay here are deliberately inert.
        _pipeline = new ResiliencePipelineBuilder { TimeProvider = timeProvider }
            .AddRetry(
                new RetryStrategyOptions
                {
                    MaxRetryAttempts = int.MaxValue,
                    Delay = TimeSpan.Zero,
                    BackoffType = DelayBackoffType.Constant,
                    UseJitter = false,
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
        CancellationToken cancellationToken
    )
    {
        var resilienceContext = ResilienceContextPool.Shared.Get(cancellationToken);
        resilienceContext.Properties.Set(
            _ExecutionKey,
            new ExecutionState(job, job.RetryCount, _registry.GetFailurePolicy(job.FunctionName), attempt, onRetry)
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

    private ValueTask<bool> _ShouldHandleAsync(RetryPredicateArguments<object> args)
    {
        // Cancellation belongs to the executor (durable cancel, shutdown, lease loss, or a foreign token), and a
        // TerminateExecutionException is the handler choosing its own terminal status; neither is a retryable failure.
        var exception = args.Outcome.Exception;
        if (exception is null or OperationCanceledException or TerminateExecutionException)
        {
            return ValueTask.FromResult(false);
        }

        var execution = args.Context.Properties.GetValue(_ExecutionKey, null!);
        if (execution.Policy.ShouldFail(exception, out var ruleException))
        {
            if (ruleException is not null)
            {
                _logger.LogJobFailRuleThrew(ruleException, execution.Job.JobId, execution.Job.FunctionName);
            }

            return ValueTask.FromResult(false);
        }

        return ValueTask.FromResult(execution.StartingRetryCount + args.AttemptNumber < execution.Job.Retries);
    }

    private static ValueTask<TimeSpan?> _DelayAsync(RetryDelayGeneratorArguments<object> args)
    {
        var execution = args.Context.Properties.GetValue(_ExecutionKey, null!);

        // The durable count, not Polly's in-process attempt number, indexes the schedule, so a run resumed after a
        // crash continues where the previous process stopped.
        var retryIndex = execution.StartingRetryCount + args.AttemptNumber;
        if (execution.Job.RetryIntervals is { Length: > 0 } intervals)
        {
            // RetryIntervals is an unvalidated public int[] on the job entity, so a negative value (a plausible typo)
            // would otherwise reach Polly as a negative TimeSpan and break the retry mechanism itself.
            var seconds = intervals[Math.Min(retryIndex, intervals.Length - 1)];
            return ValueTask.FromResult<TimeSpan?>(TimeSpan.FromSeconds(Math.Max(seconds, 0)));
        }

        return ValueTask.FromResult<TimeSpan?>(_PolicyDelay(execution.Policy, retryIndex));
    }

    // A row that stores no intervals (a manager-added row, or a call that set only a retry count) is paced by the
    // job's policy: its immediate retries run back-to-back and the rest wait the policy's jittered delayed delay. A
    // budget larger than the policy keeps waiting the delay the doubling reached, which the policy caps.
    private static TimeSpan _PolicyDelay(FailurePolicyDefinition policy, int retryIndex)
    {
        var delayedAttempt = retryIndex - policy.ImmediateRetries + 1;
        if (delayedAttempt < 1 || policy.DelayedRetries == 0)
        {
            return TimeSpan.Zero;
        }

        return policy.GetDelayedRetryDelay(delayedAttempt);
    }

    private static async ValueTask _OnRetryAsync(OnRetryArguments<object> args)
    {
        var execution = args.Context.Properties.GetValue(_ExecutionKey, null!);
        var retryCount = execution.StartingRetryCount + args.AttemptNumber + 1;
        await execution
            .OnRetry(retryCount, args.Outcome.Exception!, args.Context.CancellationToken)
            .ConfigureAwait(false);
    }

    private sealed record ExecutionState(
        JobExecutionState Job,
        int StartingRetryCount,
        FailurePolicyDefinition Policy,
        Func<int, CancellationToken, ValueTask> Attempt,
        Func<int, Exception, CancellationToken, ValueTask> OnRetry
    );
}
