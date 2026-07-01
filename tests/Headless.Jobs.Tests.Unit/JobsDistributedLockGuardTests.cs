// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.DistributedLocks;
using Headless.DistributedLocks.InMemory;
using Headless.Jobs;
using Headless.Jobs.BackgroundServices;
using Headless.Jobs.Coordination;
using Headless.Jobs.Interfaces.Managers;
using Headless.Jobs.Internal;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute.ExceptionExtensions;

namespace Tests;

/// <summary>
/// U5: behavior of the cron-seed migration guard (<see cref="JobsInitializationHostedService"/>) — skip-on-contention,
/// unchanged no-lock default, and lease release on the success and throw paths — plus the dead-node reclaim contract
/// (<see cref="JobsDeadOwnerReclaimer"/>). The reclaim path is intentionally <em>not</em> lock-guarded (#267 review):
/// the shared <c>DeadOwnerRecoveryBridge</c> marks owners reclaimed before the call and only retries on a thrown
/// failure, so the reclaimer must propagate (not swallow) to avoid stranding dead-owner InProgress rows. The seed guard
/// is provider-agnostic Core logic, so these are unit tests over the real in-memory lock provider shared between two
/// simulated nodes (KTD8) — no Docker, no DB.
/// </summary>
public sealed class JobsDistributedLockGuardTests : TestBase
{
    private static readonly DistributedLockAcquireOptions _HoldOptions = new()
    {
        AcquireTimeout = TimeSpan.Zero,
        TimeUntilExpires = TimeSpan.FromMinutes(5),
    };

    /// <summary>A real lock over in-memory storage. Two calls sharing one storage instance simulate two nodes.</summary>
    private static IDistributedLock _CreateLock(InMemoryDistributedLockStorage storage) =>
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

    private static async Task _InvokeSeedAsync(
        IInternalJobManager manager,
        SchedulerOptionsBuilder options,
        IDistributedLock lockProvider,
        CancellationToken cancellationToken
    )
    {
        var services = new ServiceCollection();
        services.AddSingleton(manager);
        // Register the lock under the Jobs-scoped keyed slot — production resolves it lazily from this same slot
        // (NullDistributedLock fallback included) inside the seed guard's try/catch.
        services.AddKeyedSingleton(JobsKeys.LockProvider, lockProvider);

        await using var sp = services.BuildServiceProvider();

        // Drive the seed guard directly — no StartAsync, so the test stays isolated from the global
        // JobFunctionProvider static state. A rename breaks the build, not at runtime.
        var hostedService = new JobsInitializationHostedService(
            sp,
            NullLogger<JobsInitializationHostedService>.Instance
        );

        await hostedService.SeedDefinedCronJobsAsync(options, cancellationToken);
    }

