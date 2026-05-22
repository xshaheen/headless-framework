// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.Testing.Tests;

namespace Tests;

public sealed class DistributedLockProviderExtensionsTests : TestBase
{
    [Fact]
    public async Task should_return_false_when_lock_not_acquired()
    {
        // given
        var provider = Substitute.For<IDistributedLockProvider>();
        provider
            .TryAcquireAsync(
                Arg.Any<string>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.FromResult<IDistributedLock?>(null));

        var workExecuted = false;

        // when
        var result = await provider.TryUsingAsync(
            "resource",
            () =>
            {
                workExecuted = true;
                return Task.CompletedTask;
            }
        );

        // then
        result.Should().BeFalse();
        workExecuted.Should().BeFalse();
    }

    [Fact]
    public async Task should_return_true_and_execute_work_when_lock_acquired()
    {
        // given
        var provider = Substitute.For<IDistributedLockProvider>();
        var distributedLock = Substitute.For<IDistributedLock>();

        provider
            .TryAcquireAsync(
                Arg.Any<string>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.FromResult<IDistributedLock?>(distributedLock));

        var workExecuted = false;

        // when
        var result = await provider.TryUsingAsync(
            "resource",
            () =>
            {
                workExecuted = true;
                return Task.CompletedTask;
            }
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
        var provider = Substitute.For<IDistributedLockProvider>();
        var distributedLock = Substitute.For<IDistributedLock>();

        provider
            .TryAcquireAsync(
                Arg.Any<string>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.FromResult<IDistributedLock?>(distributedLock));

        const string resource = "test-resource";
        var timeUntilExpires = TimeSpan.FromMinutes(10);
        var acquireTimeout = TimeSpan.FromSeconds(5);
        using var cts = new CancellationTokenSource();
        var cancellationToken = cts.Token;

        // when
        await provider.TryUsingAsync(
            resource,
            () => Task.CompletedTask,
            timeUntilExpires,
            acquireTimeout,
            cancellationToken: cancellationToken
        );

        // then
        await provider
            .Received(1)
            .TryAcquireAsync(
                resource,
                timeUntilExpires,
                acquireTimeout,
                releaseOnDispose: true,
                monitorLease: false,
                autoExtend: false,
                cancellationToken: cancellationToken
            );
    }

    [Fact]
    public async Task should_pass_monitor_flags_and_link_handle_lost_token_into_work()
    {
        // given - a real provider-built handle is needed to flow IsMonitored/HandleLostToken.
        // Stub with NSubstitute returning a Substitute-backed IDistributedLock whose IsMonitored
        // is true and HandleLostToken is a CTS we control. Verify the work delegate receives a
        // linked token that becomes cancelled when HandleLostToken fires.
        var provider = Substitute.For<IDistributedLockProvider>();
        var distributedLock = Substitute.For<IDistributedLock>();
        using var leaseLostCts = new CancellationTokenSource();
        distributedLock.IsMonitored.Returns(true);
        distributedLock.HandleLostToken.Returns(leaseLostCts.Token);
        provider
            .TryAcquireAsync(
                Arg.Any<string>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.FromResult<IDistributedLock?>(distributedLock));

        CancellationToken observedToken = default;
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
            monitorLease: true
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
                Arg.Any<TimeSpan?>(),
                Arg.Any<TimeSpan?>(),
                releaseOnDispose: true,
                monitorLease: true,
                autoExtend: false,
                cancellationToken: Arg.Any<CancellationToken>()
            );
    }
}
