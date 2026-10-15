// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;
using Headless.Testing.Tests;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

public sealed class MembershipServiceTests : TestBase
{
    [Fact]
    public async Task should_register_by_allocating_incarnation_and_writing_descriptor_without_heartbeating()
    {
        // given
        var store = new FakeMembershipStore { NextIncarnation = new NodeIncarnation(3) };
        using var sut = _CreateService(store, nodeId: "node-a");

        // when
        var identity = await sut.RegisterAsync(AbortToken);

        // then
        identity.Should().Be(new NodeIdentity(new NodeId("node-a"), new NodeIncarnation(3)));
        sut.Identity.Should().Be(identity);
        // UpsertDescriptorAsync now durably establishes both descriptor and liveness; register no longer beats.
        store.Descriptors.Should().ContainSingle().Which.Identity.Should().Be(identity);
        store.Heartbeats.Should().BeEmpty();
    }

    [Fact]
    public async Task should_return_only_alive_nodes_in_deterministic_order()
    {
        // given
        var store = new FakeMembershipStore();
        var nodeB = new NodeIdentity(new NodeId("node-b"), new NodeIncarnation(1));
        var nodeA = new NodeIdentity(new NodeId("node-a"), new NodeIncarnation(2));
        store.EnqueueSnapshot(
            new NodeLivenessSnapshot(
                nodeB,
                NodeLivenessState.Alive,
                null,
                new Dictionary<string, string>(StringComparer.Ordinal)
            ),
            new NodeLivenessSnapshot(
                nodeA,
                NodeLivenessState.Alive,
                null,
                new Dictionary<string, string>(StringComparer.Ordinal)
            ),
            new NodeLivenessSnapshot(
                new NodeIdentity(new NodeId("node-c"), new NodeIncarnation(1)),
                NodeLivenessState.Suspected,
                null,
                new Dictionary<string, string>(StringComparer.Ordinal)
            )
        );

        using var sut = _CreateService(store);

        // when
        var liveNodes = await sut.GetLiveNodesAsync(AbortToken);

        // then
        liveNodes.Should().Equal(nodeA, nodeB);
        store.ReadLiveNodesCalls.Should().Be(1);
        store.ReadLivenessCalls.Should().Be(0);
    }

    [Fact]
    public async Task should_fire_local_loss_token_and_stop_application_when_self_heartbeat_is_rejected()
    {
        // given
        var store = new FakeMembershipStore();
        var lifetime = new FakeHostApplicationLifetime();
        using var sut = _CreateService(store, lifetime: lifetime);
        var identity = await sut.RegisterAsync(AbortToken);
        store.HeartbeatAccepted = false;
        using var watcherCts = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        watcherCts.CancelAfter(TimeSpan.FromSeconds(2));
        await using var watcher = sut.WatchAsync(watcherCts.Token).GetAsyncEnumerator(watcherCts.Token);

        // when
        var accepted = await sut.HeartbeatAsync(AbortToken);
        var hasEvent = await watcher.MoveNextAsync();

        // then
        accepted.Should().BeFalse();
        sut.LocalMembershipLostToken.IsCancellationRequested.Should().BeTrue();
        sut.Identity.Should().BeNull();
        hasEvent.Should().BeTrue();
        watcher.Current.Should().BeOfType<LocalMembershipLost>().Which.Identity.Should().Be(identity);
        lifetime.StopApplicationCalled.Should().BeTrue();
    }

    [Fact]
    public async Task should_fire_local_loss_token_without_stopping_host_when_configured()
    {
        // given
        var options = new CoordinationOptions { MembershipLostBehavior = MembershipLostBehavior.StopMembershipOnly };
        var store = new FakeMembershipStore();
        var lifetime = new FakeHostApplicationLifetime();
        using var sut = _CreateService(store, options, lifetime);
        await sut.RegisterAsync(AbortToken);
        store.HeartbeatAccepted = false;

        // when
        var accepted = await sut.HeartbeatAsync(AbortToken);

        // then
        accepted.Should().BeFalse();
        sut.LocalMembershipLostToken.IsCancellationRequested.Should().BeTrue();
        lifetime.StopApplicationCalled.Should().BeFalse();
    }

