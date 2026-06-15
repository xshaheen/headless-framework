// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;

namespace Tests;

public sealed class RemoveTests(RedisCacheFixture fixture) : RedisCacheTestBase(fixture)
{
    [Fact]
    public async Task should_remove_existing_key()
    {
        // given
        await FlushAsync();
        using var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        await cache.UpsertAsync(key, "value", TimeSpan.FromMinutes(5), AbortToken);

        // when
        var result = await cache.RemoveAsync(key, AbortToken);

        // then
        result.Should().BeTrue();
        var exists = await cache.ExistsAsync(key, AbortToken);
        exists.Should().BeFalse();
    }

    [Fact]
    public async Task should_return_false_when_remove_non_existing_key()
    {
        // given
        await FlushAsync();
        using var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);

        // when
        var result = await cache.RemoveAsync(key, AbortToken);

        // then
        result.Should().BeFalse();
    }

    [Fact]
    public async Task should_remove_if_equal_when_expected_matches()
    {
        // given
        await FlushAsync();
        using var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        var value = Faker.Lorem.Sentence();
        await cache.UpsertAsync(key, value, TimeSpan.FromMinutes(5), AbortToken);

        // when
        var result = await cache.RemoveIfEqualAsync(key, value, AbortToken);

        // then
        result.Should().BeTrue();
        var exists = await cache.ExistsAsync(key, AbortToken);
        exists.Should().BeFalse();
    }

    [Fact]
    public async Task should_remove_if_equal_when_frame_carries_all_optional_metadata_sections()
    {
        // given — a frame with every optional section present (sliding, eager-refresh, last-modified,
        // etag, tags) so the CAS script has to skip the full variable-length chain to reach the value.
        await FlushAsync();
        using var cache = CreateCache();
        var store = (IFactoryCacheStore)cache;
        var key = Faker.Random.AlphaNumeric(10);
        var value = Faker.Lorem.Sentence();
        var now = DateTime.UtcNow;
        var entry = new CacheStoreEntryWrite<string>
        {
            Value = value,
            IsNull = false,
            LogicalExpiresAt = now.AddMinutes(5),
            PhysicalExpiresAt = now.AddMinutes(10),
            SlidingExpiration = TimeSpan.FromMinutes(5),
            EagerRefreshAt = now.AddMinutes(4),
            ETag = "W/\"v42\"",
            LastModifiedAt = now.AddMinutes(-30),
            Tags = ["tenant:1", "products"],
        };
        await store.SetEntryAsync(key, in entry, AbortToken);

        // when
        var result = await cache.RemoveIfEqualAsync(key, value, AbortToken);

        // then
        result.Should().BeTrue();
        var exists = await cache.ExistsAsync(key, AbortToken);
        exists.Should().BeFalse();
    }

    [Fact]
    public async Task should_not_remove_if_equal_when_expected_not_matches()
    {
        // given
        await FlushAsync();
        using var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        var value = Faker.Lorem.Sentence();
        var wrongExpected = Faker.Lorem.Sentence();
        await cache.UpsertAsync(key, value, TimeSpan.FromMinutes(5), AbortToken);

        // when
        var result = await cache.RemoveIfEqualAsync(key, wrongExpected, AbortToken);

        // then
        result.Should().BeFalse();
        var exists = await cache.ExistsAsync(key, AbortToken);
        exists.Should().BeTrue();
    }

    [Fact]
    public async Task should_remove_if_equal_when_expected_is_null_and_stored_frame_is_null()
    {
        // given
        await FlushAsync();
        using var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        await cache.UpsertAsync<string?>(key, null, TimeSpan.FromMinutes(5), AbortToken);

        // when
        var result = await cache.RemoveIfEqualAsync<string?>(key, null, AbortToken);

        // then
        result.Should().BeTrue();
        var exists = await cache.ExistsAsync(key, AbortToken);
        exists.Should().BeFalse();
    }

    [Fact]
    public async Task should_not_remove_if_equal_when_expected_is_null_and_stored_frame_is_not_null()
    {
        // given
        await FlushAsync();
        using var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        await cache.UpsertAsync(key, "value", TimeSpan.FromMinutes(5), AbortToken);

        // when
        var result = await cache.RemoveIfEqualAsync<string?>(key, null, AbortToken);

        // then
        result.Should().BeFalse();
        var exists = await cache.ExistsAsync(key, AbortToken);
        exists.Should().BeTrue();
    }

    [Fact]
    public async Task should_remove_if_equal_on_raw_unframed_counter_value()
    {
        // given — counters are stored raw/unframed, exercising the Lua else branch
        await FlushAsync();
        using var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        await cache.IncrementAsync(key, 5L, TimeSpan.FromMinutes(5), AbortToken);

        // when
        var result = await cache.RemoveIfEqualAsync(key, 5L, AbortToken);

        // then
        result.Should().BeTrue();
        var exists = await cache.ExistsAsync(key, AbortToken);
        exists.Should().BeFalse();
    }

    [Fact]
    public async Task should_remove_all_keys()
    {
        // given
        await FlushAsync();
        using var cache = CreateCache();
        var keys = new List<string>();
        for (var i = 0; i < 3; i++)
        {
            var key = Faker.Random.AlphaNumeric(10);
            keys.Add(key);
            await cache.UpsertAsync(key, $"value-{i}", TimeSpan.FromMinutes(5), AbortToken);
        }

        // when
        var result = await cache.RemoveAllAsync(keys, AbortToken);

        // then
        result.Should().Be(3);
        foreach (var key in keys)
        {
            var exists = await cache.ExistsAsync(key, AbortToken);
            exists.Should().BeFalse();
        }
    }

    [Fact]
    public async Task should_return_zero_when_remove_all_empty_keys()
    {
        // given
        await FlushAsync();
        using var cache = CreateCache();

        // when
        var result = await cache.RemoveAllAsync([], AbortToken);

        // then
        result.Should().Be(0);
    }

    [Fact]
    public async Task should_remove_by_prefix()
    {
        // given
        await FlushAsync();
        using var cache = CreateCache();
        const string prefix = "removeprefix:";
        await cache.UpsertAsync($"{prefix}key1", "value1", TimeSpan.FromMinutes(5), AbortToken);
        await cache.UpsertAsync($"{prefix}key2", "value2", TimeSpan.FromMinutes(5), AbortToken);
        await cache.UpsertAsync("other:key3", "value3", TimeSpan.FromMinutes(5), AbortToken);

        // when
        var result = await cache.RemoveByPrefixAsync(prefix, AbortToken);

        // then
        result.Should().Be(2);
        var exists1 = await cache.ExistsAsync($"{prefix}key1", AbortToken);
        var exists2 = await cache.ExistsAsync($"{prefix}key2", AbortToken);
        var exists3 = await cache.ExistsAsync("other:key3", AbortToken);
        exists1.Should().BeFalse();
        exists2.Should().BeFalse();
        exists3.Should().BeTrue();
    }

    [Fact]
    public async Task should_flush_all()
    {
        // given
        await FlushAsync();
        using var cache = CreateCache();
        var keys = new List<string>();
        for (var i = 0; i < 5; i++)
        {
            var key = Faker.Random.AlphaNumeric(10);
            keys.Add(key);
            await cache.UpsertAsync(key, "value", TimeSpan.FromMinutes(5), AbortToken);
        }

        // Small delay so the logical remove-generation marker postdates the writes (both are ms-precision).
        await Task.Delay(5, AbortToken);

        // when
        await cache.FlushAsync(AbortToken);

        // then — every entry reads as a miss. FlushAsync is a logical remove-generation marker (FusionCache
        // Clear(false) parity), not a physical FLUSHDB, so entries are physically retained until TTL; the observable
        // contract is the read miss, not GetCountAsync == 0.
        foreach (var key in keys)
        {
            (await cache.GetAsync<string>(key, AbortToken)).HasValue.Should().BeFalse();
        }
    }
}
