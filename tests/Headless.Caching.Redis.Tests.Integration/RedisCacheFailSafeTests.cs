// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using StackExchange.Redis;

namespace Tests;

/// <summary>
/// Integration tests for Redis fail-safe behavior (U6 coverage).
/// Uses real durations in the 1–3 s range so waits are short but Redis TTLs are observable.
/// </summary>
public sealed class RedisCacheFailSafeTests(RedisCacheFixture fixture) : RedisCacheTestBase(fixture)
{
    // ---------- helpers ----------

    private IDatabase Database => Fixture.ConnectionMultiplexer.GetDatabase();

    private static CacheEntryOptions _FailSafeOptions(
        TimeSpan? duration = null,
        TimeSpan? maxDuration = null,
        TimeSpan? throttleDuration = null
    )
    {
        return new()
        {
            Duration = duration ?? TimeSpan.FromSeconds(1),
            IsFailSafeEnabled = true,
            FailSafeMaxDuration = maxDuration ?? TimeSpan.FromSeconds(3),
            FailSafeThrottleDuration = throttleDuration ?? TimeSpan.FromSeconds(1),
        };
    }

    private async Task _AdvanceAsync(TimeSpan duration)
    {
        // Integration tests run against the real wall clock; advance by actually waiting.
        await TimeProvider.System.Delay(duration, AbortToken);
    }

    // ---------- tests ----------

    /// <summary>
    /// R1 — within the physical window, a factory exception causes the stale value to be returned with IsStale==true.
    /// </summary>
    [Fact]
    public async Task should_serve_stale_value_when_factory_throws_within_physical_window()
    {
        // given
        await FlushAsync();
        using var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        var options = _FailSafeOptions();

        await cache.GetOrAddAsync(key, _ => ValueTask.FromResult<string?>("original"), options, AbortToken);

        // advance past logical but still within physical window
        await _AdvanceAsync(options.Duration + TimeSpan.FromMilliseconds(250));

        // when
        var result = await cache.GetOrAddAsync<string>(
            key,
            _ => throw new InvalidOperationException("downstream unavailable"),
            options,
            AbortToken
        );

        // then
        result.HasValue.Should().BeTrue();
        result.Value.Should().Be("original");
        result.IsStale.Should().BeTrue();
    }

    /// <summary>
    /// R4 — a factory that itself throws a StackExchange.Redis exception (store-unavailable shaped failure)
    /// is treated the same as any other factory exception: stale value is served with IsStale==true.
    /// </summary>
    [Fact]
    public async Task should_serve_stale_value_when_factory_throws_redis_exception()
    {
        // given
        await FlushAsync();
        using var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        var options = _FailSafeOptions();

        await cache.GetOrAddAsync(key, _ => ValueTask.FromResult<string?>("redis-safe"), options, AbortToken);

        // advance past logical but within physical window
        await _AdvanceAsync(options.Duration + TimeSpan.FromMilliseconds(250));

        // when – factory throws a RedisException (simulates e.g. a downstream Redis call in the factory)
        var result = await cache.GetOrAddAsync<string>(
            key,
            _ => throw new RedisException("simulated Redis connection failure"),
            options,
            AbortToken
        );

        // then
        result.HasValue.Should().BeTrue();
        result.Value.Should().Be("redis-safe");
        result.IsStale.Should().BeTrue();
    }

    /// <summary>
    /// R3/physical-TTL — after a fail-safe GetOrAddAsync the Redis key TTL is set to the PHYSICAL duration
    /// (max(Duration, FailSafeMaxDuration)), NOT just Duration.
    /// </summary>
    [Fact]
    public async Task should_set_redis_ttl_to_physical_when_failsafe_enabled()
    {
        // given
        await FlushAsync();
        var key = Faker.Random.AlphaNumeric(10);
        var options = new CacheEntryOptions
        {
            Duration = TimeSpan.FromSeconds(1),
            IsFailSafeEnabled = true,
            FailSafeMaxDuration = TimeSpan.FromSeconds(3),
            FailSafeThrottleDuration = TimeSpan.FromSeconds(1),
        };

        using var cache = CreateCache(key + ":");

        // when
        await cache.GetOrAddAsync(key, _ => ValueTask.FromResult<string?>("v"), options, AbortToken);

        // then – Redis TTL should be close to FailSafeMaxDuration (the physical window), not Duration
        var ttl = await Database.KeyTimeToLiveAsync(key + ":" + key);
        ttl.Should().NotBeNull();
        // physical = max(Duration=1s, FailSafeMaxDuration=3s) = 3s
        ttl.Value.Should().BeGreaterThan(TimeSpan.FromSeconds(1.5));
        ttl.Value.Should().BeLessThanOrEqualTo(options.FailSafeMaxDuration + TimeSpan.FromSeconds(2));
    }

