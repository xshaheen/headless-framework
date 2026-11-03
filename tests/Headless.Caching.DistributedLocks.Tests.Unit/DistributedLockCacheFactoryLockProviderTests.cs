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
        await provider.TryAcquireAsync("default", "users:42", TimeSpan.FromSeconds(1), AbortToken);

        // then
        await _distributedLock
            .Received(1)
            .TryAcquireAsync("cache:factory:default:users:42", Arg.Any<DistributedLockAcquireOptions>(), AbortToken);
    }

    [Fact]
    public async Task should_map_key_to_custom_prefixed_resource()
    {
        // given
        var provider = _CreateProvider(new CacheFactoryLockOptions { ResourcePrefix = "myapp:locks:" });
        _SetupAcquire(lease: Substitute.For<IDistributedLease>());

        // when
        await provider.TryAcquireAsync("default", "users:42", TimeSpan.FromSeconds(1), AbortToken);

        // then
        await _distributedLock
            .Received(1)
            .TryAcquireAsync("myapp:locks:default:users:42", Arg.Any<DistributedLockAcquireOptions>(), AbortToken);
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
        await provider.TryAcquireAsync("default", "key", timeout, AbortToken);

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
        var releaser = await provider.TryAcquireAsync("default", "key", TimeSpan.Zero, AbortToken);

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
        var releaser = await provider.TryAcquireAsync("default", "key", TimeSpan.FromSeconds(1), AbortToken);
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
        await provider.TryAcquireAsync("default", "key", TimeSpan.FromSeconds(1), AbortToken);

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
        await provider.TryAcquireAsync("default", "key", TimeSpan.FromSeconds(1), AbortToken);

        // then — null TimeUntilExpires defers to the lock provider's own default lease duration
        capturedOptions.Should().NotBeNull();
        capturedOptions.TimeUntilExpires.Should().BeNull();
    }

    [Fact]
    public async Task should_bound_resource_name_for_key_longer_than_lock_name_limit()
    {
        // given
        var provider = _CreateProvider();
        var longKey = new string('k', 2_000);
        string? resource = null;
        _distributedLock
            .TryAcquireAsync(
                Arg.Do<string>(name => resource = name),
                Arg.Any<DistributedLockAcquireOptions?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.FromResult<IDistributedLease?>(Substitute.For<IDistributedLease>()));

        // when
        await provider.TryAcquireAsync("default", longKey, TimeSpan.FromSeconds(1), AbortToken);

        // then
        resource.Should().NotBeNull();
        resource.Length.Should().BeLessThanOrEqualTo(255);
        resource.Should().StartWith("cache:factory:sha256:");
    }

    [Fact]
    public void should_map_each_long_key_to_a_stable_distinct_resource()
    {
        // given
        var key = new string('k', 2_000);
        var otherKey = key[..^1] + "x";

        // when
        var first = DistributedLockCacheFactoryLockProvider.BuildResource("cache:factory:", "default", key);
        var second = DistributedLockCacheFactoryLockProvider.BuildResource("cache:factory:", "default", key);
        var other = DistributedLockCacheFactoryLockProvider.BuildResource("cache:factory:", "default", otherKey);

        // then
        second.Should().Be(first);
        other.Should().NotBe(first);
    }

    [Fact]
    public void should_keep_resources_of_two_cache_instances_apart()
    {
        // when
        var shortA = DistributedLockCacheFactoryLockProvider.BuildResource("cache:factory:", "a", "users:42");
        var shortB = DistributedLockCacheFactoryLockProvider.BuildResource("cache:factory:", "b", "users:42");
        var longKey = new string('k', 2_000);
        var longA = DistributedLockCacheFactoryLockProvider.BuildResource("cache:factory:", "a", longKey);
        var longB = DistributedLockCacheFactoryLockProvider.BuildResource("cache:factory:", "b", longKey);

        // then
        shortA.Should().Be("cache:factory:a:users:42");
        shortB.Should().Be("cache:factory:b:users:42");
        longB.Should().NotBe(longA);
    }

    [Fact]
    public void should_keep_digest_form_within_bound_for_longest_allowed_prefix()
    {
        // given
        var prefix = new string('p', DistributedLockCacheFactoryLockProvider.MaxResourcePrefixLength);

        // when
        var resource = DistributedLockCacheFactoryLockProvider.BuildResource(prefix, "default", new string('k', 500));

        // then
        resource.Should().HaveLength(DistributedLockCacheFactoryLockProvider.MaxReadableResourceLength);
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
