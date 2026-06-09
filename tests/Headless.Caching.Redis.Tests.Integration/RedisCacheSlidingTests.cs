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
        await TimeProvider.System.Delay(TimeSpan.FromMilliseconds(225), AbortToken);

        var firstRead = await cache.GetAsync<string>(key, AbortToken);
        var ttlAfterRearm = await Fixture.ConnectionMultiplexer.GetDatabase().KeyTimeToLiveAsync(key);

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

    [Fact]
    public async Task should_expire_at_absolute_duration_cap_even_with_repeated_reads()
    {
        await FlushAsync();
        using var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        var options = new CacheEntryOptions
        {
            Duration = TimeSpan.FromMilliseconds(550),
            SlidingExpiration = TimeSpan.FromMilliseconds(250),
        };

        await cache.GetOrAddAsync(key, _ => ValueTask.FromResult<string?>("value"), options, AbortToken);

        for (var i = 0; i < 3; i++)
        {
            await TimeProvider.System.Delay(TimeSpan.FromMilliseconds(150), AbortToken);
            (await cache.GetAsync<string>(key, AbortToken)).HasValue.Should().BeTrue();
        }

        await TimeProvider.System.Delay(TimeSpan.FromMilliseconds(175), AbortToken);
        var capped = await cache.GetAsync<string>(key, AbortToken);

        capped.HasValue.Should().BeFalse();
    }

    [Fact]
    public async Task should_not_rearm_ttl_on_value_read_below_threshold()
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

        // Read well within the first half of the 300ms window (re-arm threshold = 150ms), so the throttle must
        // suppress the re-arm: the key's TTL keeps counting down rather than being bumped back toward 300ms.
        await TimeProvider.System.Delay(TimeSpan.FromMilliseconds(50), AbortToken);

        var ttlBefore = await Fixture.ConnectionMultiplexer.GetDatabase().KeyTimeToLiveAsync(key);
        var read = await cache.GetAsync<string>(key, AbortToken);
        var ttlAfter = await Fixture.ConnectionMultiplexer.GetDatabase().KeyTimeToLiveAsync(key);

        read.Value.Should().Be("value");
        ttlBefore.Should().NotBeNull();
        ttlAfter.Should().NotBeNull();
        // A re-arm would push the TTL back up above ttlBefore; below the threshold it only ever decreases.
        ttlAfter!.Value.Should().BeLessThanOrEqualTo(ttlBefore!.Value);
    }
}
