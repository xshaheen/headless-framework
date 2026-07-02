// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.Testing.Tests;
using Headless.Tus;

namespace Tests.DistributedLock;

public sealed class DistributedLockTusFileLockTests : TestBase
{
    private readonly IDistributedLock _distributedLockProvider = Substitute.For<IDistributedLock>();

    #region Lock

    [Fact]
    public async Task should_acquire_lock_via_provider()
    {
        // given
        const string fileId = "test-file-123";
        var distributedLock = Substitute.For<IDistributedLease>();

        _distributedLockProvider
            .TryAcquireAsync(Arg.Any<string>(), Arg.Any<DistributedLockAcquireOptions?>(), Arg.Any<CancellationToken>())
            .Returns(distributedLock);

        await using var sut = new DistributedLockTusFileLock(fileId, _distributedLockProvider);

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
            .TryAcquireAsync(Arg.Any<string>(), Arg.Any<DistributedLockAcquireOptions?>(), Arg.Any<CancellationToken>())
            .Returns((IDistributedLease?)null);

        await using var sut = new DistributedLockTusFileLock(fileId, _distributedLockProvider);

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
        await using var sut = new DistributedLockTusFileLock(fileId, _distributedLockProvider);

        // when
        await sut.Lock();

        // then
        await _distributedLockProvider
            .Received(1)
            .TryAcquireAsync(
                "tus-file-lock-my-upload-id",
                Arg.Any<DistributedLockAcquireOptions?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_use_finite_lease_so_a_crashed_holder_is_recoverable()
    {
        // given
        const string fileId = "test-file";
        await using var sut = new DistributedLockTusFileLock(fileId, _distributedLockProvider);

        // when
        await sut.Lock();

        // then: null TTL uses the provider's finite default; an infinite lease would stay stuck after a crash
        await _distributedLockProvider
            .Received(1)
            .TryAcquireAsync(
                Arg.Any<string>(),
                Arg.Is<DistributedLockAcquireOptions?>(o => o != null && o.TimeUntilExpires == null),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_auto_extend_lease_while_held()
    {
        // given
        const string fileId = "test-file";
        await using var sut = new DistributedLockTusFileLock(fileId, _distributedLockProvider);

        // when
        await sut.Lock();

        // then: AutoExtend keeps the lease alive across long uploads while still expiring on crash
        await _distributedLockProvider
            .Received(1)
            .TryAcquireAsync(
                Arg.Any<string>(),
                Arg.Is<DistributedLockAcquireOptions?>(o => o != null && o.Monitoring == LockMonitoringMode.AutoExtend),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_use_zero_acquire_timeout()
    {
        // given
        const string fileId = "test-file";
        await using var sut = new DistributedLockTusFileLock(fileId, _distributedLockProvider);

        // when
        await sut.Lock();

        // then
        await _distributedLockProvider
            .Received(1)
            .TryAcquireAsync(
                Arg.Any<string>(),
                Arg.Is<DistributedLockAcquireOptions?>(o => o != null && o.AcquireTimeout == TimeSpan.Zero),
                Arg.Any<CancellationToken>()
            );
    }

    #endregion

    #region ReleaseIfHeld

    [Fact]
    public async Task should_release_lock_when_held()
    {
        // given
        const string fileId = "test-file";
        var distributedLock = Substitute.For<IDistributedLease>();

        _distributedLockProvider
            .TryAcquireAsync(Arg.Any<string>(), Arg.Any<DistributedLockAcquireOptions?>(), Arg.Any<CancellationToken>())
            .Returns(distributedLock);

        await using var sut = new DistributedLockTusFileLock(fileId, _distributedLockProvider);
        await sut.Lock();

        // when
        await sut.ReleaseIfHeld();

        // then: disposing the lease releases it (ReleaseOnDispose) and stops the auto-extend monitor
        await distributedLock.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task should_noop_release_when_not_held()
    {
        // given
        const string fileId = "test-file";
        _distributedLockProvider
            .TryAcquireAsync(Arg.Any<string>(), Arg.Any<DistributedLockAcquireOptions?>(), Arg.Any<CancellationToken>())
            .Returns((IDistributedLease?)null);

        await using var sut = new DistributedLockTusFileLock(fileId, _distributedLockProvider);
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
