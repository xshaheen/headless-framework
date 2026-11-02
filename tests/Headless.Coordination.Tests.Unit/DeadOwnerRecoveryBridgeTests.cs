// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

public sealed class DeadOwnerRecoveryBridgeTests : TestBase
{
    private static NodeIdentity _Identity(string node, long incarnation)
    {
        return new(new NodeId(node), new NodeIncarnation(incarnation));
    }

    private static NodeLivenessSnapshot _Dead(string node, long incarnation)
    {
        return new(
            _Identity(node, incarnation),
            NodeLivenessState.Dead,
            Role: null,
            Metadata: new Dictionary<string, string>(StringComparer.Ordinal)
        );
    }

    // The bridge reclaims a batch (one owner from the event path, the whole dead set from a reconcile tick), so
    // assertions match the owner set rather than a single string.
    private static IReadOnlyCollection<string> _Batch(string owner)
    {
        return Arg.Is<IReadOnlyCollection<string>>(owners => owners.Contains(owner));
    }

    private static IReadOnlyCollection<string> _BatchWithout(string owner)
    {
        return Arg.Is<IReadOnlyCollection<string>>(owners => !owners.Contains(owner));
    }

    private static (
        DeadOwnerRecoveryBridge<IDeadOwnerReclaimer> Bridge,
        FakeMembership Membership,
        IDeadOwnerReclaimer Reclaimer
    ) _Create()
    {
        var membership = new FakeMembership();
        var reclaimer = Substitute.For<IDeadOwnerReclaimer>();
        // A one-minute interval keeps the reconcile snapshot-read timeout, which runs on the system clock, from
        // firing during direct calls.
        reclaimer.ReconcileInterval.Returns(TimeSpan.FromMinutes(1));
        var bridge = new DeadOwnerRecoveryBridge<IDeadOwnerReclaimer>(
            membership,
            reclaimer,
            new FakeTimeProvider(),
            NullLogger<DeadOwnerRecoveryBridge<IDeadOwnerReclaimer>>.Instance
        );

        return (bridge, membership, reclaimer);
    }

