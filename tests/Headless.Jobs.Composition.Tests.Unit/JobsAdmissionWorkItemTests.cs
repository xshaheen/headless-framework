// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs;
using Headless.Jobs.BackgroundServices;
using Headless.Jobs.JobsThreadPool;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tests;

public sealed class JobsAdmissionWorkItemTests : TestBase
{
    private static readonly JobExecutionState _Function = new() { FunctionName = "fn", JobId = Guid.NewGuid() };

    [Fact]
    public async Task claim_write_failure_is_swallowed_without_executing()
    {
        var manager = Substitute.For<IInternalJobManager>();
        manager
            .SetTickersInProgress(Arg.Any<JobExecutionState[]>(), Arg.Any<CancellationToken>())
            .Returns<Task<JobExecutionState[]>>(_ => throw new InvalidOperationException("store down"));
        await using var pool = new JobsTaskScheduler(maxConcurrency: 1, TimeProvider.System);
        var work = JobsAdmissionWorkItem.Create(pool, manager, null!, NullLogger.Instance, _Function, isDue: false);

        await work(AbortToken);

        await manager.Received(1).SetTickersInProgress(Arg.Any<JobExecutionState[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task lost_claim_race_skips_execution()
    {
        var manager = Substitute.For<IInternalJobManager>();
        manager
            .SetTickersInProgress(Arg.Any<JobExecutionState[]>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Array.Empty<JobExecutionState>()));

        // A null handler proves execution is never reached: the delegate would throw on it.
        await using var pool = new JobsTaskScheduler(maxConcurrency: 1, TimeProvider.System);
        var work = JobsAdmissionWorkItem.Create(pool, manager, null!, NullLogger.Instance, _Function, isDue: false);

        await work(AbortToken);

        await manager.Received(1).SetTickersInProgress(Arg.Any<JobExecutionState[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task draining_pool_hands_the_queued_claim_back_without_starting_it()
    {
        // given: a row this node claimed (Queued, owned, leased) and a pool a graceful stop is draining
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessJobs(options => options.DisableBackgroundServices());
        await using var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
        var owner = provider.GetRequiredService<SchedulerOptionsBuilder>().NodeId;
        var job = new TimeJobEntity
        {
            Id = Guid.NewGuid(),
            Function = "fn",
            Status = JobStatus.Queued,
            OwnerId = owner,
            LockedUntil = DateTime.UtcNow.AddMinutes(5),
            ExecutionTime = DateTime.UtcNow,
        };
        await store.AddTimeJobsAsync([job], AbortToken);
        var claim = new JobExecutionState
        {
            FunctionName = "fn",
            JobId = job.Id,
            Type = JobType.TimeJob,
            ClaimOwner = owner,
        };
        await using var pool = new JobsTaskScheduler(maxConcurrency: 1, TimeProvider.System);
        pool.BeginDrain();

        // A null handler proves execution is never reached: the delegate would throw on it.
        var work = JobsAdmissionWorkItem.Create(
            pool,
            provider.GetRequiredService<IInternalJobManager>(),
            null!,
            NullLogger.Instance,
            claim,
            isDue: false
        );

        // when: the stop already cancelled the item's token
        await work(new CancellationToken(canceled: true));

        // then
        var stored = await store.GetTimeJobByIdAsync(job.Id, AbortToken);
        stored!.Status.Should().Be(JobStatus.Idle);
        stored.OwnerId.Should().BeNull();
        stored.LockedUntil.Should().BeNull();
        stored.RetryCount.Should().Be(0);
    }
}
