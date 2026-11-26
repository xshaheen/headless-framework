// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

public sealed class MembershipEventLogTests : TestBase
{
    private static readonly DateTimeOffset _Start = new(2026, 11, 26, 9, 0, 0, TimeSpan.Zero);

    private readonly List<IDisposable> _disposables = [];

    [Fact]
    public void should_record_published_events_oldest_first_without_a_subscriber()
    {
        // given
        var source = _CreateSource();
        var node = _Identity("node-a", 1);

        // when
        source.Publish(new NodeJoined(node));
        source.Publish(new NodeSuspected(node));
        source.Publish(new NodeLeft(node));

        // then
        var entries = source.GetEntries();
        entries.Select(static entry => entry.Sequence).Should().Equal(1, 2, 3);
        entries[0].Event.Should().BeOfType<NodeJoined>();
        entries[1].Event.Should().BeOfType<NodeSuspected>();
        entries[2].Event.Should().BeOfType<NodeLeft>();
    }

    [Fact]
    public void should_keep_the_last_256_entries_when_more_are_published()
    {
        // given
        var source = _CreateSource();
        var node = _Identity("node-a", 1);

        // when
        for (var i = 0; i < 300; i++)
        {
            source.Publish(new NodeJoined(node));
        }

        // then
        var entries = source.GetEntries();
        entries.Should().HaveCount(256);
        entries[0].Sequence.Should().Be(45);
        entries[^1].Sequence.Should().Be(300);
        entries.Select(static entry => entry.Sequence).Should().BeInAscendingOrder();
    }

    [Fact]
    public void should_stamp_observed_at_from_the_registered_clock()
    {
        // given
        var timeProvider = new FakeTimeProvider(_Start);
        var source = _CreateSource(timeProvider);
        var node = _Identity("node-a", 1);

        // when
        source.Publish(new NodeJoined(node));
        timeProvider.Advance(TimeSpan.FromSeconds(7));
        source.Publish(new NodeLeft(node));

        // then
        var entries = source.GetEntries();
        entries[0].ObservedAt.Should().Be(_Start);
        entries[1].ObservedAt.Should().Be(_Start.AddSeconds(7));
    }

    [Fact]
    public void should_return_a_copy_that_later_publishes_do_not_change()
    {
        // given
        var source = _CreateSource();
        var node = _Identity("node-a", 1);
        source.Publish(new NodeJoined(node));
        var before = source.GetEntries();

        // when
        source.Publish(new NodeLeft(node));

        // then
        before.Should().ContainSingle();
        source.GetEntries().Should().HaveCount(2);
    }

    [Fact]
    public async Task should_record_snapshot_state_for_a_first_observed_alive_node()
    {
        // given
        var node = _Identity("node-a", 1);
        var beat = _Start.AddSeconds(-3);
        var store = new FakeMembershipStore();
        store.EnqueueSnapshot(_Snapshot(node, NodeLivenessState.Alive, beat));
        var source = _CreateSource();
        var sut = _CreateHeartbeatService(store, source);

        // when
        await sut.RunOnceAsync(AbortToken);

        // then
        var entry = source.GetEntries().Should().ContainSingle().Subject;
        entry.Event.Should().BeOfType<NodeJoined>().Which.Identity.Should().Be(node);
        entry.State.Should().Be(NodeLivenessState.Alive);
        entry.LastHeartbeatAt.Should().Be(beat);
        entry.IsFirstObservation.Should().BeTrue();
    }

