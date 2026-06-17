// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Headless.Abstractions;
using Headless.DistributedLocks;
using Headless.DistributedLocks.InMemory;
using Headless.Jobs;
using Headless.Jobs.BackgroundServices;
using Headless.Jobs.Coordination;
using Headless.Jobs.Interfaces.Managers;
using Headless.Jobs.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute.ExceptionExtensions;

namespace Tests;

/// <summary>
/// U5: skip-on-contention and unchanged no-lock behavior for the two guarded operations — cron-seed migration
/// (<see cref="JobsInitializationHostedService"/>) and dead-node reclaim (<see cref="JobsDeadOwnerReclaimer"/>).
/// Both guards are provider-agnostic Core logic, so these are unit tests over the real in-memory lock provider shared
/// between two simulated nodes (KTD8) — no Docker, no DB.
/// </summary>
public sealed class JobsDistributedLockGuardTests
{
    private static readonly DistributedLockAcquireOptions _HoldOptions = new()
    {
        AcquireTimeout = TimeSpan.Zero,
        TimeUntilExpires = TimeSpan.FromMinutes(5),
    };

    /// <summary>A real lock over in-memory storage. Two calls sharing one storage instance simulate two nodes.</summary>
    private static IDistributedLock CreateLock(InMemoryDistributedLockStorage storage) =>
        new DistributedLock(
            storage,
            outboxBus: null, // lock-released notifications are not needed for these guard tests
            new DistributedLockOptions(),
            new SequentialGuidGenerator(SequentialGuidType.SqlServer),
            TimeProvider.System,
            NullLogger<DistributedLock>.Instance
        );

    // ----------------------------------------------------------------------------------------------------------------
    // Cron-seed migration guard (U2)
    // ----------------------------------------------------------------------------------------------------------------

    private static async Task InvokeSeedAsync(
        IInternalJobManager manager,
        SchedulerOptionsBuilder options,
        IDistributedLock? keyedLock,
        CancellationToken cancellationToken
    )
    {
        var services = new ServiceCollection();
        services.AddSingleton(manager);
        services.AddSingleton(TimeProvider.System);
        services.AddLogging();
        if (keyedLock is not null)
        {
            services.AddKeyedSingleton(JobsKeys.LockProvider, keyedLock);
        }

        await using var sp = services.BuildServiceProvider();

        var method = typeof(JobsInitializationHostedService).GetMethod(
            "_SeedDefinedCronJobsAsync",
            BindingFlags.NonPublic | BindingFlags.Static
        )!;

        await (Task)method.Invoke(null, [sp, options, cancellationToken])!;
    }