    [Fact]
    public async Task should_return_false_and_not_touch_store_when_heartbeat_called_before_register()
    {
        // given
        var store = new FakeMembershipStore();
        using var sut = _CreateService(store);

        // when
        var accepted = await sut.HeartbeatAsync(AbortToken);

        // then
        accepted.Should().BeFalse();
        sut.Identity.Should().BeNull();
        store.Heartbeats.Should().BeEmpty();
    }

    [Fact]
    public async Task should_return_false_and_not_touch_store_when_heartbeat_called_after_leave()
    {
        // given
        var store = new FakeMembershipStore();
        using var sut = _CreateService(store);
        await sut.RegisterAsync(AbortToken);
        await sut.LeaveAsync(AbortToken);
        store.Heartbeats.Clear();

        // when
        var accepted = await sut.HeartbeatAsync(AbortToken);

        // then
        accepted.Should().BeFalse();
        sut.Identity.Should().BeNull();
        store.Heartbeats.Should().BeEmpty();
    }

    [Theory]
    [InlineData(NodeLivenessState.Alive, true)]
    [InlineData(NodeLivenessState.Suspected, false)]
    [InlineData(NodeLivenessState.Dead, false)]
    public async Task should_map_targeted_node_liveness_state_to_is_alive(NodeLivenessState state, bool expected)
    {
        // given
        var store = new FakeMembershipStore();
        var identity = new NodeIdentity(new NodeId("node-a"), new NodeIncarnation(1));
        store.NodeStates[identity] = state;

        using var sut = _CreateService(store);

        // when
        var alive = await sut.IsAliveAsync(identity, AbortToken);

        // then
        alive.Should().Be(expected);
    }

    [Fact]
    public async Task should_treat_absent_targeted_node_as_not_alive()
    {
        // given
        var store = new FakeMembershipStore();
        var identity = new NodeIdentity(new NodeId("node-a"), new NodeIncarnation(1));
        using var sut = _CreateService(store);

        // when — identity was never configured in the store, so it resolves to absent (null)
        var alive = await sut.IsAliveAsync(identity, AbortToken);

        // then
        alive.Should().BeFalse();
    }

    [Fact]
    public async Task should_use_targeted_read_and_not_full_snapshot_for_is_alive()
    {
        // given
        var store = new FakeMembershipStore();
        var identity = new NodeIdentity(new NodeId("node-a"), new NodeIncarnation(1));
        store.NodeStates[identity] = NodeLivenessState.Alive;
        using var sut = _CreateService(store);

        // when
        await sut.IsAliveAsync(identity, AbortToken);

        // then — the targeted SPI method is used; the full cluster snapshot read is not
        store.ReadNodeLivenessCalls.Should().Be(1);
        store.ReadLivenessCalls.Should().Be(0);
    }

    [Fact]
    public async Task should_throw_when_is_alive_called_with_cancelled_token()
    {
        // given
        var store = new FakeMembershipStore();
        var identity = new NodeIdentity(new NodeId("node-a"), new NodeIncarnation(1));
        using var sut = _CreateService(store);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // when
        var act = async () => await sut.IsAliveAsync(identity, cts.Token);

        // then
        await act.Should().ThrowAsync<OperationCanceledException>();
        store.ReadNodeLivenessCalls.Should().Be(0);
    }

    [Fact]
    public async Task should_fence_at_dead_threshold_minus_margin_measured_from_registration()
    {
        // given: the fence is anchored at the registration write, not at a later heartbeat-loop tick.
        var timeProvider = new FakeTimeProvider();
        var options = _FenceOptions();
        var store = new FakeMembershipStore();
        using var sut = _CreateService(store, options, timeProvider: timeProvider);
        sut.EnableSelfFence();
        await sut.RegisterAsync(AbortToken);

        // when: no beat at all, one tick short of the fence (DeadThreshold 3s - margin 1s)
        timeProvider.Advance(TimeSpan.FromSeconds(2) - TimeSpan.FromTicks(1));

        // then
        sut.Identity.Should().NotBeNull();
        sut.LocalMembershipLostToken.IsCancellationRequested.Should().BeFalse();

        // when: the deadline passes
        timeProvider.Advance(TimeSpan.FromTicks(1));

        // then: the timer fail-stopped the node with no heartbeat or loop involved
        sut.Identity.Should().BeNull();
        sut.LocalMembershipLostToken.IsCancellationRequested.Should().BeTrue();
    }