    [Fact]
    public async Task should_mark_every_node_seen_at_the_first_beat_as_a_first_observation()
    {
        // given: a process's first beat reports nodes that were already suspected or dead before it started
        var suspected = _Identity("node-a", 1);
        var dead = _Identity("node-b", 1);
        var store = new FakeMembershipStore();
        store.EnqueueSnapshot(
            _Snapshot(suspected, NodeLivenessState.Suspected),
            _Snapshot(dead, NodeLivenessState.Dead)
        );
        var source = _CreateSource();
        var sut = _CreateHeartbeatService(store, source);

        // when
        await sut.RunOnceAsync(AbortToken);

        // then
        var entries = source.GetEntries();
        entries.Should().HaveCount(2);
        entries.Should().OnlyContain(static entry => entry.IsFirstObservation);
        entries.Should().ContainSingle(entry => entry.Event is NodeSuspected && entry.Event.Identity == suspected);
        entries
            .Should()
            .ContainSingle(entry =>
                entry.Event is NodeLeft && entry.Event.Identity == dead && entry.State == NodeLivenessState.Dead
            );
    }

    [Fact]
    public async Task should_record_the_current_snapshot_state_for_a_transition()
    {
        // given
        var node = _Identity("node-a", 1);
        var suspectedBeat = _Start.AddSeconds(-20);
        var store = new FakeMembershipStore();
        store.EnqueueSnapshot(_Snapshot(node, NodeLivenessState.Alive, _Start.AddSeconds(-40)));
        store.EnqueueSnapshot(_Snapshot(node, NodeLivenessState.Suspected, suspectedBeat));
        var source = _CreateSource();
        var sut = _CreateHeartbeatService(store, source);

        // when
        await sut.RunOnceAsync(AbortToken);
        await sut.RunOnceAsync(AbortToken);

        // then
        var entry = source.GetEntries()[^1];
        entry.Event.Should().BeOfType<NodeSuspected>().Which.Identity.Should().Be(node);
        entry.State.Should().Be(NodeLivenessState.Suspected);
        entry.LastHeartbeatAt.Should().Be(suspectedBeat);
        entry.IsFirstObservation.Should().BeFalse();
    }

    [Fact]
    public async Task should_record_the_snapshot_state_when_a_suspected_node_recovers()
    {
        // given
        var node = _Identity("node-a", 1);
        var recoveredBeat = _Start.AddSeconds(-1);
        var store = new FakeMembershipStore();
        store.EnqueueSnapshot(_Snapshot(node, NodeLivenessState.Suspected, _Start.AddSeconds(-20)));
        store.EnqueueSnapshot(_Snapshot(node, NodeLivenessState.Alive, recoveredBeat));
        var source = _CreateSource();
        var sut = _CreateHeartbeatService(store, source);

        // when
        await sut.RunOnceAsync(AbortToken);
        await sut.RunOnceAsync(AbortToken);

        // then
        var entry = source.GetEntries()[^1];
        entry.Event.Should().BeOfType<NodeRecovered>().Which.Identity.Should().Be(node);
        entry.State.Should().Be(NodeLivenessState.Alive);
        entry.LastHeartbeatAt.Should().Be(recoveredBeat);
        entry.IsFirstObservation.Should().BeFalse();
    }

    [Fact]
    public async Task should_record_the_snapshot_state_when_a_known_node_turns_dead()
    {
        // given
        var node = _Identity("node-a", 1);
        var lastBeat = _Start.AddSeconds(-40);
        var store = new FakeMembershipStore();
        store.EnqueueSnapshot(_Snapshot(node, NodeLivenessState.Suspected, _Start.AddSeconds(-20)));
        store.EnqueueSnapshot(_Snapshot(node, NodeLivenessState.Dead, lastBeat));
        var source = _CreateSource();
        var sut = _CreateHeartbeatService(store, source);

        // when
        await sut.RunOnceAsync(AbortToken);
        await sut.RunOnceAsync(AbortToken);

        // then
        var entry = source.GetEntries()[^1];
        entry.Event.Should().BeOfType<NodeLeft>().Which.Identity.Should().Be(node);
        entry.State.Should().Be(NodeLivenessState.Dead);
        entry.LastHeartbeatAt.Should().Be(lastBeat);
        entry.IsFirstObservation.Should().BeFalse();
    }

