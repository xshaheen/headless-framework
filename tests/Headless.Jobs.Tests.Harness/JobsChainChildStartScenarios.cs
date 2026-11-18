// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Jobs;
using Microsoft.Extensions.DependencyInjection;

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
        IServiceProvider services,
        CancellationToken cancellationToken
    )
    {
        var store = services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
        var manager = services.GetRequiredService<ITimeJobManager<TimeJobEntity>>();

        // given: a run-now root with one non-timed child that runs once the root succeeds
        // The manager assigns the ids, so the run is keyed by a correlation the chain's nodes inherit from the root.
        var correlation = Guid.NewGuid().ToString("N");
        var child = new TimeJobEntity
        {
            Function = ChainChildStartJobsModule.ChildFunction,
            RunCondition = RunCondition.OnSuccess,
            Retries = 0,
            RetryIntervals = [0],
        };
        var root = new TimeJobEntity
        {
            Function = ChainChildStartJobsModule.RootFunction,
            CorrelationId = correlation,
            Retries = 0,
            RetryIntervals = [0],
            Children = [child],
        };
        var run = ChainChildStartJobsModule.Track(correlation);

        try
        {
            // The manager stamps the due time on the store clock and wakes the scheduler, so the run starts at once.
            await manager.AddAsync(root, cancellationToken);

            // when: the child is running
            await _WaitForStartAsync(store, run, root.Id, child.Id, cancellationToken);

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
            ChainChildStartJobsModule.Forget(correlation);
        }
    }

    private static async Task _WaitForStartAsync(
        IJobPersistenceProvider<TimeJobEntity, CronJobEntity> store,
        ChainChildStartJobsModule.ChildRun run,
        Guid rootId,
        Guid childId,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await run.Started.Task.WaitAsync(_WaitBudget, cancellationToken);
        }
        catch (TimeoutException timeout)
        {
            var root = await store.GetTimeJobByIdAsync(rootId, cancellationToken);
            var child = await store.GetTimeJobByIdAsync(childId, cancellationToken);

            throw new TimeoutException(
                $"The child did not start within {_WaitBudget}: root {root?.Status} (lease {root?.Lease.State}), "
                    + $"child {child?.Status} (lease {child?.Lease.State}).",
                timeout
            );
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
/// keyed by the chain's correlation id, so suites running in parallel do not share a gate.
/// </summary>
public sealed class ChainChildStartJobsModule : IJobsModule
{
    public const string RootFunction = "ChainChildStart_Root";
    public const string ChildFunction = "ChainChildStart_Child";

    private static readonly ConcurrentDictionary<string, ChildRun> _Runs = new(StringComparer.Ordinal);

    private ChainChildStartJobsModule() { }

    public static ChildRun Track(string correlationId) => _Runs.GetOrAdd(correlationId, static _ => new ChildRun());

    public static void Forget(string correlationId) => _Runs.TryRemove(correlationId, out _);

    private static async Task _RunChildAsync(string? correlationId, CancellationToken cancellationToken)
    {
        if (correlationId is null || !_Runs.TryGetValue(correlationId, out var run))
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
                    static (_, context, cancellationToken) => _RunChildAsync(context.CorrelationId, cancellationToken),
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
