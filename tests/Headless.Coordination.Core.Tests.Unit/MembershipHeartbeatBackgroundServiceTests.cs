// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

public sealed class MembershipHeartbeatBackgroundServiceTests : TestBase
{
    [Fact]
    public async Task should_fault_after_five_registration_attempts_by_default()
    {
        // given
        var store = new FakeMembershipStore { ThrowOnRegister = true };
        var (sut, timeProvider, _) = _CreateSut(store);

        // when
        await sut.StartAsync(AbortToken);
        await _AdvanceUntilAttemptsAsync(sut, store, timeProvider, expectedAttempts: 5);

        // then
        store.AllocateIncarnationCalls.Should().Be(5);
        var act = () => sut.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("registration unavailable");
    }

    [Fact]
    public async Task should_stop_membership_loop_without_faulting_host_when_registration_fails_and_configured()
    {
        // given
        var store = new FakeMembershipStore { ThrowOnRegister = true };
        var options = new CoordinationOptions { MembershipLostBehavior = MembershipLostBehavior.StopMembershipOnly };
        var (sut, timeProvider, membership) = _CreateSut(store, options);

        // when
        await sut.StartAsync(AbortToken);
        await _AdvanceUntilAttemptsAsync(sut, store, timeProvider, expectedAttempts: 5);
        await sut.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

        // then
        store.AllocateIncarnationCalls.Should().Be(5);
        membership.Identity.Should().BeNull();
    }

    [Fact]
    public async Task should_not_read_liveness_when_local_heartbeat_is_rejected()
    {
        // given
        var store = new FakeMembershipStore { HeartbeatAccepted = false };
        var (sut, _, membership) = _CreateSut(store);
        await membership.RegisterAsync(AbortToken);
        var remote = new NodeIdentity(new NodeId("remote"), new NodeIncarnation(1));
        store.EnqueueSnapshot(
            new NodeLivenessSnapshot(
                remote,
                NodeLivenessState.Alive,
                null,
                new Dictionary<string, string>(StringComparer.Ordinal)
            )
        );

        // when
        await sut.RunOnceAsync(AbortToken);

        // then
        store.Heartbeats.Should().ContainSingle();
        membership.Identity.Should().BeNull();
        store.ReadLivenessCalls.Should().Be(0);
    }

    [Fact]
    public async Task should_self_fence_after_heartbeat_failures_reach_the_dead_threshold()
    {
        var options = new CoordinationOptions
        {
            HeartbeatInterval = TimeSpan.FromSeconds(1),
            SuspicionThreshold = TimeSpan.FromSeconds(2),
            DeadThreshold = TimeSpan.FromSeconds(3),
        };
        var store = new FakeMembershipStore { ThrowOnHeartbeat = true };
        var (sut, timeProvider, membership) = _CreateSut(store, options);
        await membership.RegisterAsync(AbortToken);

        await sut.RunOnceAsync(AbortToken);
        membership.Identity.Should().NotBeNull();

        timeProvider.Advance(options.DeadThreshold);
        await sut.RunOnceAsync(AbortToken);

        membership.Identity.Should().BeNull();
        membership.LocalMembershipLostToken.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public async Task should_self_fence_when_heartbeat_call_hangs_past_the_dead_threshold()
    {
        var options = new CoordinationOptions
        {
            HeartbeatInterval = TimeSpan.FromSeconds(1),
            SuspicionThreshold = TimeSpan.FromSeconds(2),
            DeadThreshold = TimeSpan.FromSeconds(3),
        };
        var store = new FakeMembershipStore { BlockOnHeartbeat = true };
        var (sut, timeProvider, membership) = _CreateSut(store, options);
        await membership.RegisterAsync(AbortToken);

        var heartbeat = sut.RunOnceAsync(AbortToken);
        timeProvider.Advance(options.DeadThreshold);
        await heartbeat.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
        store.HeartbeatRelease.TrySetResult();

        membership.Identity.Should().BeNull();
        membership.LocalMembershipLostToken.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public async Task should_not_self_fence_for_liveness_read_failures_while_heartbeats_succeed()
    {
        var options = new CoordinationOptions
        {
            HeartbeatInterval = TimeSpan.FromSeconds(1),
            SuspicionThreshold = TimeSpan.FromSeconds(2),
            DeadThreshold = TimeSpan.FromSeconds(3),
        };
        var store = new FakeMembershipStore { ThrowOnRead = true };
        var (sut, timeProvider, membership) = _CreateSut(store, options);
        await membership.RegisterAsync(AbortToken);

        timeProvider.Advance(options.DeadThreshold + options.HeartbeatInterval);
        await sut.RunOnceAsync(AbortToken);

        membership.Identity.Should().NotBeNull();
        membership.LocalMembershipLostToken.IsCancellationRequested.Should().BeFalse();
    }

    [Fact]
    public async Task should_swallow_leave_failure_during_stop()
    {
        // given
        var store = new FakeMembershipStore { ThrowOnLeave = true };
        var (sut, _, membership) = _CreateSut(store);
        await membership.RegisterAsync(AbortToken);

        // when
        var act = () => sut.StopAsync(AbortToken);

        // then
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task should_bound_graceful_leave_during_stop()
    {
        // given
        var store = new FakeMembershipStore { BlockOnLeave = true };
        var (sut, timeProvider, membership) = _CreateSut(store);
        await membership.RegisterAsync(AbortToken);

        // when
        var stopTask = sut.StopAsync(AbortToken);

        for (var i = 0; i < 6 && !stopTask.IsCompleted; i++)
        {
            timeProvider.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(10, AbortToken);
        }

        // then
        await stopTask.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
        store.Leaves.Should().BeEmpty();
    }

    private static (
        MembershipHeartbeatBackgroundService Sut,
        FakeTimeProvider TimeProvider,
        MembershipService Membership
    ) _CreateSut(FakeMembershipStore store, CoordinationOptions? options = null)
    {
        var coordinationOptions = options ?? new CoordinationOptions();
        var source = new MembershipEventSource(NullLogger<MembershipEventSource>.Instance);
        var timeProvider = new FakeTimeProvider();
        var membership = new MembershipService(
            store,
            new StaticNodeIdProvider(new NodeId("local")),
            coordinationOptions,
            source,
            new FakeHostApplicationLifetime(),
            NullLogger<MembershipService>.Instance
        );
        var sut = new MembershipHeartbeatBackgroundService(
            membership,
            source,
            coordinationOptions,
            timeProvider,
            NullLogger<MembershipHeartbeatBackgroundService>.Instance
        );

        return (sut, timeProvider, membership);
    }

    private static async Task _AdvanceUntilAttemptsAsync(
        MembershipHeartbeatBackgroundService sut,
        FakeMembershipStore store,
        FakeTimeProvider timeProvider,
        int expectedAttempts
    )
    {
        for (var i = 0; i < 20 && store.AllocateIncarnationCalls < expectedAttempts; i++)
        {
            timeProvider.Advance(TimeSpan.FromSeconds(10));
            await Task.Delay(10, AbortToken);
        }

        sut.ExecuteTask.Should().NotBeNull();
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
