// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;
using Headless.Jobs;
using Headless.Jobs.BackgroundServices;
using Headless.Jobs.Instrumentation;
using Headless.Jobs.JobsThreadPool;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tests;

/// <summary>
/// Pins the orphaned-owner sweep: rows stamped by an owner identity that is neither Alive nor Suspected in the
/// coordination liveness snapshot — a Dead identity, a superseded incarnation (never classified Dead, so the dead-owner
/// bridge never sees it), or a dead identity pruned past retention — are reclaimed via the per-policy dead-node
/// release. Alive and Suspected owners are never touched here.
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
                Task.FromResult<string[]>([
                    _Tag("ghost@1"),
                    _Tag(aliveIdentity),
                    _Tag(suspectedIdentity),
                    _Tag("self@3"),
                ])
            );

        var membership = Substitute.For<INodeMembership>();
        membership.ClusterName.Returns(_Cluster);
        membership
            .GetLivenessSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(
                new ValueTask<IReadOnlyList<NodeLivenessSnapshot>>([
                    new NodeLivenessSnapshot(
                        aliveIdentity,
                        NodeLivenessState.Alive,
                        Role: null,
                        new Dictionary<string, string>(StringComparer.Ordinal),
                        DateTimeOffset.UnixEpoch
                    ),
                    new NodeLivenessSnapshot(
                        suspectedIdentity,
                        NodeLivenessState.Suspected,
                        Role: null,
                        new Dictionary<string, string>(StringComparer.Ordinal),
                        DateTimeOffset.UnixEpoch
                    ),
                ])
            );

        var ownerIdentity = Substitute.For<IJobsOwnerIdentity>();
        ownerIdentity.MembershipLostToken.Returns(CancellationToken.None);
        ownerIdentity
            .TryGetStampOwner(out Arg.Any<string?>())
            .Returns(call =>
            {
                call[0] = _Tag("self@3");
                return true;
            });

        await using var harness = _Service(manager, ownerIdentity, membership);
        var service = harness.Service;
        await service.StartAsync(AbortToken);

        try
        {
            await _WaitForAsync(() =>
                manager
                    .ReceivedCalls()
                    .Any(c =>
                        string.Equals(
                            c.GetMethodInfo().Name,
                            nameof(manager.ReleaseDeadNodeResources),
                            StringComparison.Ordinal
                        )
                    )
            );

            await manager
                .Received(1)
                .ReleaseDeadNodeResources(
                    Arg.Is<IReadOnlyCollection<string>>(owners => owners.SequenceEqual(new[] { _Tag("ghost@1") })),
                    CancellationToken.None
                );
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task reclaims_a_dead_owner_with_stamped_rows_but_not_a_suspected_one()
    {
        // The dead-owner bridge reclaims each Dead identity once; rows a dead owner still holds after that (or after a
        // missed NodeLeft) must not wait for the identity to be pruned — up to RedisKnownNodeRetention (7 days) on
        // Redis. A Suspected owner may still be alive and renewing, so it is never swept.
        var manager = _Manager();
        var deadIdentity = new NodeIdentity(new NodeId("dead"), new NodeIncarnation(5));
        var suspectedIdentity = new NodeIdentity(new NodeId("slow"), new NodeIncarnation(4));
        manager
            .GetActiveOwnerIdsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string[]>([_Tag(deadIdentity), _Tag(suspectedIdentity)]));

        var membership = Substitute.For<INodeMembership>();
        membership.ClusterName.Returns(_Cluster);
        membership
            .GetLivenessSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(
                new ValueTask<IReadOnlyList<NodeLivenessSnapshot>>([
                    new NodeLivenessSnapshot(
                        deadIdentity,
                        NodeLivenessState.Dead,
                        Role: null,
                        new Dictionary<string, string>(StringComparer.Ordinal),
                        DateTimeOffset.UnixEpoch
                    ),
                    new NodeLivenessSnapshot(
                        suspectedIdentity,
                        NodeLivenessState.Suspected,
                        Role: null,
                        new Dictionary<string, string>(StringComparer.Ordinal),
                        DateTimeOffset.UnixEpoch
                    ),
                ])
            );

        var ownerIdentity = Substitute.For<IJobsOwnerIdentity>();
        ownerIdentity.MembershipLostToken.Returns(CancellationToken.None);

        await using var harness = _Service(manager, ownerIdentity, membership);
        var service = harness.Service;
        await service.StartAsync(AbortToken);

        try
        {
            await _WaitForAsync(() =>
                manager
                    .ReceivedCalls()
                    .Any(c =>
                        string.Equals(
                            c.GetMethodInfo().Name,
                            nameof(manager.ReleaseDeadNodeResources),
                            StringComparison.Ordinal
                        )
                    )
            );

            await manager
                .Received(1)
                .ReleaseDeadNodeResources(
                    Arg.Is<IReadOnlyCollection<string>>(owners => owners.SequenceEqual(new[] { _Tag(deadIdentity) })),
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
        var newlyRegisteredOwner = _Tag("new-owner@1");
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
        membership.ClusterName.Returns(_Cluster);
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
                            new Dictionary<string, string>(StringComparer.Ordinal),
                            DateTimeOffset.UnixEpoch
                        ),
                    ]
                    : [];
                return new ValueTask<IReadOnlyList<NodeLivenessSnapshot>>(snapshots);
            });

        var ownerIdentity = Substitute.For<IJobsOwnerIdentity>();
        ownerIdentity.MembershipLostToken.Returns(CancellationToken.None);

        await using var harness = _Service(manager, ownerIdentity, membership);
        var service = harness.Service;
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
        manager
            .GetActiveOwnerIdsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string[]>([_Tag("ghost@1")]));

        var membership = Substitute.For<INodeMembership>();
        membership.ClusterName.Returns(_Cluster);
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

        await using var harness = _Service(manager, ownerIdentity, membership);
        var service = harness.Service;
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
    public async Task does_not_reclaim_an_owner_that_registers_and_stamps_between_the_sweep_reads()
    {
        // The sweep's safety argument requires reading the stamped owners BEFORE the liveness snapshot: a live
        // owner present in the scan registered no later than the scan, so it must appear in the later snapshot.
        // This test pins the ordering by simulating a node whose registration+stamping lands immediately after
        // the sweep's FIRST read, whichever that is. Scan-first: the owner is not in the scan yet and this
        // iteration ignores it. Snapshot-first (the defect): the owner is in the scan but not the earlier
        // snapshot, and a live node's fresh claims are put through the dead-node release.
        var manager = _Manager();
        var interleavedIdentity = new NodeIdentity(new NodeId("late"), new NodeIncarnation(1));
        var registered = false;

        manager
            .GetActiveOwnerIdsAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                string[] result = registered ? [_Tag(interleavedIdentity)] : [];
                registered = true;
                return Task.FromResult(result);
            });

        var membership = Substitute.For<INodeMembership>();
        membership.ClusterName.Returns(_Cluster);
        membership
            .GetLivenessSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                IReadOnlyList<NodeLivenessSnapshot> result = registered
                    ?
                    [
                        new NodeLivenessSnapshot(
                            interleavedIdentity,
                            NodeLivenessState.Alive,
                            Role: null,
                            new Dictionary<string, string>(StringComparer.Ordinal),
                            DateTimeOffset.UnixEpoch
                        ),
                    ]
                    : [];
                registered = true;
                return new ValueTask<IReadOnlyList<NodeLivenessSnapshot>>(result);
            });

        var ownerIdentity = Substitute.For<IJobsOwnerIdentity>();
        ownerIdentity.MembershipLostToken.Returns(CancellationToken.None);

        await using var harness = _Service(manager, ownerIdentity, membership);
        var service = harness.Service;
        await service.StartAsync(AbortToken);

        try
        {
            // RunTimedOutTickers runs after the orphan sweep inside the same iteration, so observing it proves
            // the sweep completed both reads and any (defective) reclaim would already have been issued.
            await _WaitForAsync(() =>
                manager
                    .ReceivedCalls()
                    .Any(c =>
                        string.Equals(
                            c.GetMethodInfo().Name,
                            nameof(manager.RunTimedOutTickers),
                            StringComparison.Ordinal
                        )
                    )
            );

            await manager
                .DidNotReceive()
                .ReleaseDeadNodeResources(_Tag(interleavedIdentity), Arg.Any<CancellationToken>());
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

        await using var harness = _Service(manager, ownerIdentity, membership: null);
        var service = harness.Service;
        await service.StartAsync(AbortToken);

        try
        {
            // Let the loop demonstrably tick at least once.
            await _WaitForAsync(() =>
                manager
                    .ReceivedCalls()
                    .Any(c =>
                        string.Equals(
                            c.GetMethodInfo().Name,
                            nameof(manager.ReclaimStalledResources),
                            StringComparison.Ordinal
                        )
                    )
            );

            await manager.DidNotReceive().GetActiveOwnerIdsAsync(Arg.Any<CancellationToken>());
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task leaves_owners_stamped_by_another_cluster_sharing_the_store_alone()
    {
        // Two apps share one Jobs store under different coordination clusters. This node's snapshot lists only its
        // own cluster, so the other app's live owner is absent from it without being orphaned.
        var manager = _Manager();
        var self = new NodeIdentity(new NodeId("node-a"), new NodeIncarnation(1));
        var otherCluster = NodeOwnerTag.Format("billing", self);
        manager
            .GetActiveOwnerIdsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string[]>([_Tag(self), otherCluster, _Tag("ghost@1")]));
        var membership = Substitute.For<INodeMembership>();
        membership.ClusterName.Returns(_Cluster);
        membership
            .GetLivenessSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(
                new ValueTask<IReadOnlyList<NodeLivenessSnapshot>>([
                    new NodeLivenessSnapshot(
                        self,
                        NodeLivenessState.Alive,
                        Role: null,
                        new Dictionary<string, string>(StringComparer.Ordinal),
                        DateTimeOffset.UnixEpoch
                    ),
                ])
            );
        var ownerIdentity = Substitute.For<IJobsOwnerIdentity>();
        ownerIdentity.MembershipLostToken.Returns(CancellationToken.None);

        await using var harness = _Service(manager, ownerIdentity, membership);
        await harness.Service.StartAsync(AbortToken);

        try
        {
            await _WaitForAsync(() =>
                manager
                    .ReceivedCalls()
                    .Any(c =>
                        string.Equals(
                            c.GetMethodInfo().Name,
                            nameof(manager.ReleaseDeadNodeResources),
                            StringComparison.Ordinal
                        )
                    )
            );

            await manager
                .Received(1)
                .ReleaseDeadNodeResources(
                    Arg.Is<IReadOnlyCollection<string>>(owners => owners.SequenceEqual(new[] { _Tag("ghost@1") })),
                    CancellationToken.None
                );
        }
        finally
        {
            await harness.Service.StopAsync(CancellationToken.None);
        }
    }

    private const string _Cluster = "orders";

    private static string _Tag(string nodeAtIncarnation) =>
        NodeOwnerTag.Format(_Cluster, NodeIdentity.Parse(nodeAtIncarnation));

    private static string _Tag(NodeIdentity identity) => NodeOwnerTag.Format(_Cluster, identity);

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

    private static FallbackServiceHarness _Service(
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
            NullLogger<JobsExecutionTaskHandler>.Instance,
            new JobsFunctionTagBound(JobFunctionRegistryBuilder.Build([], [], []))
        );

        return new FallbackServiceHarness(
#pragma warning disable CA2000 // FallbackServiceHarness takes ownership of the service and disposes it with the scheduler and provider.
            new JobsFallbackBackgroundService(
                manager,
                new SchedulerOptionsBuilder { FallbackIntervalChecker = TimeSpan.FromMilliseconds(20) },
                handler,
                taskScheduler,
                new JobFunctionConcurrencyGate(),
                JobFunctionRegistryBuilder.Build([], [], []),
                TimeProvider.System,
                ownerIdentity,
                TestActivationBarrier.Opened(),
                NullLogger<JobsFallbackBackgroundService>.Instance,
                membership
            ),
#pragma warning restore CA2000
            taskScheduler,
            serviceProvider
        );
    }

    private sealed class FallbackServiceHarness(
        JobsFallbackBackgroundService service,
        JobsTaskScheduler taskScheduler,
        ServiceProvider serviceProvider
    ) : IAsyncDisposable
    {
        public JobsFallbackBackgroundService Service { get; } = service;

        public async ValueTask DisposeAsync()
        {
            Service.Dispose();
            await taskScheduler.DisposeAsync().ConfigureAwait(false);
            await serviceProvider.DisposeAsync().ConfigureAwait(false);
        }
    }
}
