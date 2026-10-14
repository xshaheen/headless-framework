// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Threading.Channels;
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
        var timeProvider = new RetryTimeProvider();
        var (sut, _, _) = _CreateSut(store, timeProvider: timeProvider);
        using var serviceLifetime = sut;

        // when
        await sut.StartAsync(AbortToken);
        await _AdvanceRegistrationRetriesAsync(timeProvider);

        // then
        var act = () => sut.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("registration unavailable");
        store.AllocateIncarnationCalls.Should().Be(5);
    }

    [Fact]
    public async Task should_stop_membership_loop_without_faulting_host_when_registration_fails_and_configured()
    {
        // given
        var store = new FakeMembershipStore { ThrowOnRegister = true };
        var options = new CoordinationOptions { MembershipLostBehavior = MembershipLostBehavior.StopMembershipOnly };
        var timeProvider = new RetryTimeProvider();
        var (sut, _, membership) = _CreateSut(store, options, timeProvider);
        using var serviceLifetime = sut;

        // when
        await sut.StartAsync(AbortToken);
        await _AdvanceRegistrationRetriesAsync(timeProvider);
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
            SelfFenceMargin = TimeSpan.Zero,
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
    public async Task should_reset_the_self_fence_budget_when_a_heartbeat_succeeds()
    {
        var options = new CoordinationOptions
        {
            HeartbeatInterval = TimeSpan.FromSeconds(1),
            SuspicionThreshold = TimeSpan.FromSeconds(2),
            DeadThreshold = TimeSpan.FromSeconds(3),
            SelfFenceMargin = TimeSpan.Zero,
        };
        var store = new FakeMembershipStore { ThrowOnHeartbeat = true };
        var (sut, timeProvider, membership) = _CreateSut(store, options);
        await membership.RegisterAsync(AbortToken);

        // Fail once inside the budget, then recover: the accepted heartbeat must reset the deadline.
        await sut.RunOnceAsync(AbortToken);
        timeProvider.Advance(TimeSpan.FromSeconds(2));
        store.ThrowOnHeartbeat = false;
        await sut.RunOnceAsync(AbortToken);

        // Without the reset-on-success write this failure lands 4s after register (> DeadThreshold) and fences.
        store.ThrowOnHeartbeat = true;
        timeProvider.Advance(TimeSpan.FromSeconds(2));
        await sut.RunOnceAsync(AbortToken);

        membership.Identity.Should().NotBeNull();
        membership.LocalMembershipLostToken.IsCancellationRequested.Should().BeFalse();
    }

    [Fact]
    public async Task should_measure_the_self_fence_from_before_a_slow_but_successful_beat()
    {
        // The store stamps LastBeat mid-call and peers classify Dead from LastBeat + DeadThreshold. Anchoring
        // the local fence to the beat's RETURN grants this node overhang equal to the call's latency — a slow
        // 2s success used to reset the full 3s budget, letting the node keep ownership-sensitive work running
        // while peers already reclaimed it. The fence must be measured from BEFORE the call.
        var options = new CoordinationOptions
        {
            HeartbeatInterval = TimeSpan.FromSeconds(1),
            SuspicionThreshold = TimeSpan.FromSeconds(2),
            DeadThreshold = TimeSpan.FromSeconds(3),
            SelfFenceMargin = TimeSpan.Zero,
        };
        var store = new FakeMembershipStore { BlockOnHeartbeat = true };
        var (sut, timeProvider, membership) = _CreateSut(store, options);
        await membership.RegisterAsync(AbortToken);

        // Slow-but-successful beat: 2s in-call (inside the 3s budget), then accepted.
        var slowBeat = sut.RunOnceAsync(AbortToken);
        timeProvider.Advance(TimeSpan.FromSeconds(2));
        store.HeartbeatRelease.TrySetResult();
        await slowBeat.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

        // 1s later the PRE-CALL anchor is 3s old — the budget is spent even though the last success returned
        // only 1s ago. A return-anchored fence would still see 2s of budget here and keep the node alive.
        store.BlockOnHeartbeat = false;
        timeProvider.Advance(TimeSpan.FromSeconds(1));
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
            SelfFenceMargin = TimeSpan.Zero,
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
            SelfFenceMargin = TimeSpan.Zero,
        };
        var store = new FakeMembershipStore { ThrowOnRead = true };
        var (sut, timeProvider, membership) = _CreateSut(store, options);
        await membership.RegisterAsync(AbortToken);

        // Beat every interval for longer than DeadThreshold while every liveness read fails.
        for (var i = 0; i < 4; i++)
        {
            timeProvider.Advance(options.HeartbeatInterval);
            await sut.RunOnceAsync(AbortToken);
        }

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

    [Fact]
    public async Task should_retry_registration_on_the_real_clock_when_the_app_clock_never_advances()
    {
        // given
        var store = new FakeMembershipStore { FailingRegistrations = 1 };
        var (sut, _, membership) = _CreateSut(store, realTimeProvider: TimeProvider.System);
        using var serviceLifetime = sut;

        // when
        await sut.StartAsync(AbortToken);

        // then
        var deadline = TimeProvider.System.GetTimestamp();
        while (membership.Identity is null && TimeProvider.System.GetElapsedTime(deadline) < TimeSpan.FromSeconds(5))
        {
            await Task.Delay(20, AbortToken);
        }

        membership.Identity.Should().NotBeNull();
        store.AllocateIncarnationCalls.Should().Be(2);
        await sut.StopAsync(AbortToken);
    }

    [Fact]
    public async Task should_bound_graceful_leave_on_the_real_clock_when_the_app_clock_never_advances()
    {
        // given
        var store = new FakeMembershipStore { BlockOnLeave = true };
        var (sut, _, membership) = _CreateSut(store, realTimeProvider: TimeProvider.System);
        await membership.RegisterAsync(AbortToken);

        // when
        var stopTask = sut.StopAsync(AbortToken);

        // then
        await stopTask.WaitAsync(TimeSpan.FromSeconds(10), AbortToken);
        store.Leaves.Should().BeEmpty();
    }

    [Fact]
    public async Task should_beat_during_a_slow_host_startup_and_not_fence()
    {
        // given: the Jobs startup gate registers in StartingAsync, then another service's StartingAsync runs longer
        // than DeadThreshold before any StartAsync. The loop must already be beating from that registration.
        var options = new CoordinationOptions
        {
            HeartbeatInterval = TimeSpan.FromSeconds(1),
            SuspicionThreshold = TimeSpan.FromSeconds(2),
            DeadThreshold = TimeSpan.FromSeconds(4),
            SelfFenceMargin = TimeSpan.FromSeconds(1),
            MembershipLostBehavior = MembershipLostBehavior.StopMembershipOnly,
        };
        var store = new FakeMembershipStore();
        var (sut, timeProvider, membership) = _CreateSut(store, options);
        using var serviceLifetime = sut;

        // when
        await sut.StartingAsync(AbortToken);
        await membership.RegisterAsync(AbortToken);

        await _WaitUntilAsync(() => store.Heartbeats.Count > 0);

        // Step the clock in small increments, yielding after each so the loop schedules its next delay before time
        // moves past it.
        for (var step = 0; step < 40; step++)
        {
            timeProvider.Advance(TimeSpan.FromMilliseconds(250));
            await Task.Delay(10, AbortToken);
        }

        // then: 10s of startup, well past DeadThreshold, and the loop kept the node a member
        store.Heartbeats.Should().HaveCountGreaterThan(4);
        membership.Identity.Should().NotBeNull();
        membership.LocalMembershipLostToken.IsCancellationRequested.Should().BeFalse();
        await sut.StopAsync(AbortToken);
    }

    [Fact]
    public async Task should_not_register_from_the_loop_started_in_starting_async()
    {
        // given
        var store = new FakeMembershipStore();
        var (sut, timeProvider, membership) = _CreateSut(store);
        using var serviceLifetime = sut;

        // when
        await sut.StartingAsync(AbortToken);
        timeProvider.Advance(TimeSpan.FromMinutes(1));
        await Task.Delay(50, AbortToken);

        // then: registration belongs to the startup gate or to StartAsync, never to the loop
        store.AllocateIncarnationCalls.Should().Be(0);
        membership.Identity.Should().BeNull();
        await sut.StopAsync(AbortToken);
    }

    [Fact]
    public async Task should_fail_startup_without_retrying_when_the_store_refuses_registration()
    {
        // given
        var store = new FakeMembershipStore { UpsertAccepted = false };
        var (sut, _, membership) = _CreateSut(store);
        using var serviceLifetime = sut;

        // when
        await sut.StartAsync(AbortToken);

        // then
        var act = () => sut.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
        await act.Should().ThrowAsync<MembershipRegistrationRejectedException>();
        store.AllocateIncarnationCalls.Should().Be(1);
        membership.Identity.Should().BeNull();
    }

    [Fact]
    public async Task should_not_register_a_second_incarnation_after_fencing_during_startup()
    {
        // given: registered in StartingAsync, then fenced before StartAsync runs
        var options = new CoordinationOptions
        {
            HeartbeatInterval = TimeSpan.FromSeconds(1),
            SuspicionThreshold = TimeSpan.FromSeconds(2),
            DeadThreshold = TimeSpan.FromSeconds(3),
            SelfFenceMargin = TimeSpan.Zero,
            MembershipLostBehavior = MembershipLostBehavior.StopMembershipOnly,
        };
        var store = new FakeMembershipStore { ThrowOnHeartbeat = true };
        var (sut, timeProvider, membership) = _CreateSut(store, options);
        using var serviceLifetime = sut;
        await sut.StartingAsync(AbortToken);
        await membership.RegisterAsync(AbortToken);
        timeProvider.Advance(options.DeadThreshold);
        await _WaitUntilAsync(() => membership.LocalMembershipLostToken.IsCancellationRequested);

        // when
        await sut.StartAsync(AbortToken);
        await sut.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

        // then
        store.AllocateIncarnationCalls.Should().Be(1);
        membership.Identity.Should().BeNull();
    }

    private static async Task _WaitUntilAsync(Func<bool> condition)
    {
        var started = TimeProvider.System.GetTimestamp();

        while (!condition())
        {
            if (TimeProvider.System.GetElapsedTime(started) > TimeSpan.FromSeconds(5))
            {
                throw new TimeoutException("The condition was not met within 5 seconds.");
            }

            await Task.Delay(5, AbortToken);
        }
    }

    private static (
        MembershipHeartbeatBackgroundService Sut,
        FakeTimeProvider TimeProvider,
        MembershipService Membership
    ) _CreateSut(
        FakeMembershipStore store,
        CoordinationOptions? options = null,
        FakeTimeProvider? timeProvider = null,
        TimeProvider? realTimeProvider = null
    )
    {
        var coordinationOptions = options ?? new CoordinationOptions();
        var source = new MembershipEventSource(NullLogger<MembershipEventSource>.Instance);
        timeProvider ??= new FakeTimeProvider();
        var membership = new MembershipService(
            store,
            new StaticNodeIdProvider(new NodeId("local")),
            coordinationOptions,
            source,
            timeProvider,
            new FakeHostApplicationLifetime(),
            NullLogger<MembershipService>.Instance
        );
        membership.EnableSelfFence();
        var sut = new MembershipHeartbeatBackgroundService(
            membership,
            source,
            coordinationOptions,
            timeProvider,
            realTimeProvider ?? timeProvider,
            NullLogger<MembershipHeartbeatBackgroundService>.Instance
        );

        return (sut, timeProvider, membership);
    }

    private static async Task _AdvanceRegistrationRetriesAsync(RetryTimeProvider timeProvider)
    {
        // Wait for each retry timer to exist before advancing; background startup may be delayed on CI.
        for (var i = 0; i < 4; i++)
        {
            var delay = await timeProvider
                .RetryDelays.Reader.ReadAsync(AbortToken)
                .AsTask()
                .WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
            timeProvider.Advance(delay);
        }
    }

    private sealed class RetryTimeProvider : FakeTimeProvider
    {
        public Channel<TimeSpan> RetryDelays { get; } = Channel.CreateUnbounded<TimeSpan>();

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = base.CreateTimer(callback, state, dueTime, period);
            RetryDelays.Writer.TryWrite(dueTime);
            return timer;
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
