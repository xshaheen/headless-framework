// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Jobs;

namespace Tests;

/// <summary>
/// A chain child runs under its root's claim, so its start is the first write it makes on its own row. Every store must
/// record that start, so the stored row and the dashboard read <see cref="JobStatus.InProgress"/> while the child runs.
/// Runs against memory, PostgreSQL, and SQL Server; the caller registers <see cref="ChainChildStartJobsModule"/> on a
/// host that runs its background services.
/// </summary>
public static class JobsChainChildStartScenarios
{
    private static readonly TimeSpan _WaitBudget = TimeSpan.FromSeconds(30);

    public static async Task RunningChildReadsInProgressAsync(
        IJobPersistenceProvider<TimeJobEntity, CronJobEntity> store,
        CancellationToken cancellationToken
    )
    {
        // given: a due root with one non-timed child that runs once the root succeeds
        var child = new TimeJobEntity
        {
            Id = Guid.NewGuid(),
            Function = ChainChildStartJobsModule.ChildFunction,
            RunCondition = RunCondition.OnSuccess,
            Retries = 0,
            RetryIntervals = [0],
        };
        var root = new TimeJobEntity
        {
            Id = Guid.NewGuid(),
            Function = ChainChildStartJobsModule.RootFunction,
            // wall-clock: due now on any store clock; the poll claims it on its next tick.
            ExecutionTime = DateTime.UtcNow,
            Retries = 0,
            RetryIntervals = [0],
            Children = [child],
        };
        var run = ChainChildStartJobsModule.Track(child.Id);

        try
        {
            (await store.AddTimeJobsAsync([root], cancellationToken)).Should().Be(2);

            // when: the child is running
            await run.Started.Task.WaitAsync(_WaitBudget, cancellationToken);

            // then: its start is recorded on the row and on the dashboard's run read
            var stored = await store.GetTimeJobByIdAsync(child.Id, cancellationToken);
            stored!.Status.Should().Be(JobStatus.InProgress, "a running chain child's start must be recorded");
            stored.ExecutedAt.Should().BeNull();
            var dashboardRun = await store.GetJobRunAsync(child.Id, cancellationToken);
            dashboardRun!.Status.Should().Be(JobStatus.InProgress);

            // and the run settles on the row it started
            run.Release.TrySetResult();
            var settled = await _WaitForTerminalAsync(store, child.Id, cancellationToken);
            settled.Status.Should().Be(JobStatus.Succeeded);
            run.Invocations.Should().Be(1);
            (await store.GetTimeJobByIdAsync(root.Id, cancellationToken))!.Status.Should().Be(JobStatus.Succeeded);
        }
        finally
        {
            run.Release.TrySetResult();
            ChainChildStartJobsModule.Forget(child.Id);
        }
    }

    private static async Task<TimeJobEntity> _WaitForTerminalAsync(
        IJobPersistenceProvider<TimeJobEntity, CronJobEntity> store,
        Guid id,
        CancellationToken cancellationToken
    )
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(_WaitBudget);
        TimeJobEntity? stored = null;

        while (!budget.IsCancellationRequested)
        {
            stored = await store.GetTimeJobByIdAsync(id, cancellationToken);

            if (
                stored is
                { Status: JobStatus.Succeeded or JobStatus.Failed or JobStatus.Cancelled or JobStatus.Skipped }
            )
            {
                return stored;
            }

            // real-wait: the terminal write lands on a store the scheduler owns; no signal reports it.
            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
        }

        throw new TimeoutException($"The child did not settle within {_WaitBudget}: status {stored?.Status}.");
    }
}

/// <summary>
/// A root that succeeds at once and a child that reports its start, then waits until the scenario releases it. State is
/// keyed by the child's id, so suites running in parallel do not share a gate.
/// </summary>
public sealed class ChainChildStartJobsModule : IJobsModule
{
    public const string RootFunction = "ChainChildStart_Root";
    public const string ChildFunction = "ChainChildStart_Child";

    private static readonly ConcurrentDictionary<Guid, ChildRun> _Runs = new();

    private ChainChildStartJobsModule() { }

    public static ChildRun Track(Guid childId) => _Runs.GetOrAdd(childId, static _ => new ChildRun());

    public static void Forget(Guid childId) => _Runs.TryRemove(childId, out _);

    private static async Task _RunChildAsync(Guid jobId, CancellationToken cancellationToken)
    {
        if (!_Runs.TryGetValue(jobId, out var run))
        {
            return;
        }

        Interlocked.Increment(ref run.InvocationCount);
        run.Started.TrySetResult();
        await run.Release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    static void IJobsModule.Register(JobsCatalogBuilder catalog)
    {
        catalog.AddFunctions(
            new Dictionary<string, JobFunctionRegistration>(StringComparer.Ordinal)
            {
                [RootFunction] = _Registration(static (_, _, _) => Task.CompletedTask, typeof(ChainRootJob)),
                [ChildFunction] = _Registration(
                    static (_, context, cancellationToken) => _RunChildAsync(context.Id, cancellationToken),
                    typeof(ChainChildJob)
                ),
            }
        );
        catalog.AddDescriptors(
            new Dictionary<string, JobFunctionDescriptor>(StringComparer.Ordinal)
            {
                [RootFunction] = new(RootFunction, null, string.Empty, JobPriority.Normal, 0),
                [ChildFunction] = new(ChildFunction, null, string.Empty, JobPriority.Normal, 0),
            }
        );
    }

    private static JobFunctionRegistration _Registration(JobFunctionDelegate run, Type jobType)
    {
        return new JobFunctionRegistration
        {
            CronExpression = string.Empty,
            Priority = JobPriority.Normal,
            Delegate = run,
            MaxConcurrency = 0,
            JobType = jobType,
        };
    }

    /// <summary>One child run the scenario observes.</summary>
    public sealed class ChildRun
    {
        internal int InvocationCount;

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Invocations => Volatile.Read(ref InvocationCount);
    }

    public sealed class ChainRootJob : IJob
    {
        public ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }

    public sealed class ChainChildJob : IJob
    {
        public ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }
}
