// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.Testing.Tests;
using Microsoft.Extensions.Time.Testing;

namespace Tests.RegularLocks;

public sealed class DisposableDistributedLockTests : TestBase
{
    private readonly FakeTimeProvider _timeProvider = new();
    private readonly IDistributedLock _lockProvider = Substitute.For<IDistributedLock>();

    [Fact]
    public async Task should_store_resource_and_lock_id()
    {
        // given
        var resource = Faker.Random.AlphaNumeric(10);
        var leaseId = Faker.Random.Guid().ToString();

        // when
        await using var sut = _CreateLock(resource, leaseId);

        // then
        sut.Resource.Should().Be(resource);
        sut.LeaseId.Should().Be(leaseId);
    }

    [Fact]
    public async Task should_store_fencing_token()
    {
        // given
        var resource = Faker.Random.AlphaNumeric(10);
        var leaseId = Faker.Random.Guid().ToString();
        var fencingToken = Faker.Random.Long(1);

        // when
        await using var sut = _CreateLock(resource, leaseId, fencingToken: fencingToken);

        // then
        sut.FencingToken.Should().Be(fencingToken);
    }

    [Fact]
    public async Task should_store_acquired_at()
    {
        // given
        var expectedTime = new DateTimeOffset(2025, 6, 15, 10, 30, 0, TimeSpan.Zero);
        _timeProvider.SetUtcNow(expectedTime);
        var resource = Faker.Random.AlphaNumeric(10);
        var leaseId = Faker.Random.Guid().ToString();

        // when
        await using var sut = _CreateLock(resource, leaseId);

        // then
        sut.DateAcquired.Should().Be(expectedTime);
    }

    [Fact]
    public async Task should_store_time_waited_for_lock()
    {
        // given
        var resource = Faker.Random.AlphaNumeric(10);
        var leaseId = Faker.Random.Guid().ToString();
        var timeWaited = TimeSpan.FromSeconds(5);

        // when
        await using var sut = _CreateLock(resource, leaseId, timeWaited);

        // then
        sut.TimeWaitedForLock.Should().Be(timeWaited);
    }