    /// <summary>
    /// R7 — when fail-safe is disabled (default), the Redis TTL matches Duration exactly.
    /// </summary>
    [Fact]
    public async Task should_set_redis_ttl_to_duration_when_failsafe_disabled()
    {
        // given
        await FlushAsync();
        var key = Faker.Random.AlphaNumeric(10);
        var duration = TimeSpan.FromSeconds(5);

        using var cache = CreateCache(key + ":");

        // when
        await cache.GetOrAddAsync(
            key,
            _ => ValueTask.FromResult<string?>("v"),
            CacheEntryOptions.FromTimeSpan(duration),
            AbortToken
        );

        // then – TTL should be close to Duration (no fail-safe inflation)
        var ttl = await Database.KeyTimeToLiveAsync(key + ":" + key);
        ttl.Should().NotBeNull();
        ttl.Value.Should().BeCloseTo(duration, TimeSpan.FromSeconds(2));
    }

    /// <summary>
    /// KTD-5 — GetAsync on a logically-expired (but physically retained) fail-safe key returns NoValue/miss.
    /// </summary>
    [Fact]
    public async Task should_return_no_value_from_get_when_entry_is_logically_expired()
    {
        // given
        await FlushAsync();
        using var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        var options = _FailSafeOptions();

        await cache.GetOrAddAsync(key, _ => ValueTask.FromResult<string?>("logical-expired"), options, AbortToken);

        // advance past logical expiry but within physical window
        await _AdvanceAsync(options.Duration + TimeSpan.FromMilliseconds(250));

        // when – direct GetAsync bypasses the coordinator and sees the frame's logical expiry
        var result = await cache.GetAsync<string>(key, AbortToken);

        // then – the value must NOT be visible via the plain GetAsync path
        result.HasValue.Should().BeFalse();
    }

    /// <summary>
    /// R2 — once the physical window has elapsed (key gone from Redis), a factory exception propagates
    /// without activating fail-safe.
    /// </summary>
    [Fact]
    public async Task should_propagate_exception_when_factory_throws_beyond_physical_window()
    {
        // given
        await FlushAsync();
        using var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        var options = _FailSafeOptions(
            duration: TimeSpan.FromMilliseconds(500),
            maxDuration: TimeSpan.FromMilliseconds(800),
            throttleDuration: TimeSpan.FromMilliseconds(300)
        );

        await cache.GetOrAddAsync(key, _ => ValueTask.FromResult<string?>("gone"), options, AbortToken);

        // advance past physical window so the Redis key has expired
        await _AdvanceAsync(options.FailSafeMaxDuration + TimeSpan.FromMilliseconds(400));

        // when
        var act = async () =>
            await cache.GetOrAddAsync<string>(
                key,
                _ => throw new InvalidOperationException("downstream gone"),
                options,
                AbortToken
            );

        // then – no stale reserve: exception must propagate
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("downstream gone");
    }

    /// <summary>
    /// ExistsAsync — a logically-expired (but physically retained) fail-safe entry must report as absent.
    /// </summary>
    [Fact]
    public async Task should_return_false_from_exists_when_entry_is_logically_expired()
    {
        // given
        await FlushAsync();
        using var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        var options = _FailSafeOptions();

        await cache.GetOrAddAsync(key, _ => ValueTask.FromResult<string?>("hidden"), options, AbortToken);

        // confirm it exists before logical expiry
        var beforeExpiry = await cache.ExistsAsync(key, AbortToken);

        // advance past logical expiry but remain within physical window
        await _AdvanceAsync(options.Duration + TimeSpan.FromMilliseconds(250));

        // when
        var afterLogicalExpiry = await cache.ExistsAsync(key, AbortToken);

        // then
        beforeExpiry.Should().BeTrue();
        afterLogicalExpiry.Should().BeFalse();
    }

    /// <summary>
    /// GetExpirationAsync — before logical expiry it returns the remaining logical lifetime;
    /// after logical expiry it returns null (even though the physical key is still in Redis).
    /// </summary>
    [Fact]
    public async Task should_report_logical_remaining_from_get_expiration_for_failsafe_entry()
    {
        // given
        await FlushAsync();
        using var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        var options = _FailSafeOptions(
            duration: TimeSpan.FromSeconds(2),
            maxDuration: TimeSpan.FromSeconds(5),
            throttleDuration: TimeSpan.FromSeconds(1)
        );

        await cache.GetOrAddAsync(key, _ => ValueTask.FromResult<string?>("expiry-test"), options, AbortToken);

        // when – immediately after insertion the expiration should reflect the logical duration
        var beforeExpiry = await cache.GetExpirationAsync(key, AbortToken);

        // advance past logical expiry but within physical window
        await _AdvanceAsync(options.Duration + TimeSpan.FromMilliseconds(250));

        var afterLogicalExpiry = await cache.GetExpirationAsync(key, AbortToken);

        // then
        beforeExpiry.Should().NotBeNull();
        // Should be close to the logical Duration (2s), NOT the physical (5s)
        beforeExpiry.Value.Should().BeLessThanOrEqualTo(options.Duration + TimeSpan.FromSeconds(1));
        beforeExpiry.Value.Should().BeGreaterThan(TimeSpan.Zero);

        // After logical expiry GetExpirationAsync must return null
        afterLogicalExpiry.Should().BeNull();
    }
}
