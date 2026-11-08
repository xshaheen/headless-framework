// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Internal;
using Headless.Reliability;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace Headless.Jobs;

/// <summary>
/// Runs a job's attempts in process. The job's failure policy classifies each failure and paces retries the row stores
/// no interval for; the row's durable <c>Retries</c> budget decides how many retries remain. No host setting caps or
/// overrides either, so the policy a job declares is the policy it runs with. A run allowed to defer hands a retry
/// whose delay exceeds <see cref="DeferThreshold"/> back to the caller instead of waiting it out, so the wait happens in
/// the store and not in a worker.
/// </summary>
internal sealed class JobsRetryPipeline
{
    private static readonly ResiliencePropertyKey<ExecutionState> _ExecutionKey = new("headless.jobs.retry");

    // The wait before a retry that neither a stored interval nor the policy's delayed tier paces; it is the delay every
    // retry waited before jobs carried a failure policy.
    private static readonly TimeSpan _UnpacedRetryDelay = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The longest retry delay a deferrable run still waits in process. It matches the claim's due slack: a shorter
    /// wait costs less than the write and re-claim that would replace it, which could not bring the retry closer to
    /// its instant than the slack anyway.
    /// </summary>
    internal static TimeSpan DeferThreshold => JobsDueSlack.Duration;

    private readonly JobFunctionRegistry _registry;
    private readonly ILogger _logger;
    private readonly ResiliencePipeline _pipeline;

    public JobsRetryPipeline(JobFunctionRegistry registry, TimeProvider timeProvider, ILogger logger)
    {
        _registry = registry;
        _logger = logger;

        // Polly only sequences the attempts: the retry count is bounded by the row budget in ShouldHandle and every
        // delay comes from DelayGenerator, so the attempt cap and base delay here are deliberately inert.
#pragma warning disable RS0030 // Fix makes code worse: these delays are the job's own retry schedule, paced on the app clock with its due times, leases and cancellation checks so a test drives them; a system-clock wait would put one step of the job lifecycle on a second clock.
        _pipeline = new ResiliencePipelineBuilder { TimeProvider = timeProvider }
#pragma warning restore RS0030
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

    /// <summary>
    /// Runs the job's attempts, retrying in process until one succeeds, the budget or a fail rule ends the run, or a
    /// deferrable run reaches a retry whose delay exceeds <see cref="DeferThreshold"/>.
    /// </summary>
    /// <returns>
    /// <see langword="null"/> when the run ended in process (success, or the failure propagated); otherwise the retry
    /// the caller must reschedule, which only a run started with <paramref name="canDefer"/> set returns.
    /// </returns>
    public async Task<JobsRetryDeferral?> ExecuteAsync(
        JobExecutionState job,
        bool canDefer,
        Func<int, CancellationToken, ValueTask> attempt,
        Func<int, Exception, TimeSpan, CancellationToken, ValueTask> onRetry,
        CancellationToken cancellationToken
    )
    {
        var resilienceContext = ResilienceContextPool.Shared.Get(cancellationToken);
        var execution = new ExecutionState(
            job,
            job.RetryCount,
            _registry.GetFailurePolicy(job.FunctionName),
            canDefer,
            attempt,
            onRetry
        );
        resilienceContext.Properties.Set(_ExecutionKey, execution);
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

            return null;
        }
        // The predicate declined the retry only to hand it back, so Polly rethrew the attempt's failure; that failure
        // travels in the deferral instead of failing the run.
        catch (Exception) when (execution.Deferral is not null)
        {
            return execution.Deferral;
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

        var retryIndex = execution.StartingRetryCount + args.AttemptNumber;
        if (retryIndex >= execution.Job.Retries)
        {
            return ValueTask.FromResult(false);
        }

        if (execution.CanDefer)
        {
            var delay = _RetryDelay(execution, retryIndex);
            if (delay > DeferThreshold)
            {
                execution.Deferral = new JobsRetryDeferral(retryIndex + 1, delay, exception);
                return ValueTask.FromResult(false);
            }
        }

        return ValueTask.FromResult(true);
    }

    private static ValueTask<TimeSpan?> _DelayAsync(RetryDelayGeneratorArguments<object> args)
    {
        var execution = args.Context.Properties.GetValue(_ExecutionKey, null!);
        return ValueTask.FromResult<TimeSpan?>(
            _RetryDelay(execution, execution.StartingRetryCount + args.AttemptNumber)
        );
    }

    // The durable count, not Polly's in-process attempt number, indexes the schedule, so a run resumed after a crash
    // or a deferred retry continues where the previous attempt stopped.
    private static TimeSpan _RetryDelay(ExecutionState execution, int retryIndex)
    {
        if (execution.Job.RetryIntervals is { Length: > 0 } intervals)
        {
            // RetryIntervals is an unvalidated public int[] on the job entity, so a negative value (a plausible typo)
            // would otherwise reach Polly as a negative TimeSpan and break the retry mechanism itself, and an
            // oversized one would park the row far past any delay a failure policy may declare.
            var seconds = intervals[Math.Min(retryIndex, intervals.Length - 1)];
            var delay = TimeSpan.FromSeconds(Math.Max(seconds, 0));
            return delay > FailurePolicyDefinition.MaxDelayLimit ? FailurePolicyDefinition.MaxDelayLimit : delay;
        }

        return _PolicyDelay(execution.Policy, retryIndex);
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

        // The wait that follows is handed over so a stop that lands inside it can park the job at the instant its
        // next attempt was due, rather than recomputing the schedule outside the pipeline.
        await execution
            .OnRetry(retryCount, args.Outcome.Exception!, args.RetryDelay, args.Context.CancellationToken)
            .ConfigureAwait(false);
    }

    private sealed class ExecutionState(
        JobExecutionState job,
        int startingRetryCount,
        FailurePolicyDefinition policy,
        bool canDefer,
        Func<int, CancellationToken, ValueTask> attempt,
        Func<int, Exception, TimeSpan, CancellationToken, ValueTask> onRetry
    )
    {
        public JobExecutionState Job { get; } = job;

        public int StartingRetryCount { get; } = startingRetryCount;

        public FailurePolicyDefinition Policy { get; } = policy;

        public bool CanDefer { get; } = canDefer;

        public Func<int, CancellationToken, ValueTask> Attempt { get; } = attempt;

        public Func<int, Exception, TimeSpan, CancellationToken, ValueTask> OnRetry { get; } = onRetry;

        public JobsRetryDeferral? Deferral { get; set; }
    }
}

/// <summary>A retry the pipeline handed back to wait in the store instead of in process.</summary>
/// <param name="RetryCount">The attempts spent, the failed one included.</param>
/// <param name="Delay">The wait before the next attempt.</param>
/// <param name="Exception">The failure that spent the attempt.</param>
internal sealed record JobsRetryDeferral(int RetryCount, TimeSpan Delay, Exception Exception);