    [Fact]
    public async Task should_mark_an_incarnation_that_appears_after_the_first_beat_as_a_first_observation()
    {
        // given: a node restarts with a new incarnation between two beats
        var first = _Identity("node-a", 1);
        var restarted = _Identity("node-a", 2);
        var store = new FakeMembershipStore();
        store.EnqueueSnapshot(_Snapshot(first, NodeLivenessState.Alive));
        store.EnqueueSnapshot(_Snapshot(restarted, NodeLivenessState.Alive));
        var source = _CreateSource();
        var sut = _CreateHeartbeatService(store, source);

        // when
        await sut.RunOnceAsync(AbortToken);
        await sut.RunOnceAsync(AbortToken);

        // then
        source
            .GetEntries()
            .Should()
            .ContainSingle(entry => entry.Event is NodeJoined && entry.Event.Identity == restarted)
            .Which.IsFirstObservation.Should()
            .BeTrue();
    }

    [Fact]
    public async Task should_not_mark_a_rejoin_after_death_as_a_first_observation()
    {
        // given
        var node = _Identity("node-a", 1);
        var store = new FakeMembershipStore();
        store.EnqueueSnapshot(_Snapshot(node, NodeLivenessState.Dead));
        store.EnqueueSnapshot(_Snapshot(node, NodeLivenessState.Alive));
        var source = _CreateSource();
        var sut = _CreateHeartbeatService(store, source);

        // when
        await sut.RunOnceAsync(AbortToken);
        await sut.RunOnceAsync(AbortToken);

        // then
        var entry = source.GetEntries()[^1];
        entry.Event.Should().BeOfType<NodeJoined>();
        entry.State.Should().Be(NodeLivenessState.Alive);
        entry.IsFirstObservation.Should().BeFalse();
    }

    [Fact]
    public async Task should_record_null_snapshot_fields_for_a_node_missing_from_the_next_snapshot()
    {
        // given
        var node = _Identity("node-a", 1);
        var store = new FakeMembershipStore();
        store.EnqueueSnapshot(_Snapshot(node, NodeLivenessState.Alive));
        store.EnqueueSnapshot();
        var source = _CreateSource();
        var sut = _CreateHeartbeatService(store, source);

        // when
        await sut.RunOnceAsync(AbortToken);
        await sut.RunOnceAsync(AbortToken);

        // then
        var entry = source.GetEntries()[^1];
        entry.Event.Should().BeOfType<NodeLeft>().Which.Identity.Should().Be(node);
        entry.State.Should().BeNull();
        entry.LastHeartbeatAt.Should().BeNull();
        entry.IsFirstObservation.Should().BeFalse();
    }

    [Fact]
    public async Task should_record_local_membership_lost_with_null_snapshot_fields()
    {
        // given: a host without the heartbeat service registers and beats by hand, and the store rejects the beat
        var store = new FakeMembershipStore();
        var source = _CreateSource();
        var membership = _CreateMembership(store, source);
        var identity = await membership.RegisterAsync(AbortToken);
        store.HeartbeatAccepted = false;

        // when
        var accepted = await membership.HeartbeatAsync(AbortToken);

        // then
        accepted.Should().BeFalse();
        var entry = source.GetEntries().Should().ContainSingle().Subject;
        entry.Event.Should().BeOfType<LocalMembershipLost>().Which.Identity.Should().Be(identity);
        entry.State.Should().BeNull();
        entry.LastHeartbeatAt.Should().BeNull();
        entry.IsFirstObservation.Should().BeFalse();
    }

    [Fact]
    public async Task should_assign_unique_gap_free_sequences_to_concurrent_publishes()
    {
        // given
        var source = _CreateSource();
        var node = _Identity("node-a", 1);
        using var start = new ManualResetEventSlim();

        void publishMany()
        {
            start.Wait(AbortToken);

            for (var i = 0; i < 100; i++)
            {
                source.Publish(new NodeJoined(node));
            }
        }

        var first = Task.Run(publishMany, AbortToken);
        var second = Task.Run(publishMany, AbortToken);

        // when
        start.Set();
        await Task.WhenAll(first, second);

        // then
        long[] sequences = [.. source.GetEntries().Select(static entry => entry.Sequence)];
        long[] expected = [.. Enumerable.Range(1, 200).Select(static i => (long)i)];
        sequences.Should().Equal(expected);
    }

