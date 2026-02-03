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
        await distributedLock.Received(1).ReleaseAsync();
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
            cancellationToken
        );

        // then
        await provider.Received(1).TryAcquireAsync(resource, timeUntilExpires, acquireTimeout, cancellationToken);
    }
}
