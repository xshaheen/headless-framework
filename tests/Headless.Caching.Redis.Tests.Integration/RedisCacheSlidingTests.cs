// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;

namespace Tests;

public sealed class RedisCacheSlidingTests(RedisCacheFixture fixture) : RedisCacheTestBase(fixture)
{
    [Fact]
    public async Task should_rearm_ttl_on_value_read_and_keep_value_after_original_logical_deadline()
    {
        await FlushAsync();
        using var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        var options = new CacheEntryOptions
        {
            Duration = TimeSpan.FromMilliseconds(900),
            SlidingExpiration = TimeSpan.FromMilliseconds(300),
        };

        await cache.GetOrAddAsync(key, _ => ValueTask.FromResult<string?>("value"), options, AbortToken);
        // real-wait: the sliding window is the key's Redis TTL, re-armed and expired by the Redis server clock, which no app clock can advance
        await TimeProvider.System.Delay(TimeSpan.FromMilliseconds(225), AbortToken);

        var firstRead = await cache.GetAsync<string>(key, AbortToken);
        var ttlAfterRearm = await Fixture.ConnectionMultiplexer.GetDatabase().KeyTimeToLiveAsync(key);

        // real-wait: the sliding window is the key's Redis TTL, re-armed and expired by the Redis server clock, which no app clock can advance
        await TimeProvider.System.Delay(TimeSpan.FromMilliseconds(150), AbortToken);
        var afterOriginalLogicalDeadline = await cache.GetAsync<string>(key, AbortToken);

        firstRead.Value.Should().Be("value");
        ttlAfterRearm.Should().NotBeNull();
        ttlAfterRearm!.Value.Should().BeCloseTo(options.SlidingExpiration.Value, TimeSpan.FromMilliseconds(150));
        afterOriginalLogicalDeadline.Value.Should().Be("value");
    }

    [Fact]
    public async Task should_not_rearm_ttl_on_metadata_read()
    {
        await FlushAsync();
        using var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        var options = new CacheEntryOptions
        {
            Duration = TimeSpan.FromMilliseconds(900),
            SlidingExpiration = TimeSpan.FromMilliseconds(300),
        };

        await cache.GetOrAddAsync(key, _ => ValueTask.FromResult<string?>("value"), options, AbortToken);
        // real-wait: the sliding window is the key's Redis TTL, re-armed and expired by the Redis server clock, which no app clock can advance
        await TimeProvider.System.Delay(TimeSpan.FromMilliseconds(225), AbortToken);

        var ttlBefore = await Fixture.ConnectionMultiplexer.GetDatabase().KeyTimeToLiveAsync(key);
        var exists = await cache.ExistsAsync(key, AbortToken);
        var expiration = await cache.GetExpirationAsync(key, AbortToken);
        var ttlAfter = await Fixture.ConnectionMultiplexer.GetDatabase().KeyTimeToLiveAsync(key);

        exists.Should().BeTrue();
        expiration.Should().NotBeNull();
        ttlBefore.Should().NotBeNull();
        ttlAfter.Should().NotBeNull();
        ttlAfter!.Value.Should().BeLessThan(ttlBefore!.Value + TimeSpan.FromMilliseconds(50));
    }

    // Whole-second timings: each keep-alive read must reach Redis inside the sliding window to re-arm it, and under
    // parallel-suite load a sub-second window leaves too little slack, so the key lapses on its sliding window before
    // the absolute cap this test is about. Every keep-alive read sits 1s inside its window, and the final read lands
    // past the 4s cap while the re-armed sliding window is still open, so only the cap can expire the entry.
    [Fact]
    public async Task should_expire_at_absolute_duration_cap_even_with_repeated_reads()
    {
        await FlushAsync();
        using var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        var options = new CacheEntryOptions
        {
            Duration = TimeSpan.FromSeconds(4),
            SlidingExpiration = TimeSpan.FromSeconds(2),
        };

        await cache.GetOrAddAsync(key, _ => ValueTask.FromResult<string?>("value"), options, AbortToken);

        for (var i = 0; i < 3; i++)
        {
            // real-wait: the sliding window is the key's Redis TTL, re-armed and expired by the Redis server clock, which no app clock can advance
            await TimeProvider.System.Delay(TimeSpan.FromSeconds(1), AbortToken);
            (await cache.GetAsync<string>(key, AbortToken)).HasValue.Should().BeTrue();
        }

        // 1.5s after the last keep-alive read: inside the re-armed sliding window (2s), past the 4s cap.
        // real-wait: the sliding window is the key's Redis TTL, re-armed and expired by the Redis server clock, which no app clock can advance
        await TimeProvider.System.Delay(TimeSpan.FromMilliseconds(1500), AbortToken);
        var capped = await cache.GetAsync<string>(key, AbortToken);

        capped.HasValue.Should().BeFalse();
    }

    [Fact]
    public async Task should_not_rearm_ttl_on_value_read_within_tolerance()
    {
        await FlushAsync();
        using var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        var options = new CacheEntryOptions
        {
            Duration = TimeSpan.FromSeconds(30),
            SlidingExpiration = TimeSpan.FromSeconds(10),
        };

        await cache.GetOrAddAsync(key, _ => ValueTask.FromResult<string?>("value"), options, AbortToken);

        // Read right after the write: a 10s window has a 1s re-arm tolerance (min(1s, 10%)), so a read that would
        // move the deadline by only the few ms since the write is throttled and the TTL keeps counting down.
        var ttlBefore = await Fixture.ConnectionMultiplexer.GetDatabase().KeyTimeToLiveAsync(key);
        var read = await cache.GetAsync<string>(key, AbortToken);
        var ttlAfter = await Fixture.ConnectionMultiplexer.GetDatabase().KeyTimeToLiveAsync(key);

        read.Value.Should().Be("value");
        ttlBefore.Should().NotBeNull();
        ttlAfter.Should().NotBeNull();
        // A re-arm would push the TTL back up above ttlBefore; within the tolerance it only ever decreases.
        ttlAfter!.Value.Should().BeLessThanOrEqualTo(ttlBefore!.Value);
    }
}