    [Fact]
    public async Task should_keep_recording_when_a_subscriber_channel_is_full()
    {
        // given: a one-slot subscriber that never reads
        var source = new MembershipEventSource(
            NullLogger<MembershipEventSource>.Instance,
            new FakeTimeProvider(_Start),
            capacity: 1
        );
        await using var stalled = source.WatchAsync(AbortToken).GetAsyncEnumerator(AbortToken);
        var node = _Identity("node-a", 1);

        // when
        source.Publish(new NodeJoined(node));
        source.Publish(new NodeSuspected(node));
        source.Publish(new NodeLeft(node));

        // then
        source.GetEntries().Should().HaveCount(3);
    }

    [Fact]
    public void should_resolve_an_empty_log_on_a_host_without_the_heartbeat()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessCoordination(setup =>
        {
            setup.RegisterExtension(new LogTestProviderExtension());
            setup.DisableMembershipHeartbeat();
        });
        using var provider = services.BuildServiceProvider();

        // when
        var log = provider.GetRequiredService<IMembershipEventLog>();

        // then
        log.GetEntries().Should().BeEmpty();
        log.Should().BeSameAs(provider.GetRequiredService<IMembershipEventSource>());
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        foreach (var disposable in _disposables)
        {
            disposable.Dispose();
        }

        await base.DisposeAsyncCore().ConfigureAwait(false);
    }

    private static MembershipEventSource _CreateSource(FakeTimeProvider? timeProvider = null)
    {
        return new MembershipEventSource(
            NullLogger<MembershipEventSource>.Instance,
            timeProvider ?? new FakeTimeProvider(_Start)
        );
    }

    private MembershipService _CreateMembership(FakeMembershipStore store, MembershipEventSource source)
    {
        var membership = new MembershipService(
            store,
            new StaticNodeIdProvider(new NodeId("local")),
            new CoordinationOptions { MembershipLostBehavior = MembershipLostBehavior.StopMembershipOnly },
            source,
            new FakeTimeProvider(_Start),
            new FakeHostApplicationLifetime(),
            NullLogger<MembershipService>.Instance
        );
        _disposables.Add(membership);

        return membership;
    }

    private MembershipHeartbeatBackgroundService _CreateHeartbeatService(
        FakeMembershipStore store,
        MembershipEventSource source
    )
    {
        var heartbeat = new MembershipHeartbeatBackgroundService(
            _CreateMembership(store, source),
            source,
            new CoordinationOptions(),
            new FakeTimeProvider(_Start),
            new FakeTimeProvider(_Start),
            [],
            NullLogger<MembershipHeartbeatBackgroundService>.Instance
        );
        _disposables.Add(heartbeat);

        return heartbeat;
    }

    private static NodeIdentity _Identity(string nodeId, long incarnation)
    {
        return new NodeIdentity(new NodeId(nodeId), new NodeIncarnation(incarnation));
    }

    private static NodeLivenessSnapshot _Snapshot(
        NodeIdentity identity,
        NodeLivenessState state,
        DateTimeOffset? lastHeartbeatAt = null
    )
    {
        return new NodeLivenessSnapshot(
            identity,
            state,
            null,
            new Dictionary<string, string>(StringComparer.Ordinal),
            lastHeartbeatAt ?? DateTimeOffset.UnixEpoch
        );
    }

    private sealed class StaticNodeIdProvider(NodeId nodeId) : INodeIdProvider
    {
        public ValueTask<NodeId> GetNodeIdAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return ValueTask.FromResult(nodeId);
        }
    }

    private sealed class LogTestProviderExtension : ICoordinationProviderOptionsExtension
    {
        public string ProviderName => "Fake";

        public void AddServices(IServiceCollection services)
        {
            services.AddCoordinationCore<FakeMembershipStore>();
        }
    }
}
