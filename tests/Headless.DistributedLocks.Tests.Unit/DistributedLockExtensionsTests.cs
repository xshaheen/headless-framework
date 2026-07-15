// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.Testing.Tests;

namespace Tests;

public sealed class DistributedLockExtensionsTests : TestBase
{
    [Fact]
    public async Task should_release_handle_through_provider()
    {
        // given
        var provider = Substitute.For<IDistributedLock>();
        var distributedLock = Substitute.For<IDistributedLease>();
        distributedLock.Resource.Returns("resource");
        distributedLock.LeaseId.Returns("lock-id");
        provider.ReleaseAsync("resource", "lock-id", AbortToken).Returns(Task.CompletedTask);

        // when
        await provider.ReleaseAsync(distributedLock, AbortToken);

        // then
        await distributedLock.DidNotReceive().ReleaseAsync();
        await provider.Received(1).ReleaseAsync("resource", "lock-id", AbortToken);
    }

    [Fact]
    public async Task should_return_provider_renewal_result_for_handle_renewal()
    {
        var provider = Substitute.For<IDistributedLock>();
        var distributedLock = Substitute.For<IDistributedLease>();
        var timeUntilExpires = TimeSpan.FromMinutes(5);
        distributedLock.Resource.Returns("resource");
        distributedLock.LeaseId.Returns("lock-id");
        provider.RenewAsync("resource", "lock-id", timeUntilExpires, AbortToken).Returns(Task.FromResult(false));

        var result = await provider.RenewAsync(distributedLock, timeUntilExpires, AbortToken);

        result.Should().BeFalse();
        await provider.Received(1).RenewAsync("resource", "lock-id", timeUntilExpires, AbortToken);
    }

    [Fact]
    public async Task should_return_false_when_lock_not_acquired()
    {
        // given
        var provider = Substitute.For<IDistributedLock>();
        provider
            .TryAcquireAsync(Arg.Any<string>(), Arg.Any<DistributedLockAcquireOptions?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IDistributedLease?>(null));

        var workExecuted = false;

        // when
        var result = await provider.TryUsingAsync(
            "resource",
            () =>
            {
                workExecuted = true;
                return Task.CompletedTask;
            },
            cancellationToken: AbortToken
        );

        // then
        result.Should().BeFalse();
        workExecuted.Should().BeFalse();
    }