    [Theory]
    // A sixth of DeadThreshold under 30s: 1.2s fences at 1s, 12s at 10s.
    [InlineData(200, 1200, 1000)]
    [InlineData(2000, 12_000, 10_000)]
    // The default 30s threshold and anything longer cap the margin at 5s.
    [InlineData(5000, 30_000, 25_000)]
    [InlineData(10_000, 60_000, 55_000)]
    public async Task should_derive_the_margin_from_dead_threshold_when_none_is_configured(
        int heartbeatMilliseconds,
        int deadMilliseconds,
        int fenceMilliseconds
    )
    {
        // given: no SelfFenceMargin, so a short threshold no longer needs a hand-picked margin to start
        var timeProvider = new FakeTimeProvider();
        var options = new CoordinationOptions
        {
            HeartbeatInterval = TimeSpan.FromMilliseconds(heartbeatMilliseconds),
            SuspicionThreshold = TimeSpan.FromMilliseconds(heartbeatMilliseconds * 2),
            DeadThreshold = TimeSpan.FromMilliseconds(deadMilliseconds),
            MembershipLostBehavior = MembershipLostBehavior.StopMembershipOnly,
        };
        var store = new FakeMembershipStore();
        using var sut = _CreateService(store, options, timeProvider: timeProvider);
        sut.EnableSelfFence();
        await sut.RegisterAsync(AbortToken);

        // when: one tick short of the derived fence
        timeProvider.Advance(TimeSpan.FromMilliseconds(fenceMilliseconds) - TimeSpan.FromTicks(1));

        // then
        sut.Identity.Should().NotBeNull();

        // when: the derived deadline passes
        timeProvider.Advance(TimeSpan.FromTicks(1));

        // then
        sut.Identity.Should().BeNull();
        sut.LocalMembershipLostToken.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public async Task should_fence_a_registration_made_before_the_fence_was_enabled_from_its_original_anchor()
    {
        // given: the Jobs startup gate can register before the heartbeat service attaches and enables the fence
        var timeProvider = new FakeTimeProvider();
        var options = _FenceOptions();
        var store = new FakeMembershipStore();
        using var sut = _CreateService(store, options, timeProvider: timeProvider);
        await sut.RegisterAsync(AbortToken);
        timeProvider.Advance(TimeSpan.FromSeconds(1));

        // when: the fence is enabled a second after registration, then the rest of the budget passes
        sut.EnableSelfFence();
        timeProvider.Advance(TimeSpan.FromSeconds(1) - TimeSpan.FromTicks(1));

        // then: still a member one tick before the deadline measured from the registration
        sut.Identity.Should().NotBeNull();

        timeProvider.Advance(TimeSpan.FromTicks(1));

        // then: the timer armed on enable fences at the original deadline, not a fresh one
        sut.Identity.Should().BeNull();
        sut.LocalMembershipLostToken.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public async Task should_write_the_departure_when_leaving_past_the_deadline_before_the_timer_fires()
    {
        // given: the deadline has passed but the fence timer is late, so Identity already reads null
        var timeProvider = new LateTimerTimeProvider();
        var store = new FakeMembershipStore();
        using var sut = _CreateService(store, _FenceOptions(), timeProvider: timeProvider);
        sut.EnableSelfFence();
        var identity = await sut.RegisterAsync(AbortToken);
        timeProvider.Advance(TimeSpan.FromSeconds(2));
        sut.Identity.Should().BeNull();

        // when
        await sut.LeaveAsync(AbortToken);

        // then: peers still get the explicit departure instead of waiting out DeadThreshold
        store.Leaves.Should().ContainSingle().Which.Should().Be(identity);
        sut.TryGetFence(out _, out _).Should().BeFalse();
    }

    [Fact]
    public async Task should_fire_the_fence_timer_at_the_margin_when_every_beat_fails_fast()
    {
        // given
        var timeProvider = new FakeTimeProvider();
        var store = new FakeMembershipStore { ThrowOnHeartbeat = true };
        using var sut = _CreateService(store, _FenceOptions(), timeProvider: timeProvider);
        sut.EnableSelfFence();
        await sut.RegisterAsync(AbortToken);

        // when: beats keep failing inside the budget
        timeProvider.Advance(TimeSpan.FromSeconds(1));
        var failedBeat = async () => await sut.HeartbeatAsync(AbortToken);
        await failedBeat.Should().ThrowAsync<InvalidOperationException>();
        timeProvider.Advance(TimeSpan.FromSeconds(0.5));

        // then: still a member 1.5s in
        sut.LocalMembershipLostToken.IsCancellationRequested.Should().BeFalse();

        // when: 2s after registration (DeadThreshold 3s - margin 1s)
        timeProvider.Advance(TimeSpan.FromSeconds(0.5));

        // then
        sut.LocalMembershipLostToken.IsCancellationRequested.Should().BeTrue();
        sut.Identity.Should().BeNull();
    }

    [Fact]
    public async Task should_extend_the_fence_from_the_start_of_each_accepted_beat()
    {
        // given
        var timeProvider = new FakeTimeProvider();
        using var sut = _CreateService(new FakeMembershipStore(), _FenceOptions(), timeProvider: timeProvider);
        sut.EnableSelfFence();
        await sut.RegisterAsync(AbortToken);

        // when: a beat accepted 1.5s in moves the deadline to 3.5s
        timeProvider.Advance(TimeSpan.FromSeconds(1.5));
        (await sut.HeartbeatAsync(AbortToken)).Should().BeTrue();
        timeProvider.Advance(TimeSpan.FromSeconds(1.9));

        // then
        sut.Identity.Should().NotBeNull();

        timeProvider.Advance(TimeSpan.FromSeconds(0.1));
        sut.Identity.Should().BeNull();
    }

    [Fact]
    public async Task should_read_identity_as_null_past_the_deadline_even_when_the_timer_is_late()
    {
        // given: a clock whose timers never fire, standing in for a late timer callback
        var timeProvider = new LateTimerTimeProvider();
        using var sut = _CreateService(new FakeMembershipStore(), _FenceOptions(), timeProvider: timeProvider);
        sut.EnableSelfFence();
        await sut.RegisterAsync(AbortToken);

        // when
        timeProvider.Advance(TimeSpan.FromSeconds(2));

        // then: stamp sites see no identity although nothing has signalled the loss yet
        sut.Identity.Should().BeNull();
        sut.LocalMembershipLostToken.IsCancellationRequested.Should().BeFalse();

        // and the next beat attempt fail-stops instead of renewing
        (await sut.HeartbeatAsync(AbortToken))
            .Should()
            .BeFalse();
        sut.LocalMembershipLostToken.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public async Task should_not_fence_a_host_that_never_enables_the_self_fence()
    {
        // given: no heartbeat service (DisableMembershipHeartbeat), so no beat source and no fence
        var timeProvider = new FakeTimeProvider();
        using var sut = _CreateService(new FakeMembershipStore(), _FenceOptions(), timeProvider: timeProvider);
        var identity = await sut.RegisterAsync(AbortToken);

        // when
        timeProvider.Advance(TimeSpan.FromSeconds(30));

        // then
        sut.Identity.Should().Be(identity);
        sut.LocalMembershipLostToken.IsCancellationRequested.Should().BeFalse();
    }

    [Fact]
    public async Task should_keep_identity_null_when_a_beat_is_accepted_after_the_fence_fired()
    {
        // given
        var timeProvider = new FakeTimeProvider();
        var store = new FakeMembershipStore { BlockOnHeartbeat = true };
        var source = new MembershipEventSource(NullLogger<MembershipEventSource>.Instance);
        using var sut = _CreateService(store, _FenceOptions(), timeProvider: timeProvider, source: source);
        sut.EnableSelfFence();
        await sut.RegisterAsync(AbortToken);
        using var watcherCts = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        watcherCts.CancelAfter(TimeSpan.FromSeconds(2));
        var events = new List<NodeMembershipEvent>();
        var watching = _CollectAsync(source, events, watcherCts.Token);

        // when: a beat is in flight while the fence fires, then the store accepts it late
        var beat = sut.HeartbeatAsync(AbortToken).AsTask();
        timeProvider.Advance(TimeSpan.FromSeconds(2));
        store.HeartbeatRelease.TrySetResult();
        var accepted = await beat.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
        await watching;

        // then
        accepted.Should().BeFalse();
        sut.Identity.Should().BeNull();
        events.OfType<LocalMembershipLost>().Should().ContainSingle();
    }

    [Fact]
    public async Task should_register_afresh_after_a_refused_registration()
    {
        // given: the first attempt is refused, so its cached task is faulted
        var store = new FakeMembershipStore { UpsertAccepted = false };
        using var sut = _CreateService(store);
        var refused = async () => await sut.RegisterAsync(AbortToken);
        await refused.Should().ThrowAsync<MembershipRegistrationRejectedException>();
        store.UpsertAccepted = true;
        store.NextIncarnation = new NodeIncarnation(2);

        // when
        var identity = await sut.RegisterAsync(AbortToken);

        // then: a new allocation, not a replay of the faulted attempt
        store.AllocateIncarnationCalls.Should().Be(2);
        identity.Incarnation.Should().Be(new NodeIncarnation(2));
        sut.Identity.Should().Be(identity);
    }

    [Fact]
    public async Task should_register_afresh_after_leaving()
    {
        // given
        var store = new FakeMembershipStore();
        using var sut = _CreateService(store);
        var first = await sut.RegisterAsync(AbortToken);
        await sut.LeaveAsync(AbortToken);
        store.NextIncarnation = new NodeIncarnation(2);

        // when
        var second = await sut.RegisterAsync(AbortToken);

        // then: the completed registration is not reused once its lease is gone
        store.AllocateIncarnationCalls.Should().Be(2);
        second.Should().NotBe(first);
        sut.Identity.Should().Be(second);
    }

    [Fact]
    public async Task should_not_rearm_the_fence_after_dispose()
    {
        // given: a beat still in flight when the host disposes the service
        var timeProvider = new FakeTimeProvider();
        var options = _FenceOptions();
        options.MembershipLostBehavior = MembershipLostBehavior.StopApplication;
        var store = new FakeMembershipStore();
        var lifetime = new FakeHostApplicationLifetime();
        var sut = _CreateService(store, options, lifetime, timeProvider: timeProvider);
        sut.EnableSelfFence();
        await sut.RegisterAsync(AbortToken);
        store.BlockOnHeartbeat = true;
        var beat = sut.HeartbeatAsync(AbortToken).AsTask();
        sut.Dispose();

        // when: the beat lands after dispose and the old deadline passes
        store.HeartbeatRelease.TrySetResult();
        await beat.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
        timeProvider.Advance(TimeSpan.FromSeconds(10));

        // then: no timer came back to stop the application after shutdown
        lifetime.StopApplicationCalled.Should().BeFalse();
    }

    [Fact]
    public async Task should_complete_a_shared_registration_when_the_caller_that_started_it_cancels()
    {
        // given: two callers share one registration whose allocation is still in flight
        var store = new FakeMembershipStore { BlockOnAllocate = true };
        using var sut = _CreateService(store);
        using var firstCallerCts = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        var first = sut.RegisterAsync(firstCallerCts.Token).AsTask();
        var second = sut.RegisterAsync(AbortToken).AsTask();

        // when: the caller that started the attempt gives up before it completes
        await firstCallerCts.CancelAsync();
        store.AllocateRelease.TrySetResult();

        // then: only that caller's wait is cancelled; the attempt still registers for everyone else
        await FluentActions.Awaiting(() => first).Should().ThrowAsync<OperationCanceledException>();
        var identity = await second.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
        sut.Identity.Should().Be(identity);
        store.AllocateIncarnationCalls.Should().Be(1);
    }

    [Fact]
    public async Task should_allocate_one_incarnation_for_concurrent_registrations()
    {
        // given
        var store = new FakeMembershipStore { BlockOnAllocate = true };
        using var sut = _CreateService(store);

        // when
        var first = sut.RegisterAsync(AbortToken).AsTask();
        var second = sut.RegisterAsync(AbortToken).AsTask();
        store.AllocateRelease.TrySetResult();
        var identities = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

        // then
        store.AllocateIncarnationCalls.Should().Be(1);
        identities.Should().HaveElementAt(1, identities[0]);
        store.Descriptors.Should().ContainSingle();
    }

    [Fact]
    public async Task should_throw_and_leave_identity_unset_when_the_store_refuses_registration()
    {
        // given
        var store = new FakeMembershipStore { UpsertAccepted = false, NextIncarnation = new NodeIncarnation(4) };
        using var sut = _CreateService(store, nodeId: "duplicate");

        // when
        var act = async () => await sut.RegisterAsync(AbortToken);

        // then
        var thrown = await act.Should().ThrowAsync<MembershipRegistrationRejectedException>();
        thrown.Which.Identity.Should().Be(new NodeIdentity(new NodeId("duplicate"), new NodeIncarnation(4)));
        thrown.Which.Message.Should().Contain("'duplicate'");
        sut.Identity.Should().BeNull();
    }

    [Fact]
    public async Task should_refuse_to_register_again_after_local_membership_is_lost()
    {
        // given
        var store = new FakeMembershipStore();
        var options = new CoordinationOptions { MembershipLostBehavior = MembershipLostBehavior.StopMembershipOnly };
        using var sut = _CreateService(store, options);
        await sut.RegisterAsync(AbortToken);
        store.HeartbeatAccepted = false;
        await sut.HeartbeatAsync(AbortToken);

        // when
        var act = async () => await sut.RegisterAsync(AbortToken);

        // then
        await act.Should().ThrowAsync<InvalidOperationException>();
        store.AllocateIncarnationCalls.Should().Be(1);
        sut.Identity.Should().BeNull();
    }

    // DeadThreshold 3s with a 1s margin: the fence budget is 2s.
    private static CoordinationOptions _FenceOptions()
    {
        return new CoordinationOptions
        {
            HeartbeatInterval = TimeSpan.FromSeconds(0.5),
            SuspicionThreshold = TimeSpan.FromSeconds(1.5),
            DeadThreshold = TimeSpan.FromSeconds(3),
            SelfFenceMargin = TimeSpan.FromSeconds(1),
            MembershipLostBehavior = MembershipLostBehavior.StopMembershipOnly,
        };
    }

    private static async Task _CollectAsync(
        MembershipEventSource source,
        List<NodeMembershipEvent> events,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await foreach (var membershipEvent in source.WatchAsync(cancellationToken))
            {
                events.Add(membershipEvent);
            }
        }
        catch (OperationCanceledException) { }
    }

    private static MembershipService _CreateService(
        FakeMembershipStore store,
        CoordinationOptions? options = null,
        IHostApplicationLifetime? lifetime = null,
        string nodeId = "node-a",
        TimeProvider? timeProvider = null,
        MembershipEventSource? source = null
    )
    {
        return new MembershipService(
            store,
            new StaticNodeIdProvider(new NodeId(nodeId)),
            options ?? new CoordinationOptions(),
            source ?? new MembershipEventSource(NullLogger<MembershipEventSource>.Instance),
            timeProvider ?? new FakeTimeProvider(),
            lifetime,
            NullLogger<MembershipService>.Instance
        );
    }

    private sealed class LateTimerTimeProvider : FakeTimeProvider
    {
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            return new NeverFiringTimer();
        }

        private sealed class NeverFiringTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                return true;
            }

            public void Dispose() { }

            public ValueTask DisposeAsync()
            {
                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class StaticNodeIdProvider(NodeId nodeId) : INodeIdProvider
    {
        public ValueTask<NodeId> GetNodeIdAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return ValueTask.FromResult(nodeId);
        }
    }
}