    [Fact]
    public async Task should_reclaim_once_on_node_left()
    {
        var (bridge, _, reclaimer) = _Create();

        await bridge.HandleEventAsync(new NodeLeft(_Identity("node-a", 5)));

        await reclaimer.Received(1).ReclaimAsync(_Batch("node-a@5"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_pass_none_token_to_reclaim_so_shutdown_does_not_tear_the_write()
    {
        var (bridge, _, reclaimer) = _Create();

        await bridge.HandleEventAsync(new NodeLeft(_Identity("node-a", 5)));

        await reclaimer.Received(1).ReclaimAsync(_Batch("node-a@5"), CancellationToken.None);
    }

    [Fact]
    public async Task should_not_reclaim_on_suspected_recovered_or_joined()
    {
        var (bridge, _, reclaimer) = _Create();

        await bridge.HandleEventAsync(new NodeSuspected(_Identity("node-a", 5)));
        await bridge.HandleEventAsync(new NodeRecovered(_Identity("node-a", 5)));
        await bridge.HandleEventAsync(new NodeJoined(_Identity("node-a", 5)));

        await reclaimer
            .DidNotReceive()
            .ReclaimAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_reclaim_dead_owner_from_snapshot_and_skip_alive_and_suspected()
    {
        var (bridge, membership, reclaimer) = _Create();
        membership.Snapshot =
        [
            _Dead("node-dead", 5),
            new NodeLivenessSnapshot(
                _Identity("node-alive", 7),
                NodeLivenessState.Alive,
                Role: null,
                Metadata: new Dictionary<string, string>(StringComparer.Ordinal)
            ),
            new NodeLivenessSnapshot(
                _Identity("node-suspect", 9),
                NodeLivenessState.Suspected,
                Role: null,
                Metadata: new Dictionary<string, string>(StringComparer.Ordinal)
            ),
        ];

        await bridge.ReconcileOnceAsync(AbortToken);

        await reclaimer.Received(1).ReclaimAsync(_Batch("node-dead@5"), Arg.Any<CancellationToken>());
        await reclaimer.Received(1).ReclaimAsync(_BatchWithout("node-alive@7"), Arg.Any<CancellationToken>());
        await reclaimer.Received(1).ReclaimAsync(_BatchWithout("node-suspect@9"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_dedup_between_event_and_reconcile()
    {
        var (bridge, membership, reclaimer) = _Create();
        membership.Snapshot = [_Dead("node-a", 5)];

        await bridge.HandleEventAsync(new NodeLeft(_Identity("node-a", 5)));
        await bridge.ReconcileOnceAsync(AbortToken);

        await reclaimer.Received(1).ReclaimAsync(_Batch("node-a@5"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_reclaim_persistent_dead_node_only_once_across_ticks()
    {
        var (bridge, membership, reclaimer) = _Create();
        membership.Snapshot = [_Dead("node-a", 5)];

        await bridge.ReconcileOnceAsync(AbortToken);
        await bridge.ReconcileOnceAsync(AbortToken);
        await bridge.ReconcileOnceAsync(AbortToken);

        await reclaimer.Received(1).ReclaimAsync(_Batch("node-a@5"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_not_suppress_a_new_incarnation_after_predecessor_aged_out()
    {
        var (bridge, membership, reclaimer) = _Create();

        membership.Snapshot = [_Dead("node-a", 5)];
        await bridge.ReconcileOnceAsync(AbortToken);

        // node-a@5 ages out of the snapshot, a fresh node-a@6 starts and dies.
        membership.Snapshot = [_Dead("node-a", 6)];
        await bridge.ReconcileOnceAsync(AbortToken);

        await reclaimer.Received(1).ReclaimAsync(_Batch("node-a@5"), Arg.Any<CancellationToken>());
        await reclaimer.Received(1).ReclaimAsync(_Batch("node-a@6"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_retry_after_a_failed_reclaim_and_not_swallow_silently()
    {
        var (bridge, membership, reclaimer) = _Create();
        membership.Snapshot = [_Dead("node-a", 5)];

        // Throw on the first reclaim, succeed afterwards.
        reclaimer
            .ReclaimAsync(_Batch("node-a@5"), Arg.Any<CancellationToken>())
            .Returns(_ => throw new InvalidOperationException("boom"), _ => Task.CompletedTask);

        // The event-path failure must not escape the handler (loop continues).
        await bridge.HandleEventAsync(new NodeLeft(_Identity("node-a", 5)));

        // The next reconcile retries because the failed owner was removed from the reclaimed-set.
        await bridge.ReconcileOnceAsync(AbortToken);

        await reclaimer.Received(2).ReclaimAsync(_Batch("node-a@5"), Arg.Any<CancellationToken>());
    }

    private sealed class FakeMembership : INodeMembership
    {
        public IReadOnlyList<NodeLivenessSnapshot> Snapshot { get; set; } = [];

        public NodeIdentity? Identity => null;

        public CancellationToken LocalMembershipLostToken => CancellationToken.None;

        public ValueTask<NodeIdentity> RegisterAsync(CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(default(NodeIdentity));
        }

        public ValueTask<bool> HeartbeatAsync(CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(true);
        }

        public ValueTask LeaveAsync(CancellationToken cancellationToken = default)
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask<bool> IsAliveAsync(NodeIdentity identity, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(false);
        }

        public ValueTask<IReadOnlyList<NodeIdentity>> GetLiveNodesAsync(CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult<IReadOnlyList<NodeIdentity>>([]);
        }

        public ValueTask<IReadOnlyList<NodeLivenessSnapshot>> GetLivenessSnapshotAsync(
            CancellationToken cancellationToken = default
        )
        {
            return ValueTask.FromResult(Snapshot);
        }

        public async IAsyncEnumerable<NodeMembershipEvent> WatchAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default
        )
        {
            await Task.CompletedTask.ConfigureAwait(false);

            yield break;
        }
    }

    [Fact]
    public async Task should_reconcile_from_the_heartbeat_snapshot_when_it_is_within_one_beat()
    {
        // given
        var store = new FakeMembershipStore();
        var timeProvider = new FakeTimeProvider();
        using var membership = _CreateMembershipService(store, timeProvider);
        var (bridge, reclaimer) = _CreateBridge(membership, timeProvider);
        store.EnqueueSnapshot(_Dead("node-a", 5));
        await membership.GetLivenessSnapshotAsync(AbortToken); // the heartbeat's read

        // when
        await bridge.ReconcileOnceAsync(AbortToken);

        // then
        store.ReadLivenessCalls.Should().Be(1);
        await reclaimer.Received(1).ReclaimAsync(_Batch("node-a@5"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_read_the_store_when_the_heartbeat_snapshot_is_older_than_one_beat()
    {
        // given (the reconcile interval is a minute, so only the beat bounds the reuse)
        var store = new FakeMembershipStore();
        var timeProvider = new FakeTimeProvider();
        using var membership = _CreateMembershipService(store, timeProvider);
        var (bridge, reclaimer) = _CreateBridge(membership, timeProvider);
        store.EnqueueSnapshot();
        await membership.GetLivenessSnapshotAsync(AbortToken);
        store.EnqueueSnapshot(_Dead("node-b", 2));
        timeProvider.Advance(new CoordinationOptions().HeartbeatInterval + TimeSpan.FromMilliseconds(1));

        // when
        await bridge.ReconcileOnceAsync(AbortToken);

        // then
        store.ReadLivenessCalls.Should().Be(2);
        await reclaimer.Received(1).ReclaimAsync(_Batch("node-b@2"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_read_the_store_instead_of_reusing_the_same_heartbeat_read_twice_when_the_clock_does_not_move()
    {
        // given (a frozen clock keeps the cached read inside its age bound forever)
        var store = new FakeMembershipStore();
        var timeProvider = new FakeTimeProvider();
        using var membership = _CreateMembershipService(store, timeProvider);
        var (bridge, reclaimer) = _CreateBridge(membership, timeProvider);
        store.EnqueueSnapshot(_Dead("node-a", 5));
        await membership.GetLivenessSnapshotAsync(AbortToken); // the heartbeat's read
        await bridge.ReconcileOnceAsync(AbortToken); // decides from that read
        store.EnqueueSnapshot(_Dead("node-a", 5), _Dead("node-b", 2)); // node-b dies after it

        // when
        await bridge.ReconcileOnceAsync(AbortToken);

        // then
        store.ReadLivenessCalls.Should().Be(2);
        await reclaimer.Received(1).ReclaimAsync(_Batch("node-b@2"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_read_the_store_when_no_snapshot_was_read_yet()
    {
        // given
        var store = new FakeMembershipStore();
        var timeProvider = new FakeTimeProvider();
        using var membership = _CreateMembershipService(store, timeProvider);
        var (bridge, _) = _CreateBridge(membership, timeProvider);

        // when
        await bridge.ReconcileOnceAsync(AbortToken);

        // then
        store.ReadLivenessCalls.Should().Be(1);
    }

    private static MembershipService _CreateMembershipService(FakeMembershipStore store, TimeProvider timeProvider)
    {
        var nodeIdProvider = Substitute.For<INodeIdProvider>();

        return new MembershipService(
            store,
            nodeIdProvider,
            new CoordinationOptions(),
            new MembershipEventSource(NullLogger<MembershipEventSource>.Instance),
            timeProvider,
            hostApplicationLifetime: null,
            NullLogger<MembershipService>.Instance
        );
    }

    private static (DeadOwnerRecoveryBridge<IDeadOwnerReclaimer> Bridge, IDeadOwnerReclaimer Reclaimer) _CreateBridge(
        MembershipService membership,
        TimeProvider timeProvider
    )
    {
        var reclaimer = Substitute.For<IDeadOwnerReclaimer>();
        reclaimer.ReconcileInterval.Returns(TimeSpan.FromMinutes(1));
        var bridge = new DeadOwnerRecoveryBridge<IDeadOwnerReclaimer>(
            membership,
            reclaimer,
            timeProvider,
            NullLogger<DeadOwnerRecoveryBridge<IDeadOwnerReclaimer>>.Instance
        );

        return (bridge, reclaimer);
    }
}
