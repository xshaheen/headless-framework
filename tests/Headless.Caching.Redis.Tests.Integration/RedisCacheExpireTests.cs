// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using StackExchange.Redis;

namespace Tests;

/// <summary>
/// Integration tests for <see cref="RedisCache.ExpireAsync"/> — logical expiration that preserves the fail-safe
/// reserve (and the Redis key TTL) for a fail-safe entry, but removes a non-fail-safe entry outright.
/// </summary>
public sealed class RedisCacheExpireTests(RedisCacheFixture fixture) : RedisCacheTestBase(fixture)
{
    private IDatabase Database => Fixture.ConnectionMultiplexer.GetDatabase();

    private static CacheEntryOptions _FailSafeOptions()
    {
        return new()
        {
            Duration = TimeSpan.FromSeconds(1),
            IsFailSafeEnabled = true,
            FailSafeMaxDuration = TimeSpan.FromSeconds(5),
            FailSafeThrottleDuration = TimeSpan.FromSeconds(1),
        };
    }

    [Fact]
    public async Task should_preserve_reserve_and_ttl_when_expiring_a_failsafe_entry()
    {
        // given — a fail-safe entry under an isolated key prefix so the raw Redis key is addressable
        await FlushAsync();
        var key = Faker.Random.AlphaNumeric(10);
        var prefix = key + ":";
        var redisKey = prefix + key;
        using var cache = CreateCache(prefix);
        var options = _FailSafeOptions();

        await cache.GetOrAddAsync(key, _ => ValueTask.FromResult<string?>("original"), options, AbortToken);

        // when
        var expired = await cache.ExpireAsync(key, AbortToken);

        // then — ExpireAsync succeeds and a plain read now misses
        expired.Should().BeTrue();
        (await cache.GetAsync<string>(key, AbortToken)).HasValue.Should().BeFalse();

        // and — the physical Redis key is preserved (reserve TTL kept)
        (await Database.KeyExistsAsync(redisKey))
            .Should()
            .BeTrue("the physical reserve key must survive logical expiry");
        (await Database.KeyTimeToLiveAsync(redisKey)).Should().NotBeNull();

        // and — a failing fail-safe factory still serves the stale value from the preserved reserve
        var result = await cache.GetOrAddAsync<string>(
            key,
            _ => throw new InvalidOperationException("downstream unavailable"),
            options,
            AbortToken
        );
        result.HasValue.Should().BeTrue();
        result.Value.Should().Be("original");
        result.IsStale.Should().BeTrue();
    }

    [Fact]
    public async Task should_remove_redis_key_when_expiring_a_non_failsafe_entry()
    {
        // given — a plain (non-fail-safe) entry: Physical == Logical, no reserve to keep
        await FlushAsync();
        var key = Faker.Random.AlphaNumeric(10);
        var prefix = key + ":";
        var redisKey = prefix + key;
        using var cache = CreateCache(prefix);

        await cache.GetOrAddAsync(
            key,
            _ => ValueTask.FromResult<string?>("v"),
            CacheEntryOptions.FromTimeSpan(TimeSpan.FromMinutes(5)),
            AbortToken
        );

        // when
        var expired = await cache.ExpireAsync(key, AbortToken);

        // then — succeeds, read misses, and the Redis key is gone outright
        expired.Should().BeTrue();
        (await cache.GetAsync<string>(key, AbortToken)).HasValue.Should().BeFalse();
        (await Database.KeyExistsAsync(redisKey)).Should().BeFalse("a non-fail-safe entry is removed, not retained");
    }

    [Fact]
    public async Task should_remove_redis_key_when_expiring_a_sliding_entry()
    {
        // given — a sliding entry whose absolute Duration cap exceeds the idle window (Physical > Logical),
        // but that surplus is the sliding cap, NOT a fail-safe reserve, so it must collapse to a removal.
        await FlushAsync();
        var key = Faker.Random.AlphaNumeric(10);
        var prefix = key + ":";
        var redisKey = prefix + key;
        using var cache = CreateCache(prefix);

        var options = new CacheEntryOptions
        {
            Duration = TimeSpan.FromMinutes(10),
            SlidingExpiration = TimeSpan.FromMinutes(1),
        };

        await cache.GetOrAddAsync(key, _ => ValueTask.FromResult<string?>("v"), options, AbortToken);

        // when
        var expired = await cache.ExpireAsync(key, AbortToken);

        // then — succeeds, read misses, and the Redis key is gone (no phantom reserve manufactured)
        expired.Should().BeTrue();
        (await cache.GetAsync<string>(key, AbortToken)).HasValue.Should().BeFalse();
        (await Database.KeyExistsAsync(redisKey)).Should().BeFalse("a sliding entry is removed outright, not retained");
    }

    [Fact]
    public async Task should_return_false_when_expiring_an_absent_key()
    {
        // given
        await FlushAsync();
        using var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);

        // when
        var expired = await cache.ExpireAsync(key, AbortToken);

        // then
        expired.Should().BeFalse();
    }

    /// <summary>
    /// Finding #3 — return contract: ExpireAsync returns the actual StringSet(When.Exists) outcome on the
    /// fail-safe re-stamp branch (true when the key still exists and is logically expired in place), rather
    /// than the previous unconditional true.
    /// </summary>
    [Fact]
    public async Task should_return_true_and_logically_expire_an_existing_failsafe_key()
    {
        // given — an existing fail-safe entry whose physical reserve outlives its logical lifetime
        await FlushAsync();
        var key = Faker.Random.AlphaNumeric(10);
        var prefix = key + ":";
        var redisKey = prefix + key;
        using var cache = CreateCache(prefix);

        await cache.GetOrAddAsync(key, _ => ValueTask.FromResult<string?>("v"), _FailSafeOptions(), AbortToken);

        // when — the key exists, so the When.Exists StringSet succeeds
        var expired = await cache.ExpireAsync(key, AbortToken);

        // then — true (from the successful StringSet), the entry is logically expired, but the reserve survives
        expired.Should().BeTrue();
        (await cache.GetAsync<string>(key, AbortToken)).HasValue.Should().BeFalse();
        (await cache.GetExpirationAsync(key, AbortToken)).Should().BeNull();
        (await Database.KeyExistsAsync(redisKey)).Should().BeTrue();
    }
}