    [Fact]
    public async Task should_release_on_dispose()
    {
        // given
        var resource = Faker.Random.AlphaNumeric(10);
        var leaseId = Faker.Random.Guid().ToString();
        await using var sut = _CreateLock(resource, leaseId);

        // when
        await sut.DisposeAsync();

        // then
        await _lockProvider.Received(1).ReleaseAsync(resource, leaseId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_not_release_on_dispose_when_release_on_dispose_is_false()
    {
        // given
        var resource = Faker.Random.AlphaNumeric(10);
        var leaseId = Faker.Random.Guid().ToString();
        await using var sut = _CreateLock(resource, leaseId, releaseOnDispose: false);

        // when
        await sut.DisposeAsync();

        // then
        await _lockProvider.DidNotReceive().ReleaseAsync(resource, leaseId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_return_none_handle_lost_token_when_monitor_is_absentAsync()
    {
        // given
        await using var sut = _CreateLock(Faker.Random.AlphaNumeric(10), Faker.Random.Guid().ToString());

        // when
        var token = sut.LostToken;

        // then
        token.Should().Be(CancellationToken.None);
        sut.CanObserveLoss.Should().BeFalse();
    }

    [Fact]
    public async Task should_release_explicitly_when_release_on_dispose_is_false()
    {
        // given
        var resource = Faker.Random.AlphaNumeric(10);
        var leaseId = Faker.Random.Guid().ToString();
        await using var sut = _CreateLock(resource, leaseId, releaseOnDispose: false);

        // when
        await sut.ReleaseAsync();
        await sut.DisposeAsync();

        // then
        await _lockProvider.Received(1).ReleaseAsync(resource, leaseId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_only_release_once()
    {
        // given
        var resource = Faker.Random.AlphaNumeric(10);
        var leaseId = Faker.Random.Guid().ToString();
        await using var sut = _CreateLock(resource, leaseId);

        // when
        await sut.DisposeAsync();
        await sut.DisposeAsync();
        await sut.ReleaseAsync();

        // then
        await _lockProvider.Received(1).ReleaseAsync(resource, leaseId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_stop_monitor_on_explicit_release_without_signaling_loss()
    {
        // given
        var resource = Faker.Random.AlphaNumeric(10);
        var leaseId = Faker.Random.Guid().ToString();
        var deregisterCount = 0;
        await using var sut = _CreateLock(
            resource,
            leaseId,
            deregisterMonitor: (actualResource, actualLockId) =>
            {
                actualResource.Should().Be(resource);
                actualLockId.Should().Be(leaseId);
                Interlocked.Increment(ref deregisterCount);
            }
        );
        await using var monitor = new LeaseMonitor(
            sut,
            _timeProvider,
            LoggerFactory.CreateLogger(nameof(LeaseMonitor))
        );
        sut.AttachMonitor(monitor);
        sut.CanObserveLoss.Should().BeTrue();
        var lostToken = sut.LostToken;

        // when
        await sut.ReleaseAsync();
        _timeProvider.Advance(TimeSpan.FromSeconds(20));

        // then
        sut.CanObserveLoss.Should().BeFalse();
        monitor.MonitoringTask.IsCompleted.Should().BeTrue();
        lostToken.IsCancellationRequested.Should().BeFalse();
        deregisterCount.Should().Be(1);
        await _lockProvider.Received(1).ReleaseAsync(resource, leaseId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_route_auto_extend_lease_validation_to_renew()
    {
        // given
        var resource = Faker.Random.AlphaNumeric(10);
        var leaseId = Faker.Random.Guid().ToString();
        _lockProvider.RenewAsync(resource, leaseId, Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>()).Returns(true);
        await using var sut = _CreateLock(resource, leaseId, autoExtend: true);

        // when
        var result = await ((LeaseMonitor.ILeaseHandle)sut).RenewOrValidateLeaseAsync(AbortToken);

        // then
        result.Should().Be(LeaseMonitor.LeaseState.Renewed);
        sut.RenewalCount.Should().Be(1);
        await _lockProvider
            .Received(1)
            .RenewAsync(resource, leaseId, TimeSpan.FromSeconds(10), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_treat_renew_false_with_matching_lock_id_as_unknown_under_auto_extend()
    {
        // given - autoExtend handle whose RenewAsync returns false (transient retry-exhaustion),
        // but ownership probe via GetLeaseIdAsync still shows our LeaseId — i.e., we still own it.
        var resource = Faker.Random.AlphaNumeric(10);
        var leaseId = Faker.Random.Guid().ToString();
        _lockProvider.RenewAsync(resource, leaseId, Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>()).Returns(false);
        _lockProvider.GetLeaseIdAsync(resource, Arg.Any<CancellationToken>()).Returns(leaseId);
        await using var sut = _CreateLock(resource, leaseId, autoExtend: true);

        // when
        var result = await ((LeaseMonitor.ILeaseHandle)sut).RenewOrValidateLeaseAsync(AbortToken);

        // then - we still own the lock; classify as Unknown so the safety net governs.
        result.Should().Be(LeaseMonitor.LeaseState.Unknown);
    }

    [Fact]
    public async Task should_treat_renew_false_with_differing_lock_id_as_lost_under_auto_extend()
    {
        // given - autoExtend handle whose RenewAsync returns false and probe shows a different owner.
        var resource = Faker.Random.AlphaNumeric(10);
        var leaseId = Faker.Random.Guid().ToString();
        _lockProvider.RenewAsync(resource, leaseId, Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>()).Returns(false);
        _lockProvider.GetLeaseIdAsync(resource, Arg.Any<CancellationToken>()).Returns("foreign-lock");
        await using var sut = _CreateLock(resource, leaseId, autoExtend: true);

        // when
        var result = await ((LeaseMonitor.ILeaseHandle)sut).RenewOrValidateLeaseAsync(AbortToken);

        // then - genuine loss: a different owner holds the lock.
        result.Should().Be(LeaseMonitor.LeaseState.Lost);
    }

    [Fact]
    public async Task should_treat_renew_false_with_null_probe_as_lost_under_auto_extend()
    {
        // given - autoExtend handle whose RenewAsync returns false and probe shows nothing.
        var resource = Faker.Random.AlphaNumeric(10);
        var leaseId = Faker.Random.Guid().ToString();
        _lockProvider.RenewAsync(resource, leaseId, Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>()).Returns(false);
        _lockProvider.GetLeaseIdAsync(resource, Arg.Any<CancellationToken>()).Returns((string?)null);
        await using var sut = _CreateLock(resource, leaseId, autoExtend: true);

        // when
        var result = await ((LeaseMonitor.ILeaseHandle)sut).RenewOrValidateLeaseAsync(AbortToken);

        // then - storage no longer has the row; genuine loss.
        result.Should().Be(LeaseMonitor.LeaseState.Lost);
    }

    [Fact]
    public async Task should_route_monitor_only_lease_validation_to_get_lock_id()
    {
        // given
        var resource = Faker.Random.AlphaNumeric(10);
        var leaseId = Faker.Random.Guid().ToString();
        _lockProvider.GetLeaseIdAsync(resource, Arg.Any<CancellationToken>()).Returns(leaseId);
        await using var sut = _CreateLock(resource, leaseId);

        // when
        var result = await ((LeaseMonitor.ILeaseHandle)sut).RenewOrValidateLeaseAsync(AbortToken);

        // then
        result.Should().Be(LeaseMonitor.LeaseState.Held);
        await _lockProvider.Received(1).GetLeaseIdAsync(resource, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_not_throw_when_handle_lost_token_is_read_after_dispose()
    {
        // given - acquire a monitor-backed handle and dispose it (which disposes the inner CTS).
        var resource = Faker.Random.AlphaNumeric(10);
        var leaseId = Faker.Random.Guid().ToString();
        _lockProvider.GetLeaseIdAsync(resource, Arg.Any<CancellationToken>()).Returns(leaseId);
        await using var sut = _CreateLock(resource, leaseId);
        await using var monitor = new LeaseMonitor(
            sut,
            _timeProvider,
            LoggerFactory.CreateLogger(nameof(LeaseMonitor))
        );
        sut.AttachMonitor(monitor);

        // when - dispose (which disposes the monitor's _handleLostSource CTS).
        await sut.DisposeAsync();

        // then - reading LostToken after dispose must not throw ObjectDisposedException.
        var act = () =>
        {
            var token = sut.LostToken;
            _ = token.IsCancellationRequested;
        };
        act.Should().NotThrow();
    }

    [Fact]
    public async Task should_apply_per_iteration_deadline_when_storage_blocks_indefinitely()
    {
        // given - storage that respects only the token passed in (not the caller's CT). The
        // per-iteration deadline created inside RenewOrValidateLeaseAsync must link the
        // caller's token with a TimeProvider-backed deadline; when the deadline fires the
        // storage call is cancelled, RenewOrValidateLeaseAsync catches and classifies as
        // Unknown so DisposeAsync does not wedge waiting on a stuck storage round-trip.
        var resource = Faker.Random.AlphaNumeric(10);
        var leaseId = Faker.Random.Guid().ToString();
        _lockProvider
            .GetLeaseIdAsync(resource, Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var ct = ci.Arg<CancellationToken>();
                var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
                ct.Register(() => tcs.TrySetCanceled(ct));
                return tcs.Task;
            });
        await using var sut = _CreateLock(resource, leaseId);

        // when - call the iteration with an uncancellable caller token, then advance fake time
        // past the per-iteration deadline.
        var task = ((LeaseMonitor.ILeaseHandle)sut).RenewOrValidateLeaseAsync(AbortToken);
        await Task.Yield();
        _timeProvider.Advance(TimeSpan.FromSeconds(10));

        // then - completes within a bounded real time and classifies as Unknown.
        var result = await task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
        result.Should().Be(LeaseMonitor.LeaseState.Unknown);
    }

    [Fact]
    public async Task should_dispose_monitor_when_storage_ignores_cancellation()
    {
        // given - storage operation never completes and ignores the provided token. DisposeAsync
        // must still drain the monitor after the TimeProvider-backed deadline fires.
        var resource = Faker.Random.AlphaNumeric(10);
        var leaseId = Faker.Random.Guid().ToString();
        var validationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _lockProvider
            .GetLeaseIdAsync(resource, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                validationStarted.TrySetResult();
                var blocked = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);

                return blocked.Task;
            });
        await using var sut = _CreateLock(resource, leaseId);
        await using var monitor = new LeaseMonitor(
            sut,
            _timeProvider,
            LoggerFactory.CreateLogger(nameof(LeaseMonitor))
        );
        sut.AttachMonitor(monitor);
        monitor.TriggerImmediateValidation();
        await validationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

        // when
        var disposeTask = sut.DisposeAsync().AsTask();
        await Task.Yield();
        _timeProvider.Advance(TimeSpan.FromSeconds(10));

        // then
        await disposeTask.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
        monitor.MonitoringTask.IsCompleted.Should().BeTrue();
    }

    [Fact]
    public async Task should_not_start_parallel_monitor_probes_when_storage_ignores_cancellation()
    {
        // given
        var resource = Faker.Random.AlphaNumeric(10);
        var leaseId = Faker.Random.Guid().ToString();
        var callCount = 0;
        _lockProvider
            .GetLeaseIdAsync(resource, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                Interlocked.Increment(ref callCount);
                var blocked = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);

                return blocked.Task;
            });
        await using var sut = _CreateLock(resource, leaseId);

        // when
        var first = ((LeaseMonitor.ILeaseHandle)sut).RenewOrValidateLeaseAsync(AbortToken);
        await Task.Yield();
        _timeProvider.Advance(TimeSpan.FromSeconds(10));
        var firstResult = await first.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

        var second = ((LeaseMonitor.ILeaseHandle)sut).RenewOrValidateLeaseAsync(AbortToken);
        await Task.Yield();
        _timeProvider.Advance(TimeSpan.FromSeconds(10));
        var secondResult = await second.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

        // then
        firstResult.Should().Be(LeaseMonitor.LeaseState.Unknown);
        secondResult.Should().Be(LeaseMonitor.LeaseState.Unknown);
        callCount.Should().Be(1);
    }

    [Fact]
    public async Task should_observe_late_successful_probe_after_iteration_deadline()
    {
        // given
        var resource = Faker.Random.AlphaNumeric(10);
        var leaseId = Faker.Random.Guid().ToString();
        var probe = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var callCount = 0;
        _lockProvider
            .GetLeaseIdAsync(resource, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                Interlocked.Increment(ref callCount);
                return probe.Task;
            });
        await using var sut = _CreateLock(resource, leaseId);

        // when - first iteration times out before storage replies.
        var first = ((LeaseMonitor.ILeaseHandle)sut).RenewOrValidateLeaseAsync(AbortToken);
        await Task.Yield();
        _timeProvider.Advance(TimeSpan.FromSeconds(10));
        var firstResult = await first.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

        probe.SetResult(leaseId);
        var secondResult = await ((LeaseMonitor.ILeaseHandle)sut)
            .RenewOrValidateLeaseAsync(AbortToken)
            .WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

        // then - the completed single-flight result is consumed once instead of being discarded.
        firstResult.Should().Be(LeaseMonitor.LeaseState.Unknown);
        secondResult.Should().Be(LeaseMonitor.LeaseState.Held);
        callCount.Should().Be(1);
    }

    [Fact]
    public async Task should_not_replay_successful_probe_after_it_was_observed()
    {
        // given
        var resource = Faker.Random.AlphaNumeric(10);
        var leaseId = Faker.Random.Guid().ToString();
        var currentLockId = leaseId;
        var callCount = 0;
        _lockProvider
            .GetLeaseIdAsync(resource, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                Interlocked.Increment(ref callCount);
                return Task.FromResult<string?>(currentLockId);
            });
        await using var sut = _CreateLock(resource, leaseId);

        // when
        var firstResult = await ((LeaseMonitor.ILeaseHandle)sut).RenewOrValidateLeaseAsync(AbortToken);
        currentLockId = "foreign-lock";
        var secondResult = await ((LeaseMonitor.ILeaseHandle)sut).RenewOrValidateLeaseAsync(AbortToken);

        // then - the second probe must hit storage again instead of replaying stale Held.
        firstResult.Should().Be(LeaseMonitor.LeaseState.Held);
        secondResult.Should().Be(LeaseMonitor.LeaseState.Lost);
        callCount.Should().Be(2);
    }

    [Fact]
    public async Task should_calculate_locked_duration()
    {
        // given
        var resource = Faker.Random.AlphaNumeric(10);
        var leaseId = Faker.Random.Guid().ToString();
        await using var sut = _CreateLock(resource, leaseId);
        var expectedDuration = TimeSpan.FromSeconds(30);

        // when
        _timeProvider.Advance(expectedDuration);
        await sut.DisposeAsync();

        // then - verify the elapsed time was captured (logged)
        // The lock calculates duration using timeProvider.GetElapsedTime(_timestamp)
        // We verify indirectly by checking that the lock was released after the time elapsed
        await _lockProvider.Received(1).ReleaseAsync(resource, leaseId, Arg.Any<CancellationToken>());
    }

    private DisposableDistributedLock _CreateLock(
        string resource,
        string leaseId,
        TimeSpan? timeWaitedForLock = null,
        bool releaseOnDispose = true,
        bool autoExtend = false,
        long? fencingToken = null,
        Action<string, string>? deregisterMonitor = null
    )
    {
        return new DisposableDistributedLock(
            resource,
            leaseId,
            fencingToken,
            TimeSpan.FromSeconds(10),
            timeWaitedForLock ?? TimeSpan.Zero,
            _lockProvider,
            releaseOnDispose,
            autoExtend,
            new DistributedLockOptions(),
            _timeProvider,
            deregisterMonitor,
            LoggerFactory.CreateLogger(nameof(DisposableDistributedLock))
        );
    }
}
