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
    public async Task should_miss_tagged_entry_after_remove_by_tag()
    {
        using var cache = _CreateCache();
        var tag = Faker.Random.AlphaNumeric(8);

        await cache.UpsertEntryAsync(
            "key",
            "value",
            new CacheEntryOptions { Duration = TimeSpan.FromMinutes(5), Tags = [tag] },
            AbortToken
        );

        // O(1) logical invalidation: the entry now reads as a miss (advance so the marker postdates the write).
        _timeProvider.Advance(TimeSpan.FromMilliseconds(10));
        await cache.RemoveByTagAsync(tag, AbortToken);

        (await cache.GetAsync<string>("key", AbortToken)).HasValue.Should().BeFalse();
        (await cache.ExistsAsync("key", AbortToken)).Should().BeFalse();
        (await cache.GetExpirationAsync("key", AbortToken)).Should().BeNull();
    }

    [Fact]
    public async Task should_no_op_seed_remove_marker_leaving_entries_readable()
    {
        using var cache = _CreateCache();

        await cache.UpsertAsync("key", "value", TimeSpan.FromMinutes(5), AbortToken);

        // SeedRemoveMarker is a documented no-op for the in-process cache (its FlushAsync wipes physically, so there
        // is no logical remove-generation marker to seed). Even a future-dated seed must not invalidate any entry.
        _timeProvider.Advance(TimeSpan.FromMilliseconds(10));
        cache.SeedRemoveMarker(_timeProvider.GetUtcNow());

        var value = await cache.GetAsync<string>("key", AbortToken);
        value.HasValue.Should().BeTrue();
        value.Value.Should().Be("value");
    }

    [Fact]
    public async Task should_not_invalidate_entry_lacking_the_tag()
    {
        using var cache = _CreateCache();
        var tag = Faker.Random.AlphaNumeric(8);
        var otherTag = Faker.Random.AlphaNumeric(8);

        await cache.UpsertEntryAsync(
            "key",
            "value",
            new CacheEntryOptions { Duration = TimeSpan.FromMinutes(5), Tags = [tag] },
            AbortToken
        );

        await cache.RemoveByTagAsync(otherTag, AbortToken);

        var cached = await cache.GetAsync<string>("key", AbortToken);
        cached.HasValue.Should().BeTrue();
        cached.Value.Should().Be("value");
    }

    [Fact]
    public async Task should_not_invalidate_entry_recreated_after_tag_bump()
    {
        using var cache = _CreateCache();
        var tag = Faker.Random.AlphaNumeric(8);

        await cache.UpsertEntryAsync(
            "key",
            "v1",
            new CacheEntryOptions { Duration = TimeSpan.FromMinutes(5), Tags = [tag] },
            AbortToken
        );

        await cache.RemoveByTagAsync(tag, AbortToken);
        _timeProvider.Advance(TimeSpan.FromMilliseconds(10));

        // Version-pin: a re-created entry has a newer birth time than the tag marker, so it survives.
        await cache.UpsertEntryAsync(
            "key",
            "v2",
            new CacheEntryOptions { Duration = TimeSpan.FromMinutes(5), Tags = [tag] },
            AbortToken
        );

        var cached = await cache.GetAsync<string>("key", AbortToken);
        cached.HasValue.Should().BeTrue();
        cached.Value.Should().Be("v2");
    }

    [Fact]
    public async Task should_not_invalidate_entry_overwritten_without_tag()
    {
        using var cache = _CreateCache();
        var tag = Faker.Random.AlphaNumeric(8);

        await cache.UpsertEntryAsync(
            "key",
            "tagged",
            new CacheEntryOptions { Duration = TimeSpan.FromMinutes(5), Tags = [tag] },
            AbortToken
        );

        // Overwrite with an untagged direct write (fresh birth time, no tags).
        await cache.TryReplaceAsync("key", "untagged", TimeSpan.FromMinutes(5), AbortToken);

        await cache.RemoveByTagAsync(tag, AbortToken);

        var cached = await cache.GetAsync<string>("key", AbortToken);
        cached.HasValue.Should().BeTrue();
        cached.Value.Should().Be("untagged");
    }

    [Fact]
    public async Task should_reset_markers_on_flush()
    {
        using var cache = _CreateCache();
        var tag = Faker.Random.AlphaNumeric(8);

        await cache.UpsertEntryAsync(
            "key",
            "value",
            new CacheEntryOptions { Duration = TimeSpan.FromMinutes(5), Tags = [tag] },
            AbortToken
        );
        _timeProvider.Advance(TimeSpan.FromMilliseconds(10));
        await cache.RemoveByTagAsync(tag, AbortToken);

        // Physical wipe also drops the markers: a subsequent same-tag write is not invalidated by the old marker.
        await cache.FlushAsync(AbortToken);
        _timeProvider.Advance(TimeSpan.FromMilliseconds(10));

        await cache.UpsertEntryAsync(
            "key",
            "fresh",
            new CacheEntryOptions { Duration = TimeSpan.FromMinutes(5), Tags = [tag] },
            AbortToken
        );

        var cached = await cache.GetAsync<string>("key", AbortToken);
        cached.HasValue.Should().BeTrue();
        cached.Value.Should().Be("fresh");
    }

    [Fact]
    public async Task should_logically_clear_all_entries_with_clear_async()
    {
        using var cache = _CreateCache();
        var tag = Faker.Random.AlphaNumeric(8);

        await cache.UpsertEntryAsync(
            "tagged",
            "t",
            new CacheEntryOptions { Duration = TimeSpan.FromMinutes(5), Tags = [tag] },
            AbortToken
        );
        await cache.UpsertAsync("untagged", "u", TimeSpan.FromMinutes(5), AbortToken);

        _timeProvider.Advance(TimeSpan.FromMilliseconds(10));
        await cache.ClearAsync(AbortToken);

        (await cache.GetAsync<string>("tagged", AbortToken)).HasValue.Should().BeFalse();
        (await cache.GetAsync<string>("untagged", AbortToken)).HasValue.Should().BeFalse();
    }

    [Fact]
    public async Task should_serve_tag_invalidated_reserve_as_stale_via_factory()
    {
        using var cache = _CreateCache();
        var tag = Faker.Random.AlphaNumeric(8);
        var options = new CacheEntryOptions
        {
            Duration = TimeSpan.FromMinutes(5),
            IsFailSafeEnabled = true,
            FailSafeMaxDuration = TimeSpan.FromMinutes(30),
            FailSafeThrottleDuration = TimeSpan.FromMilliseconds(200),
            Tags = [tag],
        };

        await cache.GetOrAddAsync("key", _ => ValueTask.FromResult<string?>("value"), options, AbortToken);
        _timeProvider.Advance(TimeSpan.FromMilliseconds(10));
        await cache.RemoveByTagAsync(tag, AbortToken);

        // Demoted to a fail-safe reserve: a failing factory still serves the stale value.
        var result = await cache.GetOrAddAsync<string>(
            "key",
            _ => throw new InvalidOperationException("boom"),
            options,
            AbortToken
        );

        result.HasValue.Should().BeTrue();
        result.Value.Should().Be("value");
        result.IsStale.Should().BeTrue();
    }
}
