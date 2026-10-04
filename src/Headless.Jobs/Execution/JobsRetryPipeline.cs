// Copyright (c) Mahmoud Shaheen. All rights reserved.

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

    // The wait before a retry that neither a stored interval nor the policy's delayed tier paces; it is the delay every
    // retry waited before jobs carried a failure policy.
    private static readonly TimeSpan _UnpacedRetryDelay = TimeSpan.FromSeconds(30);
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
        // Cancellation of this execution's token belongs to the executor (durable cancel, shutdown, lease loss), and a
        // TerminateExecutionException is the handler choosing its own terminal status; neither is a retryable failure.
        // A cancellation the handler raised while that token is live (an HttpClient timeout, its own CancelAfter) is
        // an ordinary failure: the fail rules see it and the budget retries it.
        var exception = args.Outcome.Exception;
        if (
            exception is null or TerminateExecutionException
            || (exception is OperationCanceledException && args.Context.CancellationToken.IsCancellationRequested)
        )
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
            // would otherwise reach Polly as a negative TimeSpan and break the retry mechanism itself, and an
            // oversized one would park the row far past any delay a failure policy may declare.
            var seconds = intervals[Math.Min(retryIndex, intervals.Length - 1)];
            var delay = TimeSpan.FromSeconds(Math.Max(seconds, 0));
            return ValueTask.FromResult<TimeSpan?>(
                delay > FailurePolicyDefinition.MaxDelayLimit ? FailurePolicyDefinition.MaxDelayLimit : delay
            );
        }

        return ValueTask.FromResult<TimeSpan?>(_PolicyDelay(execution.Policy, retryIndex));
    }

    // A row that stores no intervals (a manager-added row, or a call that set only a retry count) is paced by the
    // job's policy: its immediate retries run back-to-back and the rest wait the policy's jittered delayed delay. A
    // budget larger than the policy keeps waiting the delay the doubling reached, which the policy caps. A retry past
    // the immediate tier of a policy with no delayed tier has no policy delay at all, so it waits the framework
    // fallback rather than hammering a failing dependency back-to-back for the rest of the row's budget.
    private static TimeSpan _PolicyDelay(FailurePolicyDefinition policy, int retryIndex)
    {
        var delayedAttempt = retryIndex - policy.ImmediateRetries + 1;
        if (delayedAttempt < 1)
        {
            return TimeSpan.Zero;
        }

        return policy.DelayedRetries == 0 ? _UnpacedRetryDelay : policy.GetDelayedRetryDelay(delayedAttempt);
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
