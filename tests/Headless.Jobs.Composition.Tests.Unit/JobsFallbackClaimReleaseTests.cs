// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs;
using Headless.Jobs.BackgroundServices;
using Headless.Jobs.Instrumentation;
using Headless.Jobs.JobsThreadPool;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tests;

// The timed-out sweep claims a whole batch Queued and leased before it queues any of it. When the queuing stops part
// way, the rows it never queued must go back at once, not wait out a whole lease for the next sweep.
public sealed class JobsFallbackClaimReleaseTests : TestBase
{
    private static readonly TimeSpan _waitBudget = TimeSpan.FromSeconds(10);

    private readonly JobExecutionState[] _batch = [_Job(), _Job(), _Job()];

    [Fact]
    public async Task should_release_the_unqueued_batch_when_the_pool_freezes_mid_dispatch()
    {
        await using var taskScheduler = new JobsTaskScheduler(maxConcurrency: 1, timeProvider: TimeProvider.System);
        using var membershipLostCts = new CancellationTokenSource();

        // The freeze lands between the claim and the first admission, so the loop's frozen pre-check has passed.
        var released = await _RunOneBatchAsync(taskScheduler, membershipLostCts, onClaim: taskScheduler.Freeze);

        released.Select(x => x.JobId).Should().BeEquivalentTo(_batch.Select(x => x.JobId));
    }

    [Fact]
    public async Task should_release_the_unqueued_batch_when_membership_loss_cancels_the_loop_mid_dispatch()
    {
        await using var taskScheduler = new JobsTaskScheduler(maxConcurrency: 1, timeProvider: TimeProvider.System);
        using var membershipLostCts = new CancellationTokenSource();

        var released = await _RunOneBatchAsync(taskScheduler, membershipLostCts, onClaim: membershipLostCts.Cancel);

        released.Select(x => x.JobId).Should().BeEquivalentTo(_batch.Select(x => x.JobId));
    }

    private async Task<JobExecutionState[]> _RunOneBatchAsync(
        JobsTaskScheduler taskScheduler,
        CancellationTokenSource membershipLostCts,
        Action onClaim
    )
    {
        var manager = Substitute.For<IInternalJobManager>();
        manager
            .ReclaimStalledResources(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(default(JobReclaimCounts)));
        var claimed = 0;
        manager
            .RunTimedOutTickers(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                if (Interlocked.Increment(ref claimed) != 1)
                {
                    return Task.FromResult(Array.Empty<JobExecutionState>());
                }

                onClaim();
                return Task.FromResult(_batch);
            });
        var released = new TaskCompletionSource<JobExecutionState[]>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        manager
            .ReleaseAcquiredResources(Arg.Any<JobExecutionState[]?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                released.TrySetResult(call.Arg<JobExecutionState[]?>() ?? []);
                return Task.CompletedTask;
            });

        var ownerIdentity = Substitute.For<IJobsOwnerIdentity>();
        ownerIdentity.MembershipLostToken.Returns(membershipLostCts.Token);

        var services = new ServiceCollection();
        services.AddSingleton(manager);
        await using var serviceProvider = services.BuildServiceProvider();
        var registry = JobFunctionRegistryBuilder.Build([], [], []);
        var handler = new JobsExecutionTaskHandler(
            serviceProvider,
            TimeProvider.System,
            Substitute.For<IJobsInstrumentation>(),
            manager,
            registry,
            new JobsExecutionCancellationRegistry(),
            new SchedulerOptionsBuilder(),
            NullLogger<JobsExecutionTaskHandler>.Instance,
            new JobsFunctionTagBound(registry)
        );

        using var service = new JobsFallbackBackgroundService(
            manager,
            new SchedulerOptionsBuilder { FallbackIntervalChecker = TimeSpan.FromMilliseconds(20) },
            handler,
            taskScheduler,
            new JobFunctionConcurrencyGate(),
            registry,
            TimeProvider.System,
            ownerIdentity,
            TestActivationBarrier.Opened(),
            NullLogger<JobsFallbackBackgroundService>.Instance
        );

        await service.StartAsync(AbortToken);

        try
        {
            return await released.Task.WaitAsync(_waitBudget, AbortToken);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    private static JobExecutionState _Job()
    {
        return new JobExecutionState
        {
            JobId = Guid.NewGuid(),
            FunctionName = "fn-timed-out",
            Type = JobType.TimeJob,
            ExecutionTime = DateTime.UtcNow,
            RetryIntervals = [0],
            Status = JobStatus.Queued,
            RunCondition = RunCondition.OnSuccess,
        };
    }
}
