// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Collections.Frozen;
using Headless.Jobs;
using Headless.Reliability;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tests;

public sealed class JobsRetryPipelineTests : TestBase
{
    private const string _FunctionName = "Fn";

    [Fact]
    public async Task should_wait_each_stored_interval_and_reuse_the_last_one()
    {
        var run = await _RunToExhaustionAsync(FailurePolicyDefinition.None, retries: 3, retryIntervals: [5, 10]);

        run.Attempts.Should().Equal(0, 1, 2, 3);
        run.Delays.Should().Equal(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task should_prefer_stored_intervals_over_the_policy_delays()
    {
        var policy = new FailurePolicyBuilder().Delayed(2, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(10)).Build();

        var run = await _RunToExhaustionAsync(policy, retries: 2, retryIntervals: [3]);

        run.Delays.Should().Equal(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task should_clamp_a_negative_stored_interval_to_zero()
    {
        var run = await _RunToExhaustionAsync(FailurePolicyDefinition.None, retries: 1, retryIntervals: [-5]);

        run.Attempts.Should().Equal(0, 1);
        run.Delays.Should().BeEmpty();
    }

    [Fact]
    public async Task should_wait_the_policy_delays_when_the_row_stores_no_intervals()
    {
        var policy = new FailurePolicyBuilder()
            .Immediate(1)
            .Delayed(2, TimeSpan.FromSeconds(10), TimeSpan.FromHours(1))
            .Build();

        var run = await _RunToExhaustionAsync(policy, retries: 3, retryIntervals: null);

        // The first retry is immediate, so only the two delayed retries wait: 10s then 20s, each within the
        // policy's jitter band.
        run.Attempts.Should().Equal(0, 1, 2, 3);
        run.Delays.Should().HaveCount(2);
        run.Delays[0].Should().BeCloseTo(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(2));
        run.Delays[1].Should().BeCloseTo(TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(4));
    }

    [Fact]
    public async Task should_cap_a_stored_interval_at_the_max_delay_limit()
    {
        var run = await _RunToExhaustionAsync(FailurePolicyDefinition.None, retries: 1, retryIntervals: [200_000]);

        run.Delays.Should().Equal(FailurePolicyDefinition.MaxDelayLimit);
    }

    [Fact]
    public async Task should_retry_back_to_back_within_the_policy_immediate_tier()
    {
        var policy = new FailurePolicyBuilder().Immediate(2).Build();

        var run = await _RunToExhaustionAsync(policy, retries: 2, retryIntervals: []);

        run.Attempts.Should().Equal(0, 1, 2);
        run.Delays.Should().BeEmpty();
    }

    [Fact]
    public async Task should_wait_the_fallback_delay_for_a_retry_the_policy_does_not_pace()
    {
        var policy = new FailurePolicyBuilder().Immediate(1).Build();

        var run = await _RunToExhaustionAsync(policy, retries: 3, retryIntervals: null);

        // The first retry is the policy's immediate one; the two retries past it have no policy delay to wait.
        run.Attempts.Should().Equal(0, 1, 2, 3);
        run.Delays.Should().Equal(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task should_index_the_schedule_by_the_durable_retry_count_after_recovery()
    {
        var policy = new FailurePolicyBuilder()
            .Immediate(1)
            .Delayed(1, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10))
            .Build();

        var run = await _RunToExhaustionAsync(policy, retries: 2, retryIntervals: null, retryCount: 1);

        // The process resumed after the immediate retry was spent, so its one remaining retry is the delayed one.
        run.Attempts.Should().Equal(1, 2);
        run.Delays.Should().ContainSingle().Which.Should().BeCloseTo(TimeSpan.FromSeconds(9), TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task should_stop_at_the_row_budget_even_when_the_policy_allows_more()
    {
        var policy = new FailurePolicyBuilder().Immediate(5).Build();

        var run = await _RunToExhaustionAsync(policy, retries: 1, retryIntervals: null);

        run.Attempts.Should().Equal(0, 1);
        run.RetryCounts.Should().Equal(1);
    }

    [Fact]
    public async Task should_not_retry_a_failure_a_fail_rule_matches()
    {
        var policy = new FailurePolicyBuilder().FailOn<TimeoutException>().Build();

        var run = await _RunToExhaustionAsync(policy, retries: 3, retryIntervals: null);

        run.Attempts.Should().Equal(0);
        run.RetryCounts.Should().BeEmpty();
    }

    [Fact]
    public async Task should_retry_a_cancellation_the_execution_did_not_request()
    {
        // The handler's own timeout: the exception carries an already-cancelled token, but not the execution's.
        using var handlerTimeout = new CancellationTokenSource();
        await handlerTimeout.CancelAsync();

        var run = await _RunToExhaustionAsync<OperationCanceledException>(
            FailurePolicyDefinition.None,
            retries: 3,
            retryIntervals: null,
            failure: () => new OperationCanceledException(handlerTimeout.Token)
        );

        run.Attempts.Should().Equal(0, 1, 2, 3);
        run.RetryCounts.Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task should_not_retry_a_cancellation_a_fail_rule_matches()
    {
        var policy = new FailurePolicyBuilder().FailOn<OperationCanceledException>().Build();

        var run = await _RunToExhaustionAsync<TaskCanceledException>(
            policy,
            retries: 3,
            retryIntervals: null,
            failure: static () => new TaskCanceledException("handler timeout")
        );

        run.Attempts.Should().Equal(0);
        run.RetryCounts.Should().BeEmpty();
    }

    [Fact]
    public async Task should_not_retry_cancellation_of_the_execution_token()
    {
        using var execution = new CancellationTokenSource();

        var run = await _RunToExhaustionAsync<OperationCanceledException>(
            FailurePolicyDefinition.None,
            retries: 3,
            retryIntervals: null,
            failure: () =>
            {
                execution.Cancel();
                return new OperationCanceledException(execution.Token);
            },
            executionToken: execution.Token
        );

        run.Attempts.Should().Equal(0);
        run.RetryCounts.Should().BeEmpty();
    }

    [Fact]
    public async Task should_not_retry_terminate_execution()
    {
        var run = await _RunToExhaustionAsync<TerminateExecutionException>(
            FailurePolicyDefinition.None,
            retries: 3,
            retryIntervals: null,
            failure: static () => new TerminateExecutionException(JobStatus.Skipped, "stop")
        );

        run.Attempts.Should().Equal(0);
    }

    [Fact]
    public async Task should_hand_back_a_retry_whose_delay_exceeds_the_threshold_when_the_run_can_defer()
    {
        var (deferral, attempts, delays) = await _RunDeferrableAsync(retries: 3, retryIntervals: [60], retryCount: 1);

        // The second attempt failed (durable count 1 -> 2 spent), and its 60s wait is the caller's to schedule.
        attempts.Should().Equal(1);
        delays.Should().BeEmpty();
        deferral.Should().NotBeNull();
        deferral!.RetryCount.Should().Be(2);
        deferral.Delay.Should().Be(TimeSpan.FromSeconds(60));
        deferral.Exception.Should().BeOfType<TimeoutException>();
    }

    [Fact]
    public async Task should_wait_in_process_for_a_retry_within_the_threshold_even_when_the_run_can_defer()
    {
        var (deferral, attempts, delays) = await _RunDeferrableAsync(retries: 2, retryIntervals: [0, 1, 60]);

        // 0s and 1s stay in process; the third interval is never reached because the budget is spent.
        deferral.Should().BeNull();
        attempts.Should().Equal(0, 1, 2);
        delays.Should().Equal(JobsRetryPipeline.DeferThreshold);
    }

    [Fact]
    public async Task should_not_defer_when_the_budget_is_spent()
    {
        var (deferral, attempts, _) = await _RunDeferrableAsync(retries: 1, retryIntervals: [60], retryCount: 1);

        deferral.Should().BeNull();
        attempts.Should().Equal(1);
    }

    [Fact]
    public async Task should_wait_in_process_for_a_long_delay_when_the_run_cannot_defer()
    {
        var run = await _RunToExhaustionAsync(FailurePolicyDefinition.None, retries: 1, retryIntervals: [60]);

        run.Delays.Should().Equal(TimeSpan.FromSeconds(60));
    }

    // Runs a deferrable job whose attempts all fail. A run that ends in process rethrows, which this records as no
    // deferral.
    private static async Task<(
        JobsRetryDeferral? Deferral,
        List<int> Attempts,
        List<TimeSpan> Delays
    )> _RunDeferrableAsync(int retries, int[] retryIntervals, int retryCount = 0)
    {
        var (pipeline, timeProvider) = _CreatePipeline(FailurePolicyDefinition.None);
        var job = _CreateJob(retries, retryIntervals, retryCount);
        var attempts = new List<int>();
        JobsRetryDeferral? deferral = null;

        try
        {
            deferral = await pipeline.ExecuteAsync(
                job,
                canDefer: true,
                (attemptRetryCount, _) =>
                {
                    attempts.Add(attemptRetryCount);
                    return ValueTask.FromException(new TimeoutException("transient"));
                },
                (nextRetryCount, _, _, _) =>
                {
                    job.RetryCount = nextRetryCount;
                    return ValueTask.CompletedTask;
                },
                AbortToken
            );
        }
        catch (TimeoutException)
        {
            // The run ended in process on its last failure.
        }

        return (deferral, attempts, [.. timeProvider.Delays.Where(d => d > TimeSpan.Zero)]);
    }

    private static (JobsRetryPipeline Pipeline, DelayRecordingTimeProvider TimeProvider) _CreatePipeline(
        FailurePolicyDefinition policy
    )
    {
        var registry = JobFunctionRegistryBuilder.Build([], [], []) with
        {
            FailurePolicies = new Dictionary<string, FailurePolicyDefinition>(StringComparer.Ordinal)
            {
                [_FunctionName] = policy,
            }.ToFrozenDictionary(StringComparer.Ordinal),
        };
        var timeProvider = new DelayRecordingTimeProvider();
        return (new JobsRetryPipeline(registry, timeProvider, NullLogger.Instance), timeProvider);
    }

    private static JobExecutionState _CreateJob(int retries, int[]? retryIntervals, int retryCount) =>
        new()
        {
            JobId = Guid.NewGuid(),
            FunctionName = _FunctionName,
            Type = JobType.TimeJob,
            Retries = retries,
            RetryCount = retryCount,
            RetryIntervals = retryIntervals,
        };

    private static Task<PipelineRun> _RunToExhaustionAsync(
        FailurePolicyDefinition policy,
        int retries,
        int[]? retryIntervals,
        int retryCount = 0
    )
    {
        return _RunToExhaustionAsync<TimeoutException>(
            policy,
            retries,
            retryIntervals,
            static () => new TimeoutException("transient"),
            retryCount
        );
    }

    private static async Task<PipelineRun> _RunToExhaustionAsync<TException>(
        FailurePolicyDefinition policy,
        int retries,
        int[]? retryIntervals,
        Func<Exception> failure,
        int retryCount = 0,
        CancellationToken? executionToken = null
    )
        where TException : Exception
    {
        var (pipeline, timeProvider) = _CreatePipeline(policy);
        var job = _CreateJob(retries, retryIntervals, retryCount);
        var attempts = new List<int>();
        var retryCounts = new List<int>();

        var action = async () =>
            await pipeline.ExecuteAsync(
                job,
                canDefer: false,
                (attemptRetryCount, _) =>
                {
                    attempts.Add(attemptRetryCount);
                    return ValueTask.FromException(failure());
                },
                (nextRetryCount, _, _, _) =>
                {
                    // The executor persists the advanced count before the next attempt runs.
                    retryCounts.Add(nextRetryCount);
                    job.RetryCount = nextRetryCount;
                    return ValueTask.CompletedTask;
                },
                executionToken ?? AbortToken
            );

        await action.Should().ThrowAsync<TException>();

        return new PipelineRun(attempts, retryCounts, [.. timeProvider.Delays.Where(d => d > TimeSpan.Zero)]);
    }

    private sealed record PipelineRun(List<int> Attempts, List<int> RetryCounts, List<TimeSpan> Delays);

    // Records every delay the pipeline asks for and fires it at once, so the tests read the exact computed schedule
    // without waiting for it.
    private sealed class DelayRecordingTimeProvider : TimeProvider
    {
        public ConcurrentQueue<TimeSpan> Delays { get; } = new();

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Delays.Enqueue(dueTime);

            return System.CreateTimer(callback, state, TimeSpan.Zero, period);
        }
    }
}
