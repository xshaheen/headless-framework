// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;
using Headless.Jobs;
using Headless.Jobs.BackgroundServices;
using Headless.Jobs.Instrumentation;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Interfaces.Managers;
using Headless.Jobs.JobsThreadPool;
using Headless.Jobs.Models;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tests;

/// <summary>
/// Pins the orphaned-owner sweep: rows stamped by an owner identity that is absent from the coordination
/// liveness snapshot — a superseded incarnation (never classified Dead, so the dead-owner bridge never sees
/// it) or a dead identity pruned past retention — are reclaimed via the per-policy dead-node release. Owners
/// present in the snapshot (Alive, Suspected, or Dead-retained) are never touched here.
/// </summary>
public sealed class JobsOrphanedOwnerSweepTests : TestBase
{
    private static readonly TimeSpan _waitBudget = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task reclaims_owners_absent_from_the_liveness_snapshot_and_leaves_observable_owners_alone()
    {
        var manager = _Manager();
        var aliveIdentity = new NodeIdentity(new NodeId("alive"), new NodeIncarnation(2));
        var suspectedIdentity = new NodeIdentity(new NodeId("slow"), new NodeIncarnation(4));
        manager
            .GetActiveOwnerIdsAsync(Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult<string[]>(["ghost@1", aliveIdentity.ToString(), suspectedIdentity.ToString(), "self@3"])
            );

        var membership = Substitute.For<INodeMembership>();
        membership
            .GetLivenessSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(
                new ValueTask<IReadOnlyList<NodeLivenessSnapshot>>([
                    new NodeLivenessSnapshot(
                        aliveIdentity,
                        NodeLivenessState.Alive,
                        Role: null,
                        new Dictionary<string, string>(StringComparer.Ordinal)
                    ),
                    new NodeLivenessSnapshot(
                        suspectedIdentity,
                        NodeLivenessState.Suspected,
                        Role: null,
                        new Dictionary<string, string>(StringComparer.Ordinal)
                    ),
                ])
            );

        var ownerIdentity = Substitute.For<IJobsOwnerIdentity>();
        ownerIdentity.MembershipLostToken.Returns(CancellationToken.None);
        ownerIdentity
            .TryGetStampOwner(out Arg.Any<string?>())
            .Returns(call =>
            {
                call[0] = "self@3";
                return true;
            });

        using var service = _Service(manager, ownerIdentity, membership);
        await service.StartAsync(AbortToken);

        try
        {
            await _WaitForAsync(() =>
                manager.ReceivedCalls().Any(c => c.GetMethodInfo().Name == nameof(manager.ReleaseDeadNodeResources))
            );

            await manager
                .Received(1)
                .ReleaseDeadNodeResources(
                    Arg.Is<IReadOnlyCollection<string>>(owners => owners.SequenceEqual(new[] { "ghost@1" })),
                    CancellationToken.None
                );
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task does_not_reclaim_an_owner_that_registers_and_stamps_work_between_the_owner_and_snapshot_reads()
    {
        const string newlyRegisteredOwner = "new-owner@1";
        var identity = new NodeIdentity(new NodeId("new-owner"), new NodeIncarnation(1));
        var ownerScanCompleted = false;
        var snapshotObservedRegistration = false;
        var manager = _Manager();
        manager
            .GetActiveOwnerIdsAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                ownerScanCompleted = true;
                return Task.FromResult<string[]>([newlyRegisteredOwner]);
            });

        var membership = Substitute.For<INodeMembership>();
        membership
            .GetLivenessSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                snapshotObservedRegistration = ownerScanCompleted;
                IReadOnlyList<NodeLivenessSnapshot> snapshots = ownerScanCompleted
                    ?
                    [
                        new NodeLivenessSnapshot(
                            identity,
                            NodeLivenessState.Alive,
                            Role: null,
                            new Dictionary<string, string>(StringComparer.Ordinal)
                        ),
                    ]
                    : [];
                return new ValueTask<IReadOnlyList<NodeLivenessSnapshot>>(snapshots);
            });

        var ownerIdentity = Substitute.For<IJobsOwnerIdentity>();
        ownerIdentity.MembershipLostToken.Returns(CancellationToken.None);

        using var service = _Service(manager, ownerIdentity, membership);
        await service.StartAsync(AbortToken);

        try
        {
            await _WaitForAsync(() => snapshotObservedRegistration);

            await manager
                .DidNotReceive()
                .ReleaseDeadNodeResources(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>());
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task does_not_start_the_release_batch_after_local_membership_is_lost()
    {
        var manager = _Manager();
        manager.GetActiveOwnerIdsAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<string[]>(["ghost@1"]));

        var membership = Substitute.For<INodeMembership>();
        membership
            .GetLivenessSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(new ValueTask<IReadOnlyList<NodeLivenessSnapshot>>([]));

        using var membershipLost = new CancellationTokenSource();
        var finalFenceReached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ownerIdentity = Substitute.For<IJobsOwnerIdentity>();
        ownerIdentity.MembershipLostToken.Returns(membershipLost.Token);
        ownerIdentity
            .TryGetStampOwner(out Arg.Any<string?>())
            .Returns(call =>
            {
                call[0] = null;
                membershipLost.Cancel();
                finalFenceReached.TrySetResult();
                return false;
            });

        using var service = _Service(manager, ownerIdentity, membership);
        await service.StartAsync(AbortToken);

        try
        {
            await finalFenceReached.Task.WaitAsync(_waitBudget, AbortToken);

            await manager
                .DidNotReceive()
                .ReleaseDeadNodeResources(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>());
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task does_not_sweep_without_a_membership_registration()
    {
        // In-memory path: no coordination provider, single-process ownership — nothing can be orphaned and no
        // snapshot exists to diff against.
        var manager = _Manager();
        var ownerIdentity = Substitute.For<IJobsOwnerIdentity>();
        ownerIdentity.MembershipLostToken.Returns(CancellationToken.None);

        using var service = _Service(manager, ownerIdentity, membership: null);
        await service.StartAsync(AbortToken);

        try
        {
            // Let the loop demonstrably tick at least once.
            await _WaitForAsync(() =>
                manager.ReceivedCalls().Any(c => c.GetMethodInfo().Name == nameof(manager.ReclaimStalledResources))
            );

            await manager.DidNotReceive().GetActiveOwnerIdsAsync(Arg.Any<CancellationToken>());
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    private static async Task _WaitForAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.Add(_waitBudget);
        while (!condition())
        {
            DateTime.UtcNow.Should().BeBefore(deadline, "the fallback loop should have reached the assertion state");
            await Task.Delay(10, AbortToken);
        }
    }

    private static IInternalJobManager _Manager()
    {
        var manager = Substitute.For<IInternalJobManager>();
        manager.ReclaimStalledResources(Arg.Any<CancellationToken>()).Returns(Task.FromResult(0));
        manager
            .RunTimedOutTickers(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Array.Empty<JobExecutionState>()));
        manager
            .ReleaseDeadNodeResources(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        manager.GetActiveOwnerIdsAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(Array.Empty<string>()));
        return manager;
    }

    private static JobsFallbackBackgroundService _Service(
        IInternalJobManager manager,
        IJobsOwnerIdentity ownerIdentity,
        INodeMembership? membership
    )
    {
        var services = new ServiceCollection();
        services.AddSingleton(manager);
        var serviceProvider = services.BuildServiceProvider();
        var taskScheduler = new JobsTaskScheduler(maxConcurrency: 1, timeProvider: TimeProvider.System);
        var handler = new JobsExecutionTaskHandler(
            serviceProvider,
            TimeProvider.System,
            Substitute.For<IJobsInstrumentation>(),
            manager,
            JobFunctionRegistryBuilder.Build([], [], []),
            new JobsExecutionCancellationRegistry(),
            new SchedulerOptionsBuilder(),
            NullLogger<JobsExecutionTaskHandler>.Instance
        );

        return new JobsFallbackBackgroundService(
            manager,
            new SchedulerOptionsBuilder { FallbackIntervalChecker = TimeSpan.FromMilliseconds(20) },
            handler,
            taskScheduler,
            new JobFunctionConcurrencyGate(),
            JobFunctionRegistryBuilder.Build([], [], []),
            TimeProvider.System,
            ownerIdentity,
            NullLogger<JobsFallbackBackgroundService>.Instance,
            membership
        );
    }
}