    [Fact]
    public async Task Seed_with_lock_disabled_runs_body_once_and_never_queries_lock()
    {
        var manager = Substitute.For<IInternalJobManager>();
        var spyLock = Substitute.For<IDistributedLock>();
        var options = new SchedulerOptionsBuilder { UseStorageLock = false };

        await InvokeSeedAsync(manager, options, spyLock, CancellationToken.None);

        await manager.Received(1).MigrateDefinedCronJobs(Arg.Any<(string, string)[]>(), Arg.Any<CancellationToken>());
        await spyLock
            .DidNotReceive()
            .TryAcquireAsync(Arg.Any<string>(), Arg.Any<DistributedLockAcquireOptions>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Seed_with_free_lock_runs_body_once_and_releases_lease()
    {
        var storage = new InMemoryDistributedLockStorage(TimeProvider.System);
        var lockProvider = CreateLock(storage);
        var manager = Substitute.For<IInternalJobManager>();
        var options = new SchedulerOptionsBuilder { UseStorageLock = true };

        await InvokeSeedAsync(manager, options, lockProvider, CancellationToken.None);

        await manager.Received(1).MigrateDefinedCronJobs(Arg.Any<(string, string)[]>(), Arg.Any<CancellationToken>());
        // Lease released on completion → the resource is free again for the next boot.
        (await lockProvider.IsLockedAsync(JobsKeys.CronSeedMigrationResource))
            .Should()
            .BeFalse();
    }

    [Fact]
    public async Task Seed_skips_when_another_node_holds_the_lock()
    {
        var storage = new InMemoryDistributedLockStorage(TimeProvider.System);

        // Node A holds the seed lock.
        var nodeA = CreateLock(storage);
        await using var held = await nodeA.TryAcquireAsync(JobsKeys.CronSeedMigrationResource, _HoldOptions);
        held.Should().NotBeNull("pre-condition: the seed lock must be acquirable on an empty store");

        // Node B (same storage) attempts the guarded seed.
        var nodeB = CreateLock(storage);
        var manager = Substitute.For<IInternalJobManager>();
        var options = new SchedulerOptionsBuilder { UseStorageLock = true };

        await InvokeSeedAsync(manager, options, nodeB, CancellationToken.None);

        await manager
            .DidNotReceive()
            .MigrateDefinedCronJobs(Arg.Any<(string, string)[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Seed_skips_when_acquire_faults()
    {
        var faultingLock = Substitute.For<IDistributedLock>();
        faultingLock
            .TryAcquireAsync(Arg.Any<string>(), Arg.Any<DistributedLockAcquireOptions>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("lock store down"));
        var manager = Substitute.For<IInternalJobManager>();
        var options = new SchedulerOptionsBuilder { UseStorageLock = true };

        // Faulting acquire is swallowed as a skip — startup must not fail on a lock-store hiccup.
        await InvokeSeedAsync(manager, options, faultingLock, CancellationToken.None);

        await manager
            .DidNotReceive()
            .MigrateDefinedCronJobs(Arg.Any<(string, string)[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Seed_propagates_cancellation_during_acquire()
    {
        var cancelingLock = Substitute.For<IDistributedLock>();
        cancelingLock
            .TryAcquireAsync(Arg.Any<string>(), Arg.Any<DistributedLockAcquireOptions>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());
        var manager = Substitute.For<IInternalJobManager>();
        var options = new SchedulerOptionsBuilder { UseStorageLock = true };

        var act = async () => await InvokeSeedAsync(manager, options, cancelingLock, CancellationToken.None);

        await act.Should().ThrowAsync<OperationCanceledException>();
        await manager
            .DidNotReceive()
            .MigrateDefinedCronJobs(Arg.Any<(string, string)[]>(), Arg.Any<CancellationToken>());
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Dead-node reclaim guard (U3)
    // ----------------------------------------------------------------------------------------------------------------

    private static JobsDeadOwnerReclaimer CreateReclaimer(
        IInternalJobManager manager,
        SchedulerOptionsBuilder options,
        IDistributedLock lockProvider
    ) => new(manager, options, lockProvider, NullLogger<JobsDeadOwnerReclaimer>.Instance);

    [Fact]
    public async Task Reclaim_with_lock_disabled_reclaims_every_owner_and_never_queries_lock()
    {
        var manager = Substitute.For<IInternalJobManager>();
        var spyLock = Substitute.For<IDistributedLock>();
        var options = new SchedulerOptionsBuilder { UseStorageLock = false };
        var reclaimer = CreateReclaimer(manager, options, spyLock);

        await reclaimer.ReclaimAsync(["node-a@1", "node-b@2"], CancellationToken.None);

        await manager.Received(1).ReleaseDeadNodeResources("node-a@1", Arg.Any<CancellationToken>());
        await manager.Received(1).ReleaseDeadNodeResources("node-b@2", Arg.Any<CancellationToken>());
        await spyLock
            .DidNotReceive()
            .TryAcquireAsync(Arg.Any<string>(), Arg.Any<DistributedLockAcquireOptions>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reclaim_with_free_lock_runs_batch_once_and_releases_lease()
    {
        var storage = new InMemoryDistributedLockStorage(TimeProvider.System);
        var lockProvider = CreateLock(storage);
        var manager = Substitute.For<IInternalJobManager>();
        var options = new SchedulerOptionsBuilder { UseStorageLock = true };
        var reclaimer = CreateReclaimer(manager, options, lockProvider);

        await reclaimer.ReclaimAsync(["node-a@1", "node-b@2"], CancellationToken.None);

        await manager.Received(1).ReleaseDeadNodeResources("node-a@1", Arg.Any<CancellationToken>());
        await manager.Received(1).ReleaseDeadNodeResources("node-b@2", Arg.Any<CancellationToken>());
        (await lockProvider.IsLockedAsync(JobsKeys.DeadNodeSweepResource)).Should().BeFalse();
    }

    [Fact]
    public async Task Reclaim_skips_entire_batch_when_another_survivor_sweeps()
    {
        var storage = new InMemoryDistributedLockStorage(TimeProvider.System);

        // Survivor A holds the sweep lock.
        var nodeA = CreateLock(storage);
        await using var held = await nodeA.TryAcquireAsync(JobsKeys.DeadNodeSweepResource, _HoldOptions);
        held.Should().NotBeNull("pre-condition: the sweep lock must be acquirable on an empty store");

        // Survivor B (same storage) attempts the guarded reclaim.
        var nodeB = CreateLock(storage);
        var manager = Substitute.For<IInternalJobManager>();
        var options = new SchedulerOptionsBuilder { UseStorageLock = true };
        var reclaimer = CreateReclaimer(manager, options, nodeB);

        await reclaimer.ReclaimAsync(["node-a@1", "node-b@2"], CancellationToken.None);

        await manager.DidNotReceive().ReleaseDeadNodeResources(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reclaim_skips_when_acquire_faults()
    {
        var faultingLock = Substitute.For<IDistributedLock>();
        faultingLock
            .TryAcquireAsync(Arg.Any<string>(), Arg.Any<DistributedLockAcquireOptions>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("lock store down"));
        var manager = Substitute.For<IInternalJobManager>();
        var options = new SchedulerOptionsBuilder { UseStorageLock = true };
        var reclaimer = CreateReclaimer(manager, options, faultingLock);

        await reclaimer.ReclaimAsync(["node-a@1"], CancellationToken.None);

        await manager.DidNotReceive().ReleaseDeadNodeResources(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reclaim_propagates_cancellation_during_acquire()
    {
        var cancelingLock = Substitute.For<IDistributedLock>();
        cancelingLock
            .TryAcquireAsync(Arg.Any<string>(), Arg.Any<DistributedLockAcquireOptions>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());
        var manager = Substitute.For<IInternalJobManager>();
        var options = new SchedulerOptionsBuilder { UseStorageLock = true };
        var reclaimer = CreateReclaimer(manager, options, cancelingLock);

        var act = async () => await reclaimer.ReclaimAsync(["node-a@1"], CancellationToken.None);

        await act.Should().ThrowAsync<OperationCanceledException>();
        await manager.DidNotReceive().ReleaseDeadNodeResources(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
