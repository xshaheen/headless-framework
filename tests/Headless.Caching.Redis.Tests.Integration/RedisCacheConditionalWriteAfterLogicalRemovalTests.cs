// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Microsoft.Extensions.Time.Testing;
using StackExchange.Redis;

namespace Tests;

/// <summary>
/// A logical clear, flush, or expiry leaves the Redis key physically present while every read reports it absent; the
/// conditional writes must agree with those reads instead of with the raw key.
/// </summary>
public sealed class RedisCacheConditionalWriteAfterLogicalRemovalTests(RedisCacheFixture fixture)
    : RedisCacheTestBase(fixture)
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

    private IDatabase Database => Fixture.ConnectionMultiplexer.GetDatabase();

    [Fact]
    public async Task should_insert_over_an_entry_a_clear_invalidated()
    {
        var (cache, key) = await _CreateWithStaleEntryAsync(c => c.ClearAsync(AbortToken).AsTask());
        using var _ = cache;

        var inserted = await cache.TryInsertAsync(key, "fresh", TimeSpan.FromMinutes(5), AbortToken);

        inserted.Should().BeTrue("the cleared entry is absent to every read");
        (await cache.GetAsync<string>(key, AbortToken)).Value.Should().Be("fresh");
        (await cache.TryInsertAsync(key, "second", TimeSpan.FromMinutes(5), AbortToken))
            .Should()
            .BeFalse("the inserted entry is live");
    }

    [Fact]
    public async Task should_insert_over_an_entry_a_flush_invalidated()
    {
        var (cache, key) = await _CreateWithStaleEntryAsync(c => c.FlushAsync(AbortToken).AsTask());
        using var _ = cache;

        (await cache.TryInsertAsync(key, "fresh", TimeSpan.FromMinutes(5), AbortToken)).Should().BeTrue();
        (await cache.GetAsync<string>(key, AbortToken)).Value.Should().Be("fresh");
    }

    [Fact]
    public async Task should_insert_over_an_entry_past_its_logical_expiry()
    {
        await FlushAsync();
        using var cache = CreateCache($"{Faker.Random.AlphaNumeric(8)}:", timeProvider: _clock);
        var key = Faker.Random.AlphaNumeric(10);
        await cache.UpsertAsync(key, "old", TimeSpan.FromMinutes(1), AbortToken);

        // The app clock passes the logical expiry while Redis still holds the key under its real-time TTL.
        _clock.Advance(TimeSpan.FromMinutes(2));

        (await cache.GetAsync<string>(key, AbortToken)).HasValue.Should().BeFalse();
        (await cache.TryInsertAsync(key, "fresh", TimeSpan.FromMinutes(5), AbortToken)).Should().BeTrue();
        (await cache.GetAsync<string>(key, AbortToken)).Value.Should().Be("fresh");
    }

    [Fact]
    public async Task should_not_replace_if_equal_an_entry_a_clear_invalidated()
    {
        var (cache, key) = await _CreateWithStaleEntryAsync(c => c.ClearAsync(AbortToken).AsTask());
        using var _ = cache;

        var replaced = await cache.TryReplaceIfEqualAsync(key, "old", "new", TimeSpan.FromMinutes(5), AbortToken);

        replaced.Should().BeFalse("no read sees the expected value any more");
        (await cache.GetAsync<string>(key, AbortToken)).HasValue.Should().BeFalse();
    }

    [Fact]
    public async Task should_not_remove_if_equal_an_entry_a_clear_invalidated()
    {
        var (cache, key) = await _CreateWithStaleEntryAsync(c => c.ClearAsync(AbortToken).AsTask());
        using var _ = cache;

        (await cache.RemoveIfEqualAsync(key, "old", AbortToken)).Should().BeFalse();
    }

    [Fact]
    public async Task should_not_replace_an_entry_a_clear_invalidated()
    {
        var (cache, key) = await _CreateWithStaleEntryAsync(c => c.ClearAsync(AbortToken).AsTask());
        using var _ = cache;

        (await cache.TryReplaceAsync(key, "new", TimeSpan.FromMinutes(5), AbortToken)).Should().BeFalse();
        (await cache.GetAsync<string>(key, AbortToken)).HasValue.Should().BeFalse();
    }

    [Fact]
    public async Task should_still_compare_and_swap_a_live_entry()
    {
        await FlushAsync();
        using var cache = CreateCache($"{Faker.Random.AlphaNumeric(8)}:", timeProvider: _clock);
        var key = Faker.Random.AlphaNumeric(10);
        await cache.UpsertAsync(key, "old", TimeSpan.FromMinutes(5), AbortToken);

        (await cache.TryReplaceIfEqualAsync(key, "old", "new", TimeSpan.FromMinutes(5), AbortToken)).Should().BeTrue();
        (await cache.TryReplaceAsync(key, "newer", TimeSpan.FromMinutes(5), AbortToken)).Should().BeTrue();
        (await cache.RemoveIfEqualAsync(key, "newer", AbortToken)).Should().BeTrue();
    }

    private async Task<(RedisCache Cache, string Key)> _CreateWithStaleEntryAsync(Func<RedisCache, Task> invalidate)
    {
        await FlushAsync();
        var prefix = $"{Faker.Random.AlphaNumeric(8)}:";
        var cache = CreateCache(prefix, timeProvider: _clock);
        var key = Faker.Random.AlphaNumeric(10);
        await cache.UpsertAsync(key, "old", TimeSpan.FromMinutes(5), AbortToken);

        // A marker invalidates only entries born strictly before it.
        _clock.Advance(TimeSpan.FromSeconds(1));
        await invalidate(cache);

        (await cache.GetAsync<string>(key, AbortToken)).HasValue.Should().BeFalse("the entry is logically removed");
        (await Database.KeyExistsAsync($"{prefix}{key}")).Should().BeTrue("the key is still physically present");

        return (cache, key);
    }
}
