// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
            Metadata: new Dictionary<string, string>(StringComparer.Ordinal),
            LastHeartbeatAt: DateTimeOffset.UnixEpoch
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

        await reclaimer.Received(1).ReclaimAsync(_Batch("orders/node-a@5"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_pass_none_token_to_reclaim_so_shutdown_does_not_tear_the_write()
    {
        var (bridge, _, reclaimer) = _Create();

        await bridge.HandleEventAsync(new NodeLeft(_Identity("node-a", 5)));

        await reclaimer.Received(1).ReclaimAsync(_Batch("orders/node-a@5"), CancellationToken.None);
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
                Metadata: new Dictionary<string, string>(StringComparer.Ordinal),
                LastHeartbeatAt: DateTimeOffset.UnixEpoch
            ),
            new NodeLivenessSnapshot(
                _Identity("node-suspect", 9),
                NodeLivenessState.Suspected,
                Role: null,
                Metadata: new Dictionary<string, string>(StringComparer.Ordinal),
                LastHeartbeatAt: DateTimeOffset.UnixEpoch
            ),
        ];

        await bridge.ReconcileOnceAsync(AbortToken);

        await reclaimer.Received(1).ReclaimAsync(_Batch("orders/node-dead@5"), Arg.Any<CancellationToken>());
        await reclaimer.Received(1).ReclaimAsync(_BatchWithout("orders/node-alive@7"), Arg.Any<CancellationToken>());
        await reclaimer.Received(1).ReclaimAsync(_BatchWithout("orders/node-suspect@9"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_dedup_between_event_and_reconcile()
    {
        var (bridge, membership, reclaimer) = _Create();
        membership.Snapshot = [_Dead("node-a", 5)];

        await bridge.HandleEventAsync(new NodeLeft(_Identity("node-a", 5)));
        await bridge.ReconcileOnceAsync(AbortToken);

        await reclaimer.Received(1).ReclaimAsync(_Batch("orders/node-a@5"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_reclaim_persistent_dead_node_only_once_across_ticks()
    {
        var (bridge, membership, reclaimer) = _Create();
        membership.Snapshot = [_Dead("node-a", 5)];

        await bridge.ReconcileOnceAsync(AbortToken);
        await bridge.ReconcileOnceAsync(AbortToken);
        await bridge.ReconcileOnceAsync(AbortToken);

        await reclaimer.Received(1).ReclaimAsync(_Batch("orders/node-a@5"), Arg.Any<CancellationToken>());
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

        await reclaimer.Received(1).ReclaimAsync(_Batch("orders/node-a@5"), Arg.Any<CancellationToken>());
        await reclaimer.Received(1).ReclaimAsync(_Batch("orders/node-a@6"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_retry_after_a_failed_reclaim_and_not_swallow_silently()
    {
        var (bridge, membership, reclaimer) = _Create();
        membership.Snapshot = [_Dead("node-a", 5)];

        // Throw on the first reclaim, succeed afterwards.
        reclaimer
            .ReclaimAsync(_Batch("orders/node-a@5"), Arg.Any<CancellationToken>())
            .Returns(_ => throw new InvalidOperationException("boom"), _ => Task.CompletedTask);

        // The event-path failure must not escape the handler (loop continues).
        await bridge.HandleEventAsync(new NodeLeft(_Identity("node-a", 5)));

        // The next reconcile retries because the failed owner was removed from the reclaimed-set.
        await bridge.ReconcileOnceAsync(AbortToken);

        await reclaimer.Received(2).ReclaimAsync(_Batch("orders/node-a@5"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_drive_a_consumer_reclaimer_registered_through_the_public_entry_on_both_paths()
    {
        // given
        await using var provider = _BuildConsumerHost(TimeSpan.FromSeconds(10), out var membership);
        var bridge = _SingleBridge(provider);
        var reclaimer = provider.GetRequiredService<ConsumerReclaimer>();
        membership.Snapshot = [_Dead("node-b", 3)];

        // when
        await bridge.HandleEventAsync(new NodeLeft(_Identity("node-a", 5)));
        await bridge.ReconcileOnceAsync(AbortToken);

        // then
        reclaimer.Batches.Should().HaveCount(2);
        reclaimer.Batches[0].Should().Equal("orders/node-a@5");
        reclaimer.Batches[1].Should().Equal("orders/node-b@3");
        reclaimer.Tokens.Should().AllSatisfy(token => token.Should().Be(CancellationToken.None));
    }

    [Fact]
    public void should_add_one_reclaimer_and_one_bridge_when_registered_twice()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddDeadOwnerReclaimer<ConsumerReclaimer>();
        services.AddDeadOwnerReclaimer<ConsumerReclaimer>();
        services.AddDeadOwnerReclaimer<OtherConsumerReclaimer>();

        // then
        services.Should().ContainSingle(d => d.ServiceType == typeof(ConsumerReclaimer));
        services
            .Where(d => d.ServiceType == typeof(IHostedService))
            .Select(d => d.ImplementationType)
            .Should()
            .BeEquivalentTo([
                typeof(DeadOwnerRecoveryBridge<ConsumerReclaimer>),
                typeof(DeadOwnerRecoveryBridge<OtherConsumerReclaimer>),
            ]);
    }

    [Fact]
    public async Task should_clamp_a_reclaimer_interval_above_half_the_dead_retention_window()
    {
        // given (the default 30 s retention window caps the cadence at 15 s)
        await using var provider = _BuildConsumerHost(TimeSpan.FromMinutes(1), out _);

        // when
        var bridge = _SingleBridge(provider);

        // then
        bridge.ReconcileInterval.Should().Be(TimeSpan.FromSeconds(15));
    }

    [Fact]
    public async Task should_derive_half_the_dead_retention_window_when_the_reclaimer_states_no_interval()
    {
        // given
        await using var provider = _BuildConsumerHost(configured: null, out _);

        // when
        var bridge = _SingleBridge(provider);

        // then
        bridge.ReconcileInterval.Should().Be(TimeSpan.FromSeconds(15));
    }

    [Fact]
    public async Task should_keep_a_reclaimer_interval_when_the_host_has_no_coordination_provider()
    {
        // given (the options system still resolves default coordination options, but no store enforces their window)
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<INodeMembership, NullNodeMembership>();
        services.AddSingleton(new ConsumerReclaimerSettings(TimeSpan.FromMinutes(1)));
        services.AddDeadOwnerReclaimer<ConsumerReclaimer>();
        await using var provider = services.BuildServiceProvider();

        // when
        var bridge = _SingleBridge(provider);

        // then
        bridge.ReconcileInterval.Should().Be(TimeSpan.FromMinutes(1));
    }

    private static ServiceProvider _BuildConsumerHost(TimeSpan? configured, out FakeMembership membership)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        membership = new FakeMembership();
        services.AddSingleton<INodeMembership>(membership);
        services.AddSingleton<TimeProvider>(new FakeTimeProvider());
        services.AddSingleton(new ConsumerReclaimerSettings(configured));
        services.AddDeadOwnerReclaimer<ConsumerReclaimer>();

        return services.BuildServiceProvider();
    }

    private static DeadOwnerRecoveryBridge<ConsumerReclaimer> _SingleBridge(IServiceProvider provider)
    {
        return provider
            .GetServices<IHostedService>()
            .Should()
            .ContainSingle()
            .Which.Should()
            .BeOfType<DeadOwnerRecoveryBridge<ConsumerReclaimer>>()
            .Subject;
    }

    private sealed record ConsumerReclaimerSettings(TimeSpan? ReconcileInterval);

    // Stands in for a reclaimer an application or another package defines, outside Coordination's friend assemblies.
    private sealed class ConsumerReclaimer(ConsumerReclaimerSettings settings) : IDeadOwnerReclaimer
    {
        public List<string[]> Batches { get; } = [];

        public List<CancellationToken> Tokens { get; } = [];

        public TimeSpan? ReconcileInterval => settings.ReconcileInterval;

        public Task ReclaimAsync(IReadOnlyCollection<string> owners, CancellationToken cancellationToken)
        {
            Batches.Add([.. owners]);
            Tokens.Add(cancellationToken);

            return Task.CompletedTask;
        }
    }

    private sealed class OtherConsumerReclaimer : IDeadOwnerReclaimer
    {
        public TimeSpan? ReconcileInterval => null;

        public Task ReclaimAsync(IReadOnlyCollection<string> owners, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class FakeMembership : INodeMembership
    {
        public IReadOnlyList<NodeLivenessSnapshot> Snapshot { get; set; } = [];

        public NodeIdentity? Identity => null;

        public string ClusterName => "orders";

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
        await reclaimer.Received(1).ReclaimAsync(_Batch(_DefaultClusterTag("node-a", 5)), Arg.Any<CancellationToken>());
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
        await reclaimer.Received(1).ReclaimAsync(_Batch(_DefaultClusterTag("node-b", 2)), Arg.Any<CancellationToken>());
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
        await reclaimer.Received(1).ReclaimAsync(_Batch(_DefaultClusterTag("node-b", 2)), Arg.Any<CancellationToken>());
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

    // The real MembershipService tags owners with its configured cluster, so expectations format them the same way.
    private static string _DefaultClusterTag(string node, long incarnation)
    {
        return NodeOwnerTag.Format(CoordinationOptions.DefaultClusterName, _Identity(node, incarnation));
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
