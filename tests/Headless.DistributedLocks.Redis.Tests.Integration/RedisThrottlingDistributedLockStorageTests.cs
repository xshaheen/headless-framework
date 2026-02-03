// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks.Redis;
using Headless.Redis;
using Headless.Testing.Tests;

namespace Tests;

/// <summary>
/// Integration tests for <see cref="RedisThrottlingDistributedLockStorage"/>.
/// Tests verify Redis-specific behaviors: INCRBY with expiration via Lua script.
/// </summary>
[Collection<RedisTestFixture>]
public sealed class RedisThrottlingDistributedLockStorageTests(RedisTestFixture fixture) : TestBase
{
    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await fixture.ConnectionMultiplexer.FlushAllAsync();
    }

    #region IncrementAsync

    [Fact]
    public async Task should_increment_and_return_new_value()
    {
        // given
        var resource = $"throttle:{Faker.Random.AlphaNumeric(10)}";
        var ttl = TimeSpan.FromMinutes(5);

        // when
        var result1 = await fixture.ThrottlingLockStorage.IncrementAsync(resource, ttl);
        var result2 = await fixture.ThrottlingLockStorage.IncrementAsync(resource, ttl);
        var result3 = await fixture.ThrottlingLockStorage.IncrementAsync(resource, ttl);

        // then
        result1.Should().Be(1);
        result2.Should().Be(2);
        result3.Should().Be(3);
    }

    [Fact]
    public async Task should_set_expiration_on_first_increment()
    {
        // given
        var resource = $"throttle:{Faker.Random.AlphaNumeric(10)}";
        var ttl = TimeSpan.FromMinutes(5);

        // when
        await fixture.ThrottlingLockStorage.IncrementAsync(resource, ttl);

        // then
        var db = fixture.ConnectionMultiplexer.GetDatabase();
        var expiration = await db.KeyTimeToLiveAsync(resource);
        expiration.Should().NotBeNull();
        expiration!.Value.TotalMinutes.Should().BeGreaterThan(4);
        expiration.Value.TotalMinutes.Should().BeLessThanOrEqualTo(5);
    }

    [Fact]
    public async Task should_expire_counter_after_ttl()
    {
        // given
        var resource = $"throttle:{Faker.Random.AlphaNumeric(10)}";
        var ttl = TimeSpan.FromMilliseconds(100);
        await fixture.ThrottlingLockStorage.IncrementAsync(resource, ttl);

        // when
        await Task.Delay(200);

        // then
        var count = await fixture.ThrottlingLockStorage.GetHitCountsAsync(resource);
        count.Should().Be(0);
    }

    [Fact]
    public async Task should_increment_atomically_under_concurrent_access()
    {
        // given
        var resource = $"throttle:{Faker.Random.AlphaNumeric(10)}";
        var ttl = TimeSpan.FromMinutes(5);
        const int concurrentIncrements = 100;

        // when
        await Parallel.ForEachAsync(
            Enumerable.Range(0, concurrentIncrements),
            new ParallelOptions { MaxDegreeOfParallelism = 50 },
            async (_, _) => await fixture.ThrottlingLockStorage.IncrementAsync(resource, ttl)
        );

        // then
        var count = await fixture.ThrottlingLockStorage.GetHitCountsAsync(resource);
        count.Should().Be(concurrentIncrements);
    }

    #endregion

    #region GetHitCountsAsync

    [Fact]
    public async Task should_return_zero_when_key_not_exists()
    {
        // given
        var resource = $"throttle:{Faker.Random.AlphaNumeric(10)}";

        // when
        var count = await fixture.ThrottlingLockStorage.GetHitCountsAsync(resource);

        // then
        count.Should().Be(0);
    }

    [Fact]
    public async Task should_return_current_hit_count()
    {
        // given
        var resource = $"throttle:{Faker.Random.AlphaNumeric(10)}";
        var ttl = TimeSpan.FromMinutes(5);
        await fixture.ThrottlingLockStorage.IncrementAsync(resource, ttl);
        await fixture.ThrottlingLockStorage.IncrementAsync(resource, ttl);
        await fixture.ThrottlingLockStorage.IncrementAsync(resource, ttl);

        // when
        var count = await fixture.ThrottlingLockStorage.GetHitCountsAsync(resource);

        // then
        count.Should().Be(3);
    }

    #endregion

    #region TTL Refresh Behavior

    [Fact]
    public async Task should_refresh_ttl_on_subsequent_increments()
    {
        // given
        var resource = $"throttle:{Faker.Random.AlphaNumeric(10)}";
        var ttl = TimeSpan.FromSeconds(5);

        // when - increment and then check TTL was refreshed
        await fixture.ThrottlingLockStorage.IncrementAsync(resource, ttl);
        await Task.Delay(100); // Wait a bit
        await fixture.ThrottlingLockStorage.IncrementAsync(resource, ttl);

        // then - TTL should be close to original (refreshed)
        var db = fixture.ConnectionMultiplexer.GetDatabase();
        var expiration = await db.KeyTimeToLiveAsync(resource);
        expiration.Should().NotBeNull();
        expiration!.Value.TotalSeconds.Should().BeGreaterThan(4);
    }

    #endregion
}