    [Fact]
    public async Task should_return_true_and_execute_work_when_lock_acquired()
    {
        // given
        var provider = Substitute.For<IDistributedLock>();
        var distributedLock = Substitute.For<IDistributedLease>();

        provider
            .TryAcquireAsync(Arg.Any<string>(), Arg.Any<DistributedLockAcquireOptions?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IDistributedLease?>(distributedLock));

        var workExecuted = false;

        // when
        var result = await provider.TryUsingAsync(
            "resource",
            () =>
            {
                workExecuted = true;
                return Task.CompletedTask;
            },
            cancellationToken: AbortToken
        );

        // then
        result.Should().BeTrue();
        workExecuted.Should().BeTrue();
        // TryUsingAsync uses `await using` so DisposeAsync is invoked (which drains the lease
        // monitor and then releases the storage row). Asserting on DisposeAsync rather than
        // ReleaseAsync because the extension no longer calls ReleaseAsync directly.
        await distributedLock.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task should_pass_parameters_to_try_acquire()
    {
        // given
        var provider = Substitute.For<IDistributedLock>();
        var distributedLock = Substitute.For<IDistributedLease>();

        provider
            .TryAcquireAsync(Arg.Any<string>(), Arg.Any<DistributedLockAcquireOptions?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IDistributedLease?>(distributedLock));

        const string resource = "test-resource";
        var timeUntilExpires = TimeSpan.FromMinutes(10);
        var acquireTimeout = TimeSpan.FromSeconds(5);
        var cancellationToken = AbortToken;

        // when
        await provider.TryUsingAsync(
            resource,
            () => Task.CompletedTask,
            new DistributedLockAcquireOptions { TimeUntilExpires = timeUntilExpires, AcquireTimeout = acquireTimeout },
            cancellationToken
        );

        // then
        await provider
            .Received(1)
            .TryAcquireAsync(
                resource,
                Arg.Is<DistributedLockAcquireOptions?>(o =>
                    o != null
                    && o.TimeUntilExpires == timeUntilExpires
                    && o.AcquireTimeout == acquireTimeout
                    && o.ReleaseOnDispose
                    && o.Monitoring == LockMonitoringMode.None
                ),
                cancellationToken
            );
    }

    [Fact]
    public async Task should_pass_options_to_sync_try_using_and_force_monitoring_off()
    {
        // given
        var provider = Substitute.For<IDistributedLock>();
        var distributedLock = Substitute.For<IDistributedLease>();
        provider
            .TryAcquireAsync(Arg.Any<string>(), Arg.Any<DistributedLockAcquireOptions?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IDistributedLease?>(distributedLock));
        var workExecuted = false;
        var options = new DistributedLockAcquireOptions
        {
            TimeUntilExpires = TimeSpan.FromSeconds(10),
            AcquireTimeout = TimeSpan.FromSeconds(2),
            ReleaseOnDispose = false,
            Monitoring = LockMonitoringMode.Monitor,
        };

        // when
        var result = await provider.TryUsingAsync("resource", () => workExecuted = true, options, AbortToken);

        // then
        result.Should().BeTrue();
        workExecuted.Should().BeTrue();
        await provider
            .Received(1)
            .TryAcquireAsync(
                "resource",
                Arg.Is<DistributedLockAcquireOptions?>(o =>
                    o != null
                    && o.TimeUntilExpires == options.TimeUntilExpires
                    && o.AcquireTimeout == options.AcquireTimeout
                    && o.ReleaseOnDispose
                    && o.Monitoring == LockMonitoringMode.None
                ),
                AbortToken
            );
    }

    [Fact]
    public async Task should_pass_options_to_sync_state_try_using()
    {
        // given
        var provider = Substitute.For<IDistributedLock>();
        var distributedLock = Substitute.For<IDistributedLease>();
        provider
            .TryAcquireAsync(Arg.Any<string>(), Arg.Any<DistributedLockAcquireOptions?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IDistributedLease?>(distributedLock));
        var observedState = 0;

        // when
        var result = await provider.TryUsingAsync(
            "resource",
            42,
            state => observedState = state,
            new DistributedLockAcquireOptions { TimeUntilExpires = TimeSpan.FromSeconds(10) },
            AbortToken
        );

        // then
        result.Should().BeTrue();
        observedState.Should().Be(42);
        await provider
            .Received(1)
            .TryAcquireAsync(
                "resource",
                Arg.Is<DistributedLockAcquireOptions?>(o =>
                    o != null
                    && o.TimeUntilExpires == TimeSpan.FromSeconds(10)
                    && o.ReleaseOnDispose
                    && o.Monitoring == LockMonitoringMode.None
                ),
                AbortToken
            );
    }

    [Fact]
    public async Task should_pass_monitor_flags_and_link_handle_lost_token_into_work()
    {
        // given - a real provider-built handle is needed to flow CanObserveLoss/LostToken.
        // Stub with NSubstitute returning a Substitute-backed IDistributedLease whose CanObserveLoss
        // is true and LostToken is a CTS we control. Verify the work delegate receives a
        // linked token that becomes cancelled when LostToken fires.
        var provider = Substitute.For<IDistributedLock>();
        var distributedLock = Substitute.For<IDistributedLease>();
        using var leaseLostCts = new CancellationTokenSource();
        distributedLock.CanObserveLoss.Returns(true);
        distributedLock.LostToken.Returns(leaseLostCts.Token);

        provider
            .TryAcquireAsync(Arg.Any<string>(), Arg.Any<DistributedLockAcquireOptions?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IDistributedLease?>(distributedLock));

        var observedToken = CancellationToken.None;
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        // when
        var task = provider.TryUsingAsync(
            "resource",
            async ct =>
            {
                observedToken = ct;
                started.SetResult(true);
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                }
                catch (OperationCanceledException) { }
            },
            new DistributedLockAcquireOptions { Monitoring = LockMonitoringMode.Monitor },
            AbortToken
        );

        await started.Task;
        await leaseLostCts.CancelAsync();
        var result = await task;

        // then
        result.Should().BeTrue();
        observedToken.CanBeCanceled.Should().BeTrue();
        observedToken.IsCancellationRequested.Should().BeTrue();
        await provider
            .Received(1)
            .TryAcquireAsync(
                "resource",
                Arg.Is<DistributedLockAcquireOptions?>(o =>
                    o != null && o.Monitoring == LockMonitoringMode.Monitor && o.ReleaseOnDispose
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_throw_when_release_extension_provider_is_null()
    {
        // given
        IDistributedLock provider = null!;
        var distributedLock = Substitute.For<IDistributedLease>();

        // when
        var act = async () => await provider.ReleaseAsync(distributedLock, AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("provider");
    }

    [Fact]
    public async Task should_pass_state_to_async_state_work_and_dispose_handle()
    {
        // given
        var provider = Substitute.For<IDistributedLock>();
        var distributedLock = Substitute.For<IDistributedLease>();
        provider
            .TryAcquireAsync(Arg.Any<string>(), Arg.Any<DistributedLockAcquireOptions?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IDistributedLease?>(distributedLock));
        var observedState = 0;

        // when — the TState overload avoids a closure; the state must flow into the work delegate
        var result = await provider.TryUsingAsync(
            "resource",
            42,
            state =>
            {
                observedState = state;
                return Task.CompletedTask;
            },
            options: null,
            AbortToken
        );

        // then
        result.Should().BeTrue();
        observedState.Should().Be(42);
        await distributedLock.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task should_not_run_async_state_work_when_lock_not_acquired()
    {
        // given
        var provider = Substitute.For<IDistributedLock>();
        provider
            .TryAcquireAsync(Arg.Any<string>(), Arg.Any<DistributedLockAcquireOptions?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IDistributedLease?>(null));
        var workExecuted = false;

        // when
        var result = await provider.TryUsingAsync(
            "resource",
            42,
            _ =>
            {
                workExecuted = true;
                return Task.CompletedTask;
            },
            options: null,
            AbortToken
        );

        // then
        result.Should().BeFalse();
        workExecuted.Should().BeFalse();
    }

    [Fact]
    public async Task should_link_lost_token_into_async_state_work_when_loss_observable()
    {
        // given — a monitored handle whose LostToken we control; the state+token overload must
        // hand the work a token linked to lease loss, exactly like the stateless overload.
        var provider = Substitute.For<IDistributedLock>();
        var distributedLock = Substitute.For<IDistributedLease>();
        using var leaseLostCts = new CancellationTokenSource();
        distributedLock.CanObserveLoss.Returns(true);
        distributedLock.LostToken.Returns(leaseLostCts.Token);
        provider
            .TryAcquireAsync(Arg.Any<string>(), Arg.Any<DistributedLockAcquireOptions?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IDistributedLease?>(distributedLock));

        var observedState = 0;
        var observedToken = CancellationToken.None;
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        // when
        var task = provider.TryUsingAsync(
            "resource",
            42,
            async (state, ct) =>
            {
                observedState = state;
                observedToken = ct;
                started.SetResult(true);
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                }
                catch (OperationCanceledException) { }
            },
            new DistributedLockAcquireOptions { Monitoring = LockMonitoringMode.Monitor },
            AbortToken
        );

        await started.Task;
        await leaseLostCts.CancelAsync();
        var result = await task;

        // then
        result.Should().BeTrue();
        observedState.Should().Be(42);
        observedToken.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public async Task should_pass_bare_caller_token_when_loss_not_observable()
    {
        // given — an unmonitored handle (CanObserveLoss = false); the token-receiving overload
        // must pass the caller's token through unchanged instead of allocating a linked source.
        var provider = Substitute.For<IDistributedLock>();
        var distributedLock = Substitute.For<IDistributedLease>();
        distributedLock.CanObserveLoss.Returns(false);
        provider
            .TryAcquireAsync(Arg.Any<string>(), Arg.Any<DistributedLockAcquireOptions?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IDistributedLease?>(distributedLock));

        var callerToken = AbortToken;
        var observedToken = CancellationToken.None;

        // when
        var result = await provider.TryUsingAsync(
            "resource",
            ct =>
            {
                observedToken = ct;
                return Task.CompletedTask;
            },
            options: null,
            callerToken
        );

        // then
        result.Should().BeTrue();
        observedToken.Should().Be(callerToken);
    }

    [Fact]
    public async Task should_map_timespan_arguments_into_options_for_sync_work()
    {
        // given
        var provider = Substitute.For<IDistributedLock>();
        var distributedLock = Substitute.For<IDistributedLease>();
        provider
            .TryAcquireAsync(Arg.Any<string>(), Arg.Any<DistributedLockAcquireOptions?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IDistributedLease?>(distributedLock));
        var workExecuted = false;
        var timeUntilExpires = TimeSpan.FromMinutes(3);
        var acquireTimeout = TimeSpan.FromSeconds(7);

        // when — the TimeSpan convenience overload builds the options itself
        var result = await provider.TryUsingAsync(
            "resource",
            () => workExecuted = true,
            timeUntilExpires,
            acquireTimeout,
            AbortToken
        );

        // then — sync work cannot observe loss, so monitoring is forced off
        result.Should().BeTrue();
        workExecuted.Should().BeTrue();
        await provider
            .Received(1)
            .TryAcquireAsync(
                "resource",
                Arg.Is<DistributedLockAcquireOptions?>(o =>
                    o != null
                    && o.TimeUntilExpires == timeUntilExpires
                    && o.AcquireTimeout == acquireTimeout
                    && o.ReleaseOnDispose
                    && o.Monitoring == LockMonitoringMode.None
                ),
                AbortToken
            );
    }

    [Fact]
    public async Task should_map_timespan_arguments_into_options_for_sync_state_work()
    {
        // given
        var provider = Substitute.For<IDistributedLock>();
        var distributedLock = Substitute.For<IDistributedLease>();
        provider
            .TryAcquireAsync(Arg.Any<string>(), Arg.Any<DistributedLockAcquireOptions?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IDistributedLease?>(distributedLock));
        var observedState = 0;
        var timeUntilExpires = TimeSpan.FromMinutes(3);
        var acquireTimeout = TimeSpan.FromSeconds(7);

        // when
        var result = await provider.TryUsingAsync(
            "resource",
            42,
            state => observedState = state,
            timeUntilExpires,
            acquireTimeout,
            AbortToken
        );

        // then
        result.Should().BeTrue();
        observedState.Should().Be(42);
        await provider
            .Received(1)
            .TryAcquireAsync(
                "resource",
                Arg.Is<DistributedLockAcquireOptions?>(o =>
                    o != null
                    && o.TimeUntilExpires == timeUntilExpires
                    && o.AcquireTimeout == acquireTimeout
                    && o.ReleaseOnDispose
                    && o.Monitoring == LockMonitoringMode.None
                ),
                AbortToken
            );
    }
}
