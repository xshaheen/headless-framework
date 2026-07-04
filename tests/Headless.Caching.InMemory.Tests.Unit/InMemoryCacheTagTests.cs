// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
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

    // The maintenance sweep (which prunes stale tag markers) runs on a throttled background task in production; drive
    // it deterministically here via the same private entry point the performance tests invoke.
    private static async Task _RunMaintenanceAsync(InMemoryCache cache)
    {
        var method = typeof(InMemoryCache).GetMethod(
            "_DoMaintenanceAsync",
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly,
            binder: null,
            Type.EmptyTypes,
            modifiers: null
        );
        method.Should().NotBeNull();

        await (Task)method!.Invoke(cache, parameters: null)!;
    }

    private static int _GetTagMarkerCount(InMemoryCache cache)
    {
        var field = typeof(InMemoryCache).GetField(
            "_tagMarkers",
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly
        );
        field.Should().NotBeNull();

        var markers = field!.GetValue(cache)!;
        return (int)markers.GetType().GetProperty("Count")!.GetValue(markers)!;
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
    public async Task should_keep_marker_and_not_resurrect_entry_when_pruning_before_max_lifetime_elapses()
    {
        // SAFETY (#546): a marker must NOT be pruned while a still-live entry it invalidates could be resurrected.
        using var cache = _CreateCache(new InMemoryCacheOptions { MaintenanceInterval = TimeSpan.FromHours(1) });
        var tag = Faker.Random.AlphaNumeric(8);
        var duration = TimeSpan.FromMinutes(5); // maxObservedEntryLifetime becomes 5 minutes.

        await cache.UpsertEntryAsync(
            "key",
            "value",
            new CacheEntryOptions { Duration = duration, Tags = [tag] },
            AbortToken
        );

        // Marker postdates the entry's birth, so the entry is logically invalidated.
        _timeProvider.Advance(TimeSpan.FromMilliseconds(10));
        await cache.RemoveByTagAsync(tag, AbortToken);
        _GetTagMarkerCount(cache).Should().Be(1);

        // Advance LESS than the max lifetime: the entry is still physically present, so the marker is still needed.
        _timeProvider.Advance(TimeSpan.FromMinutes(1));
        await _RunMaintenanceAsync(cache);

        // The marker must survive...
        _GetTagMarkerCount(cache).Should().Be(1, "the marker is still needed by a physically-present entry");

        // ...and the pre-marker entry must still read as invalidated (a wrongly-pruned marker would resurrect it).
        (await cache.GetAsync<string>("key", AbortToken))
            .HasValue.Should()
            .BeFalse();
    }

    [Fact]
    public async Task should_prune_tag_markers_older_than_max_observed_entry_lifetime()
    {
        // BOUND (#546): once every entry a marker could invalidate is guaranteed physically gone, the marker is
        // pruned so the store cannot grow unbounded with process-lifetime distinct-tag cardinality.
        using var cache = _CreateCache(new InMemoryCacheOptions { MaintenanceInterval = TimeSpan.FromHours(1) });
        const int tagCount = 50;
        var duration = TimeSpan.FromMinutes(1); // maxObservedEntryLifetime becomes 1 minute.

        for (var i = 0; i < tagCount; i++)
        {
            var tag = $"tag-{i}";

            await cache.UpsertEntryAsync(
                $"key-{i}",
                "value",
                new CacheEntryOptions { Duration = duration, Tags = [tag] },
                AbortToken
            );
            await cache.RemoveByTagAsync(tag, AbortToken);
        }

        _GetTagMarkerCount(cache).Should().Be(tagCount);

        // Advance beyond the max observed lifetime: every entry is physically gone, so every marker is safe to prune.
        _timeProvider.Advance(TimeSpan.FromMinutes(2));
        await _RunMaintenanceAsync(cache);

        _GetTagMarkerCount(cache)
            .Should()
            .Be(0, "markers whose invalidation instant predates now - maxObservedEntryLifetime must be pruned");
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
