// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.DistributedLocks;
using Headless.DistributedLocks.Cache;
using Headless.Redis;
using Headless.Serializer;
using Headless.Testing.Tests;
using Tests.TestSetup;

namespace Tests;

[Collection<CacheTestFixture>]
public sealed class CacheResourceLockStorageTests(CacheTestFixture fixture) : TestBase
{
    private readonly CacheResourceLockStorage _storage = new(
        new RedisCache(
            new SystemJsonSerializer(),
            TimeProvider.System,
            new RedisCacheOptions { ConnectionMultiplexer = fixture.ConnectionMultiplexer },
            fixture.ScriptsLoader
        )
    );

    public override async ValueTask InitializeAsync()
    {
        await fixture.ConnectionMultiplexer.FlushAllAsync();
    }

    [Fact]
    public async Task should_insert_lock()
    {
        // given
        var key = Faker.Random.AlphaNumeric(10);
        var lockId = Guid.NewGuid().ToString("N");

        // when
        var result = await _storage.InsertAsync(key, lockId, TimeSpan.FromMinutes(5));

        // then
        result.Should().BeTrue();
        var storedId = await _storage.GetAsync(key);
        storedId.Should().Be(lockId);
    }

    [Fact]
    public async Task should_not_insert_when_exists()
    {
        // given
        var key = Faker.Random.AlphaNumeric(10);
        var originalLockId = Guid.NewGuid().ToString("N");
        var newLockId = Guid.NewGuid().ToString("N");
        await _storage.InsertAsync(key, originalLockId, TimeSpan.FromMinutes(5));

        // when
        var result = await _storage.InsertAsync(key, newLockId, TimeSpan.FromMinutes(5));

        // then
        result.Should().BeFalse();
        var storedId = await _storage.GetAsync(key);
        storedId.Should().Be(originalLockId);
    }

    [Fact]
    public async Task should_remove_lock()
    {
        // given
        var key = Faker.Random.AlphaNumeric(10);
        var lockId = Guid.NewGuid().ToString("N");
        await _storage.InsertAsync(key, lockId, TimeSpan.FromMinutes(5));

        // when
        var result = await _storage.RemoveIfEqualAsync(key, lockId);

        // then
        result.Should().BeTrue();
        var exists = await _storage.ExistsAsync(key);
        exists.Should().BeFalse();
    }

    [Fact]
    public async Task should_not_remove_when_different_id()
    {
        // given
        var key = Faker.Random.AlphaNumeric(10);
        var lockId = Guid.NewGuid().ToString("N");
        var wrongId = Guid.NewGuid().ToString("N");
        await _storage.InsertAsync(key, lockId, TimeSpan.FromMinutes(5));

        // when
        var result = await _storage.RemoveIfEqualAsync(key, wrongId);

        // then
        result.Should().BeFalse();
        var exists = await _storage.ExistsAsync(key);
        exists.Should().BeTrue();
    }

    [Fact]
    public async Task should_expire_after_ttl()
    {
        // given
        var key = Faker.Random.AlphaNumeric(10);
        var lockId = Guid.NewGuid().ToString("N");
        await _storage.InsertAsync(key, lockId, TimeSpan.FromMilliseconds(100));

        // when
        await Task.Delay(200, AbortToken);

        // then
        var exists = await _storage.ExistsAsync(key);
        exists.Should().BeFalse();
    }

    [Fact]
    public async Task should_get_lock_id()
    {
        // given
        var key = Faker.Random.AlphaNumeric(10);
        var lockId = Guid.NewGuid().ToString("N");
        await _storage.InsertAsync(key, lockId, TimeSpan.FromMinutes(5));

        // when
        var result = await _storage.GetAsync(key);

        // then
        result.Should().Be(lockId);
    }

    [Fact]
    public async Task should_check_exists()
    {
        // given
        var key = Faker.Random.AlphaNumeric(10);
        var lockId = Guid.NewGuid().ToString("N");

        // when - key not exists
        var resultBefore = await _storage.ExistsAsync(key);

        // then
        resultBefore.Should().BeFalse();

        // when - key exists
        await _storage.InsertAsync(key, lockId, TimeSpan.FromMinutes(5));
        var resultAfter = await _storage.ExistsAsync(key);

        // then
        resultAfter.Should().BeTrue();
    }

    [Fact]
    public async Task should_get_expiration()
    {
        // given
        var key = Faker.Random.AlphaNumeric(10);
        var lockId = Guid.NewGuid().ToString("N");
        var ttl = TimeSpan.FromMinutes(5);
        await _storage.InsertAsync(key, lockId, ttl);

        // when
        var result = await _storage.GetExpirationAsync(key);

        // then
        result.Should().NotBeNull();
        result!.Value.TotalMinutes.Should().BeGreaterThan(4);
        result.Value.TotalMinutes.Should().BeLessThanOrEqualTo(5);
    }
}
