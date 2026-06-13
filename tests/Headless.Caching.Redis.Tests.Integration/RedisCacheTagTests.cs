// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using StackExchange.Redis;

namespace Tests;

public sealed class RedisCacheTagTests(RedisCacheFixture fixture) : RedisCacheTestBase(fixture)
{
    private const string _TagNamespace = "__cache_tag__:";

    private IDatabase _Database => Fixture.ConnectionMultiplexer.GetDatabase();

    [Fact]
    public async Task should_store_tag_index_under_reserved_namespace()
    {
        // given
        await FlushAsync();
        var prefix = $"{Faker.Random.AlphaNumeric(8)}:";
        using var cache = CreateCache(prefix);
        var key = Faker.Random.AlphaNumeric(10);
        var tag = Faker.Random.AlphaNumeric(8);

        // when
        await cache.UpsertEntryAsync(
            key,
            "value",
            new CacheEntryOptions { Duration = TimeSpan.FromMinutes(5), Tags = [tag] },
            AbortToken
        );

        // then — the tag hash lives at {KeyPrefix}__cache_tag__:{tag} with the full key as the field
        var tagHashKey = $"{prefix}{_TagNamespace}{tag}";
        (await _Database.KeyExistsAsync(tagHashKey)).Should().BeTrue();
        (await _Database.HashExistsAsync(tagHashKey, $"{prefix}{key}")).Should().BeTrue();

        // and it is unlinked after the tag invalidation
        var removed = await cache.RemoveByTagAsync(tag, AbortToken);
        removed.Should().Be(1);
        (await _Database.KeyExistsAsync(tagHashKey)).Should().BeFalse();
    }

    [Fact]
    public async Task should_skip_and_cleanup_stale_membership_after_plain_upsert_overwrite()
    {
        // given — a tagged entry overwritten by a plain (untagged, non-scripted) upsert
        await FlushAsync();
        var prefix = $"{Faker.Random.AlphaNumeric(8)}:";
        using var cache = CreateCache(prefix);
        var key = Faker.Random.AlphaNumeric(10);
        var tag = Faker.Random.AlphaNumeric(8);

        await cache.GetOrAddAsync(
            key,
            _ => ValueTask.FromResult<string?>("tagged"),
            new CacheEntryOptions { Duration = TimeSpan.FromMinutes(5), Tags = [tag] },
            AbortToken
        );

        await cache.UpsertAsync(key, "overwritten", TimeSpan.FromMinutes(10), AbortToken);

        // when — the recorded physical stamp no longer matches the live entry, so it is skipped
        var removed = await cache.RemoveByTagAsync(tag, AbortToken);

        // then
        removed.Should().Be(0);
        var cached = await cache.GetAsync<string>(key, AbortToken);
        cached.HasValue.Should().BeTrue();
        cached.Value.Should().Be("overwritten");

        // and the stale membership was cleaned up with the tag hash
        (await _Database.KeyExistsAsync($"{prefix}{_TagNamespace}{tag}"))
            .Should()
            .BeFalse();
    }

    [Fact]
    public async Task should_extend_tag_hash_ttl_with_greater_than_semantics()
    {
        // given
        await FlushAsync();
        var prefix = $"{Faker.Random.AlphaNumeric(8)}:";
        using var cache = CreateCache(prefix);
        var tag = Faker.Random.AlphaNumeric(8);
        var tagHashKey = $"{prefix}{_TagNamespace}{tag}";

        await cache.UpsertEntryAsync(
            Faker.Random.AlphaNumeric(10),
            "v1",
            new CacheEntryOptions { Duration = TimeSpan.FromMinutes(5), Tags = [tag] },
            AbortToken
        );

        var initialTtl = await _Database.KeyTimeToLiveAsync(tagHashKey);

        // when — a longer-lived member extends the hash TTL
        await cache.UpsertEntryAsync(
            Faker.Random.AlphaNumeric(10),
            "v2",
            new CacheEntryOptions { Duration = TimeSpan.FromMinutes(10), Tags = [tag] },
            AbortToken
        );

        var extendedTtl = await _Database.KeyTimeToLiveAsync(tagHashKey);

        // and a shorter-lived member must NOT shorten it
        await cache.UpsertEntryAsync(
            Faker.Random.AlphaNumeric(10),
            "v3",
            new CacheEntryOptions { Duration = TimeSpan.FromMinutes(1), Tags = [tag] },
            AbortToken
        );

        var unchangedTtl = await _Database.KeyTimeToLiveAsync(tagHashKey);

        // then
        initialTtl.Should().NotBeNull();
        initialTtl!.Value.Should().BeLessThanOrEqualTo(TimeSpan.FromMinutes(5));
        extendedTtl.Should().NotBeNull();
        extendedTtl!.Value.Should().BeGreaterThan(TimeSpan.FromMinutes(5));
        unchangedTtl.Should().NotBeNull();
        unchangedTtl!.Value.Should().BeGreaterThan(TimeSpan.FromMinutes(5));
    }

    [Fact]
    public async Task should_remove_dropped_tag_membership_on_retagged_write()
    {
        // given — the second tagged write drops the first tag
        await FlushAsync();
        var prefix = $"{Faker.Random.AlphaNumeric(8)}:";
        using var cache = CreateCache(prefix);
        var key = Faker.Random.AlphaNumeric(10);
        var oldTag = Faker.Random.AlphaNumeric(8);
        var newTag = Faker.Random.AlphaNumeric(8);

        await cache.UpsertEntryAsync(
            key,
            "v1",
            new CacheEntryOptions { Duration = TimeSpan.FromMinutes(5), Tags = [oldTag] },
            AbortToken
        );

        // when
        await cache.UpsertEntryAsync(
            key,
            "v2",
            new CacheEntryOptions { Duration = TimeSpan.FromMinutes(5), Tags = [newTag] },
            AbortToken
        );

        // then — the dropped membership was HDELed atomically with the write
        (await _Database.HashExistsAsync($"{prefix}{_TagNamespace}{oldTag}", $"{prefix}{key}"))
            .Should()
            .BeFalse();
        (await _Database.HashExistsAsync($"{prefix}{_TagNamespace}{newTag}", $"{prefix}{key}")).Should().BeTrue();

        (await cache.RemoveByTagAsync(oldTag, AbortToken)).Should().Be(0);
        (await cache.GetAsync<string>(key, AbortToken)).HasValue.Should().BeTrue();
        (await cache.RemoveByTagAsync(newTag, AbortToken)).Should().Be(1);
        (await cache.GetAsync<string>(key, AbortToken)).HasValue.Should().BeFalse();
    }

    [Fact]
    public async Task should_return_zero_for_unknown_tag()
    {
        // given
        await FlushAsync();
        using var cache = CreateCache($"{Faker.Random.AlphaNumeric(8)}:");

        // when
        var removed = await cache.RemoveByTagAsync(Faker.Random.AlphaNumeric(12), AbortToken);

        // then
        removed.Should().Be(0);
    }
}
