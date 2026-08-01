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

            await manager.Received(1).ReleaseDeadNodeResources("ghost@1", CancellationToken.None);
            await manager
                .DidNotReceive()
                .ReleaseDeadNodeResources(aliveIdentity.ToString(), Arg.Any<CancellationToken>());
            await manager
                .DidNotReceive()
                .ReleaseDeadNodeResources(suspectedIdentity.ToString(), Arg.Any<CancellationToken>());
            await manager.DidNotReceive().ReleaseDeadNodeResources("self@3", Arg.Any<CancellationToken>());
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
        manager.ReleaseDeadNodeResources(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
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
