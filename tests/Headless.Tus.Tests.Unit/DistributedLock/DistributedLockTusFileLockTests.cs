// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.Testing.Tests;
using Headless.Tus;

namespace Tests.DistributedLock;

public sealed class DistributedLockTusFileLockTests : TestBase
{
    private readonly IDistributedLockProvider _distributedLockProvider = Substitute.For<IDistributedLockProvider>();

    #region Lock

    [Fact]
    public async Task should_acquire_lock_via_provider()
    {
        // given
        const string fileId = "test-file-123";
        var distributedLock = Substitute.For<IDistributedLock>();

        _distributedLockProvider
            .TryAcquireAsync(
                Arg.Any<string>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(distributedLock);

        var sut = new DistributedLockTusFileLock(fileId, _distributedLockProvider);

        // when
        var result = await sut.Lock();

        // then
        result.Should().BeTrue();
    }

    [Fact]
    public async Task should_return_false_when_lock_unavailable()
    {
        // given
        const string fileId = "test-file-123";
        _distributedLockProvider
            .TryAcquireAsync(
                Arg.Any<string>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns((IDistributedLock?)null);

        var sut = new DistributedLockTusFileLock(fileId, _distributedLockProvider);

        // when
        var result = await sut.Lock();

        // then
        result.Should().BeFalse();
    }

    [Fact]
    public async Task should_use_correct_resource_name()
    {
        // given
        const string fileId = "my-upload-id";
        var sut = new DistributedLockTusFileLock(fileId, _distributedLockProvider);

        // when
        await sut.Lock();

        // then
        await _distributedLockProvider
            .Received(1)
            .TryAcquireAsync(
                "tus-file-lock-my-upload-id",
                Arg.Any<TimeSpan?>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_use_infinite_expiration()
    {
        // given
        const string fileId = "test-file";
        var sut = new DistributedLockTusFileLock(fileId, _distributedLockProvider);

        // when
        await sut.Lock();

        // then
        await _distributedLockProvider
            .Received(1)
            .TryAcquireAsync(
                Arg.Any<string>(),
                Timeout.InfiniteTimeSpan,
                Arg.Any<TimeSpan?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_use_zero_acquire_timeout()
    {
        // given
        const string fileId = "test-file";
        var sut = new DistributedLockTusFileLock(fileId, _distributedLockProvider);

        // when
        await sut.Lock();

        // then
        await _distributedLockProvider
            .Received(1)
            .TryAcquireAsync(Arg.Any<string>(), Arg.Any<TimeSpan?>(), TimeSpan.Zero, Arg.Any<CancellationToken>());
    }

    #endregion

    #region ReleaseIfHeld

    [Fact]
    public async Task should_release_lock_when_held()
    {
        // given
        const string fileId = "test-file";
        const string lockId = "lock-123";
        var distributedLock = Substitute.For<IDistributedLock>();
        distributedLock.Resource.Returns("tus-file-lock-test-file");
        distributedLock.LockId.Returns(lockId);

        _distributedLockProvider
            .TryAcquireAsync(
                Arg.Any<string>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(distributedLock);

        var sut = new DistributedLockTusFileLock(fileId, _distributedLockProvider);
        await sut.Lock();

        // when
        await sut.ReleaseIfHeld();

        // then
        await _distributedLockProvider
            .Received(1)
            .ReleaseAsync("tus-file-lock-test-file", lockId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_noop_release_when_not_held()
    {
        // given
        const string fileId = "test-file";
        _distributedLockProvider
            .TryAcquireAsync(
                Arg.Any<string>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns((IDistributedLock?)null);

        var sut = new DistributedLockTusFileLock(fileId, _distributedLockProvider);
        await sut.Lock(); // Lock fails, _distributedLock remains null

        // when
        await sut.ReleaseIfHeld();

        // then - no exception and ReleaseAsync not called
        await _distributedLockProvider
            .DidNotReceive()
            .ReleaseAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    #endregion
}
