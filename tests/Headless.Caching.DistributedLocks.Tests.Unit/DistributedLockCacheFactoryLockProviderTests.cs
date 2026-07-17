// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.DistributedLocks;
using Headless.Testing.Tests;

namespace Tests;

public sealed class DistributedLockCacheFactoryLockProviderTests : TestBase
{
    private readonly IDistributedLock _distributedLock = Substitute.For<IDistributedLock>();

    [Fact]
    public async Task should_map_key_to_default_prefixed_resource()
    {
        // given
        var provider = _CreateProvider();
        _SetupAcquire(lease: Substitute.For<IDistributedLease>());

        // when
        await provider.TryAcquireAsync("users:42", TimeSpan.FromSeconds(1), AbortToken);

        // then
        await _distributedLock
            .Received(1)
            .TryAcquireAsync("cache:factory:users:42", Arg.Any<DistributedLockAcquireOptions>(), AbortToken);
    }

    [Fact]
    public async Task should_map_key_to_custom_prefixed_resource()
    {
        // given
        var provider = _CreateProvider(new CacheFactoryLockOptions { ResourcePrefix = "myapp:locks:" });
        _SetupAcquire(lease: Substitute.For<IDistributedLease>());

        // when
        await provider.TryAcquireAsync("users:42", TimeSpan.FromSeconds(1), AbortToken);

        // then
        await _distributedLock
            .Received(1)
            .TryAcquireAsync("myapp:locks:users:42", Arg.Any<DistributedLockAcquireOptions>(), AbortToken);
    }

    [Theory]
    [InlineData(0L)] // TimeSpan.Zero -> single try-once attempt
    [InlineData(-1L)] // Timeout.InfiniteTimeSpan -> unbounded wait
    [InlineData(2_500L)] // finite -> bounded wait
    public async Task should_map_seam_timeout_to_acquire_timeout(long timeoutMilliseconds)
    {
        // given
        var timeout = TimeSpan.FromMilliseconds(timeoutMilliseconds);
        var provider = _CreateProvider();
        DistributedLockAcquireOptions? capturedOptions = null;
        _distributedLock
            .TryAcquireAsync(
                Arg.Any<string>(),
                Arg.Do<DistributedLockAcquireOptions?>(options => capturedOptions = options),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.FromResult<IDistributedLease?>(Substitute.For<IDistributedLease>()));

        // when
        await provider.TryAcquireAsync("key", timeout, AbortToken);

        // then
        capturedOptions.Should().NotBeNull();
        capturedOptions.AcquireTimeout.Should().Be(timeout);
    }

    [Fact]
    public async Task should_return_null_releaser_when_lock_not_acquired()
    {
        // given
        var provider = _CreateProvider();
        _SetupAcquire(lease: null);

        // when
        var releaser = await provider.TryAcquireAsync("key", TimeSpan.Zero, AbortToken);

        // then
        releaser.Should().BeNull();
    }

    [Fact]
    public async Task should_dispose_lease_when_releaser_is_disposed()
    {
        // given
        var lease = Substitute.For<IDistributedLease>();
        var provider = _CreateProvider();
        _SetupAcquire(lease);

        // when
        var releaser = await provider.TryAcquireAsync("key", TimeSpan.FromSeconds(1), AbortToken);
        await releaser!.DisposeAsync();

        // then
        await lease.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task should_forward_time_until_expires_when_configured()
    {
        // given
        var timeUntilExpires = TimeSpan.FromMinutes(3);
        var provider = _CreateProvider(new CacheFactoryLockOptions { TimeUntilExpires = timeUntilExpires });
        DistributedLockAcquireOptions? capturedOptions = null;
        _distributedLock
            .TryAcquireAsync(
                Arg.Any<string>(),
                Arg.Do<DistributedLockAcquireOptions?>(options => capturedOptions = options),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.FromResult<IDistributedLease?>(Substitute.For<IDistributedLease>()));

        // when
        await provider.TryAcquireAsync("key", TimeSpan.FromSeconds(1), AbortToken);

        // then
        capturedOptions.Should().NotBeNull();
        capturedOptions.TimeUntilExpires.Should().Be(timeUntilExpires);
    }

    [Fact]
    public async Task should_leave_lease_ttl_at_lock_default_when_not_configured()
    {
        // given
        var provider = _CreateProvider();
        DistributedLockAcquireOptions? capturedOptions = null;
        _distributedLock
            .TryAcquireAsync(
                Arg.Any<string>(),
                Arg.Do<DistributedLockAcquireOptions?>(options => capturedOptions = options),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.FromResult<IDistributedLease?>(Substitute.For<IDistributedLease>()));

        // when
        await provider.TryAcquireAsync("key", TimeSpan.FromSeconds(1), AbortToken);

        // then — null TimeUntilExpires defers to the lock provider's own default lease duration
        capturedOptions.Should().NotBeNull();
        capturedOptions.TimeUntilExpires.Should().BeNull();
    }

    private DistributedLockCacheFactoryLockProvider _CreateProvider(CacheFactoryLockOptions? options = null)
    {
        return new(_distributedLock, options ?? new CacheFactoryLockOptions());
    }

    private void _SetupAcquire(IDistributedLease? lease)
    {
        _distributedLock
            .TryAcquireAsync(Arg.Any<string>(), Arg.Any<DistributedLockAcquireOptions?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(lease));
    }
}