    [Fact]
    public async Task Seed_with_lock_disabled_runs_body_once_and_never_queries_lock()
    {
        var manager = Substitute.For<IInternalJobManager>();
        var spyLock = Substitute.For<IDistributedLock>();
        var options = new SchedulerOptionsBuilder { UseStorageLock = false };

        await _InvokeSeedAsync(manager, options, spyLock, AbortToken);

        await manager.Received(1).MigrateDefinedCronJobs(Arg.Any<(string, string)[]>(), Arg.Any<CancellationToken>());
        await spyLock
            .DidNotReceive()
            .TryAcquireAsync(Arg.Any<string>(), Arg.Any<DistributedLockAcquireOptions>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Seed_with_free_lock_runs_body_once_and_releases_lease()
    {
        var storage = new InMemoryDistributedLockStorage(TimeProvider.System);
        var lockProvider = _CreateLock(storage);
        var manager = Substitute.For<IInternalJobManager>();
        var options = new SchedulerOptionsBuilder { UseStorageLock = true };

        await _InvokeSeedAsync(manager, options, lockProvider, AbortToken);

        await manager.Received(1).MigrateDefinedCronJobs(Arg.Any<(string, string)[]>(), Arg.Any<CancellationToken>());
        // Lease released on completion → the resource is free again for the next boot.
        (await lockProvider.IsLockedAsync(JobsKeys.CronSeedMigrationResource, AbortToken))
            .Should()
            .BeFalse();
    }

    [Fact]
    public async Task Seed_releases_lease_when_body_throws()
    {
        var storage = new InMemoryDistributedLockStorage(TimeProvider.System);
        var lockProvider = _CreateLock(storage);
        var manager = Substitute.For<IInternalJobManager>();
        manager
            .MigrateDefinedCronJobs(Arg.Any<(string, string)[]>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("seed boom"));
        var options = new SchedulerOptionsBuilder { UseStorageLock = true };

        var act = async () => await _InvokeSeedAsync(manager, options, lockProvider, AbortToken);

        await act.Should().ThrowAsync<InvalidOperationException>();
        // The `await using` must release the lease even when the body throws, so the resource is free for the next boot
        // rather than wedged until the TTL expires.
        (await lockProvider.IsLockedAsync(JobsKeys.CronSeedMigrationResource, AbortToken))
            .Should()
            .BeFalse();
    }

    [Fact]
    public async Task Seed_skips_when_another_node_holds_the_lock()
    {
        var storage = new InMemoryDistributedLockStorage(TimeProvider.System);

        // Node A holds the seed lock.
        var nodeA = _CreateLock(storage);
        await using var held = await nodeA.TryAcquireAsync(
            JobsKeys.CronSeedMigrationResource,
            _HoldOptions,
            AbortToken
        );
        held.Should().NotBeNull("pre-condition: the seed lock must be acquirable on an empty store");

        // Node B (same storage) attempts the guarded seed.
        var nodeB = _CreateLock(storage);
        var manager = Substitute.For<IInternalJobManager>();
        var options = new SchedulerOptionsBuilder { UseStorageLock = true };

        await _InvokeSeedAsync(manager, options, nodeB, AbortToken);

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
        await _InvokeSeedAsync(manager, options, faultingLock, AbortToken);

        await manager
            .DidNotReceive()
            .MigrateDefinedCronJobs(Arg.Any<(string, string)[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Seed_propagates_cancellation_when_caller_token_is_cancelled()
    {
        // Real pre-cancelled caller token: a host-shutdown / caller cancellation must propagate out of StartAsync.
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var cancelingLock = Substitute.For<IDistributedLock>();
        cancelingLock
            .TryAcquireAsync(Arg.Any<string>(), Arg.Any<DistributedLockAcquireOptions>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());
        var manager = Substitute.For<IInternalJobManager>();
        var options = new SchedulerOptionsBuilder { UseStorageLock = true };

        var act = async () => await _InvokeSeedAsync(manager, options, cancelingLock, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        await manager
            .DidNotReceive()
            .MigrateDefinedCronJobs(Arg.Any<(string, string)[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Seed_skips_when_acquire_throws_oce_but_caller_token_not_cancelled()
    {
        // A provider that surfaces its own internal timeout as an OperationCanceledException while the caller token is
        // NOT cancelled must be treated as an acquire fault (skip), not propagated as a host-startup crash.
        var faultingLock = Substitute.For<IDistributedLock>();
        faultingLock
            .TryAcquireAsync(Arg.Any<string>(), Arg.Any<DistributedLockAcquireOptions>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());
        var manager = Substitute.For<IInternalJobManager>();
        var options = new SchedulerOptionsBuilder { UseStorageLock = true };

        // AbortToken is normally not cancelled, so the provider OCE is swallowed as a skip; if the test is aborted,
        // cancellation can still propagate promptly.
        await _InvokeSeedAsync(manager, options, faultingLock, AbortToken);

        await manager
            .DidNotReceive()
            .MigrateDefinedCronJobs(Arg.Any<(string, string)[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Seed_skips_when_lock_factory_throws_at_resolution()
    {
        // A consumer factory that throws when DI resolves the keyed lock (e.g. a required service is missing) must be
        // treated as an acquire fault — the seed skips — not crash host startup. The guard resolves the keyed lock
        // lazily inside its try/catch precisely so this is recoverable.
        var manager = Substitute.For<IInternalJobManager>();
        var services = new ServiceCollection();
        services.AddSingleton(manager);
        services.AddKeyedSingleton<IDistributedLock>(
            JobsKeys.LockProvider,
            (_, _) => throw new InvalidOperationException("no IDistributedLock registered")
        );
        await using var sp = services.BuildServiceProvider();
        var options = new SchedulerOptionsBuilder { UseStorageLock = true };
        var hostedService = new JobsInitializationHostedService(
            sp,
            NullLogger<JobsInitializationHostedService>.Instance
        );

        // Must not throw — the throwing factory is swallowed as a skip.
        await hostedService.SeedDefinedCronJobsAsync(options, AbortToken);

        await manager
            .DidNotReceive()
            .MigrateDefinedCronJobs(Arg.Any<(string, string)[]>(), Arg.Any<CancellationToken>());
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Dead-node reclaim (intentionally unguarded — bridge-retry contract, #267)
    // ----------------------------------------------------------------------------------------------------------------

    private static JobsDeadOwnerReclaimer _CreateReclaimer(
        IInternalJobManager manager,
        SchedulerOptionsBuilder options
    ) => new(manager, options);

    [Fact]
    public async Task Reclaim_processes_every_owner_in_the_batch()
    {
        var manager = Substitute.For<IInternalJobManager>();
        var options = new SchedulerOptionsBuilder();
        var reclaimer = _CreateReclaimer(manager, options);

        await reclaimer.ReclaimAsync(["node-a@1", "node-b@2"], CancellationToken.None);

        // KTD6: the reclaimer must call ReleaseDeadNodeResources with CancellationToken.None (not the incoming token)
        // so a reclaim racing host shutdown still completes. Assert the exact token, not Arg.Any, to enforce that.
        await manager
            .Received(1)
            .ReleaseDeadNodeResources("node-a@1", Arg.Is<CancellationToken>(ct => ct == CancellationToken.None));
        await manager
            .Received(1)
            .ReleaseDeadNodeResources("node-b@2", Arg.Is<CancellationToken>(ct => ct == CancellationToken.None));
    }

    [Fact]
    public async Task Reclaim_propagates_when_release_throws_so_the_bridge_retries()
    {
        // The reclaimer must NOT swallow a failure. The shared DeadOwnerRecoveryBridge marks each owner reclaimed
        // *before* calling us and only un-marks it (→ retries on the next reconcile tick) when ReclaimAsync throws. A
        // normal return on failure would pin the owner and strand its dead-node InProgress rows — the sole reason this
        // path stays lock-free (a skip-on-contention that returned normally would do exactly that).
        var manager = Substitute.For<IInternalJobManager>();
        manager
            .ReleaseDeadNodeResources(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("release boom"));
        var options = new SchedulerOptionsBuilder();
        var reclaimer = _CreateReclaimer(manager, options);

        var act = async () => await reclaimer.ReclaimAsync(["node-a@1"], CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
