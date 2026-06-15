// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

public sealed class DeadOwnerRecoveryBridgeTests
{
    private static NodeIdentity Identity(string node, long incarnation) =>
        new(new NodeId(node), new NodeIncarnation(incarnation));

    private static NodeLivenessSnapshot Dead(string node, long incarnation) =>
        new(
            Identity(node, incarnation),
            NodeLivenessState.Dead,
            Role: null,
            Metadata: new Dictionary<string, string>(StringComparer.Ordinal)
        );

    private static (
        DeadOwnerRecoveryBridge<IDeadOwnerReclaimer> Bridge,
        FakeMembership Membership,
        IDeadOwnerReclaimer Reclaimer
    ) Create()
    {
        var membership = new FakeMembership();
        var reclaimer = Substitute.For<IDeadOwnerReclaimer>();
        // A positive interval keeps the reconcile snapshot-read timeout from firing during direct calls; the fake
        // clock only advances when a test asks it to, so the bounded read never times out under test.
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
        var (bridge, _, reclaimer) = Create();

        await bridge.HandleEventAsync(new NodeLeft(Identity("node-a", 5)));

        await reclaimer.Received(1).ReclaimAsync("node-a@5", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_pass_none_token_to_reclaim_so_shutdown_does_not_tear_the_write()
    {
        var (bridge, _, reclaimer) = Create();

        await bridge.HandleEventAsync(new NodeLeft(Identity("node-a", 5)));

        await reclaimer.Received(1).ReclaimAsync("node-a@5", CancellationToken.None);
    }

    [Fact]
    public async Task should_not_reclaim_on_suspected_recovered_or_joined()
    {
        var (bridge, _, reclaimer) = Create();

        await bridge.HandleEventAsync(new NodeSuspected(Identity("node-a", 5)));
        await bridge.HandleEventAsync(new NodeRecovered(Identity("node-a", 5)));
        await bridge.HandleEventAsync(new NodeJoined(Identity("node-a", 5)));

        await reclaimer.DidNotReceive().ReclaimAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_reclaim_dead_owner_from_snapshot_and_skip_alive_and_suspected()
    {
        var (bridge, membership, reclaimer) = Create();
        membership.Snapshot =
        [
            Dead("node-dead", 5),
            new NodeLivenessSnapshot(
                Identity("node-alive", 7),
                NodeLivenessState.Alive,
                Role: null,
                Metadata: new Dictionary<string, string>(StringComparer.Ordinal)
            ),
            new NodeLivenessSnapshot(
                Identity("node-suspect", 9),
                NodeLivenessState.Suspected,
                Role: null,
                Metadata: new Dictionary<string, string>(StringComparer.Ordinal)
            ),
        ];

        await bridge.ReconcileOnceAsync(TestContext.Current.CancellationToken);

        await reclaimer.Received(1).ReclaimAsync("node-dead@5", Arg.Any<CancellationToken>());
        await reclaimer.DidNotReceive().ReclaimAsync("node-alive@7", Arg.Any<CancellationToken>());
        await reclaimer.DidNotReceive().ReclaimAsync("node-suspect@9", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_dedup_between_event_and_reconcile()
    {
        var (bridge, membership, reclaimer) = Create();
        membership.Snapshot = [Dead("node-a", 5)];

        await bridge.HandleEventAsync(new NodeLeft(Identity("node-a", 5)));
        await bridge.ReconcileOnceAsync(TestContext.Current.CancellationToken);

        await reclaimer.Received(1).ReclaimAsync("node-a@5", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_reclaim_persistent_dead_node_only_once_across_ticks()
    {
        var (bridge, membership, reclaimer) = Create();
        membership.Snapshot = [Dead("node-a", 5)];

        await bridge.ReconcileOnceAsync(TestContext.Current.CancellationToken);
        await bridge.ReconcileOnceAsync(TestContext.Current.CancellationToken);
        await bridge.ReconcileOnceAsync(TestContext.Current.CancellationToken);

        await reclaimer.Received(1).ReclaimAsync("node-a@5", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_not_suppress_a_new_incarnation_after_predecessor_aged_out()
    {
        var (bridge, membership, reclaimer) = Create();

        membership.Snapshot = [Dead("node-a", 5)];
        await bridge.ReconcileOnceAsync(TestContext.Current.CancellationToken);

        // node-a@5 ages out of the snapshot, a fresh node-a@6 starts and dies.
        membership.Snapshot = [Dead("node-a", 6)];
        await bridge.ReconcileOnceAsync(TestContext.Current.CancellationToken);

        await reclaimer.Received(1).ReclaimAsync("node-a@5", Arg.Any<CancellationToken>());
        await reclaimer.Received(1).ReclaimAsync("node-a@6", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_retry_after_a_failed_reclaim_and_not_swallow_silently()
    {
        var (bridge, membership, reclaimer) = Create();
        membership.Snapshot = [Dead("node-a", 5)];

        // Throw on the first reclaim, succeed afterwards.
        reclaimer
            .ReclaimAsync("node-a@5", Arg.Any<CancellationToken>())
            .Returns(_ => throw new InvalidOperationException("boom"), _ => Task.CompletedTask);

        // The event-path failure must not escape the handler (loop continues).
        await bridge.HandleEventAsync(new NodeLeft(Identity("node-a", 5)));

        // The next reconcile retries because the failed owner was removed from the reclaimed-set.
        await bridge.ReconcileOnceAsync(TestContext.Current.CancellationToken);

        await reclaimer.Received(2).ReclaimAsync("node-a@5", Arg.Any<CancellationToken>());
    }

    private sealed class FakeMembership : INodeMembership
    {
        public IReadOnlyList<NodeLivenessSnapshot> Snapshot { get; set; } = [];

        public NodeIdentity? Identity => null;

        public CancellationToken LocalMembershipLostToken => CancellationToken.None;

        public ValueTask<NodeIdentity> RegisterAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(default(NodeIdentity));

        public ValueTask<bool> HeartbeatAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(true);

        public ValueTask LeaveAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask<bool> IsAliveAsync(NodeIdentity identity, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(false);

        public ValueTask<IReadOnlyList<NodeIdentity>> GetLiveNodesAsync(
            CancellationToken cancellationToken = default
        ) => ValueTask.FromResult<IReadOnlyList<NodeIdentity>>([]);

        public ValueTask<IReadOnlyList<NodeLivenessSnapshot>> GetLivenessSnapshotAsync(
            CancellationToken cancellationToken = default
        ) => ValueTask.FromResult(Snapshot);

        public async IAsyncEnumerable<NodeMembershipEvent> WatchAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default
        )
        {
            await Task.CompletedTask.ConfigureAwait(false);

            yield break;
        }
    }
}
