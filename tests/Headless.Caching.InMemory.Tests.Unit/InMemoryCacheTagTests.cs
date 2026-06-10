// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Testing.Tests;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

public sealed class InMemoryCacheTagTests : TestBase
{
    private readonly FakeTimeProvider _timeProvider = new();

    private InMemoryCache _CreateCache(InMemoryCacheOptions? options = null)
    {
        options ??= new InMemoryCacheOptions();
        return new InMemoryCache(_timeProvider, options);
    }

    [Fact]
    public async Task should_not_count_evicted_entry_in_remove_by_tag()
    {
        // given — an LRU-evicted tagged entry must be untagged from the index by the eviction path
        var options = new InMemoryCacheOptions { MaxItems = 3 };
        using var cache = _CreateCache(options);
        var tag = Faker.Random.AlphaNumeric(8);

        await cache.UpsertEntryAsync(
            "tagged",
            "value",
            new CacheEntryOptions { Duration = TimeSpan.FromMinutes(5), Tags = [tag] },
            AbortToken
        );
        _timeProvider.Advance(TimeSpan.FromMilliseconds(10));

        for (var i = 0; i < 4; i++)
        {
            await cache.UpsertAsync($"filler-{i}", "value", TimeSpan.FromMinutes(5), AbortToken);
            _timeProvider.Advance(TimeSpan.FromMilliseconds(10));
        }

        // Wait briefly to allow async eviction to complete
        await Task.Delay(100, AbortToken);
        (await cache.ExistsAsync("tagged", AbortToken)).Should().BeFalse("the tagged entry must have been evicted");

        // when
        var removed = await cache.RemoveByTagAsync(tag, AbortToken);

        // then
        removed.Should().Be(0);
    }

    [Fact]
    public async Task should_not_count_expired_entry_in_remove_by_tag()
    {
        // given
        using var cache = _CreateCache();
        var tag = Faker.Random.AlphaNumeric(8);

        await cache.UpsertEntryAsync(
            "expiring",
            "value",
            new CacheEntryOptions { Duration = TimeSpan.FromMilliseconds(100), Tags = [tag] },
            AbortToken
        );

        // when — past physical expiry the entry is dead even though it may still be resident
        _timeProvider.Advance(TimeSpan.FromMilliseconds(200));
        var removed = await cache.RemoveByTagAsync(tag, AbortToken);

        // then
        removed.Should().Be(0);
        (await cache.GetAsync<string>("expiring", AbortToken)).HasValue.Should().BeFalse();
    }

    [Fact]
    public async Task should_drop_stale_membership_when_entry_overwritten_without_tag()
    {
        // given — a tagged entry overwritten by a replace write without tags
        using var cache = _CreateCache();
        var tag = Faker.Random.AlphaNumeric(8);

        await cache.UpsertEntryAsync(
            "key",
            "tagged",
            new CacheEntryOptions { Duration = TimeSpan.FromMinutes(5), Tags = [tag] },
            AbortToken
        );

        await cache.TryReplaceAsync("key", "untagged", TimeSpan.FromMinutes(5), AbortToken);

        // when
        var removed = await cache.RemoveByTagAsync(tag, AbortToken);
        var cached = await cache.GetAsync<string>("key", AbortToken);

        // then — the live entry no longer carries the tag, so it survives
        removed.Should().Be(0);
        cached.HasValue.Should().BeTrue();
        cached.Value.Should().Be("untagged");
    }

    [Fact]
    public async Task should_retag_entry_when_overwritten_with_different_tags()
    {
        // given — the second write drops the first tag and adds another
        using var cache = _CreateCache();
        var oldTag = Faker.Random.AlphaNumeric(8);
        var newTag = Faker.Random.AlphaNumeric(8);

        await cache.UpsertEntryAsync(
            "key",
            "v1",
            new CacheEntryOptions { Duration = TimeSpan.FromMinutes(5), Tags = [oldTag] },
            AbortToken
        );

        await cache.UpsertEntryAsync(
            "key",
            "v2",
            new CacheEntryOptions { Duration = TimeSpan.FromMinutes(5), Tags = [newTag] },
            AbortToken
        );

        // when / then — the old tag no longer matches; the new one removes the entry
        (await cache.RemoveByTagAsync(oldTag, AbortToken))
            .Should()
            .Be(0);
        (await cache.GetAsync<string>("key", AbortToken)).HasValue.Should().BeTrue();

        (await cache.RemoveByTagAsync(newTag, AbortToken)).Should().Be(1);
        (await cache.GetAsync<string>("key", AbortToken)).HasValue.Should().BeFalse();
    }

    [Fact]
    public async Task should_clear_tag_index_on_flush()
    {
        // given
        using var cache = _CreateCache();
        var tag = Faker.Random.AlphaNumeric(8);

        await cache.UpsertEntryAsync(
            "key",
            "value",
            new CacheEntryOptions { Duration = TimeSpan.FromMinutes(5), Tags = [tag] },
            AbortToken
        );

        // when
        await cache.FlushAsync(AbortToken);
        var removed = await cache.RemoveByTagAsync(tag, AbortToken);

        // then
        removed.Should().Be(0);
    }
}
