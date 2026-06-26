// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using StackExchange.Redis;

namespace Tests;

public sealed class RedisCacheTagTests(RedisCacheFixture fixture) : RedisCacheTestBase(fixture)
{
    private const string _TagMarkerNamespace = "\0__tag:";
    private const string _ClearMarkerSuffix = "\0__clear";
    private const string _RemoveMarkerSuffix = "\0__remove";

    private IDatabase _Database => Fixture.ConnectionMultiplexer.GetDatabase();

    [Fact]
    public async Task should_write_tag_marker_under_reserved_namespace()
    {
        await FlushAsync();
        var prefix = $"{Faker.Random.AlphaNumeric(8)}:";
        using var cache = CreateCache(prefix);
        var tag = Faker.Random.AlphaNumeric(8);

        // O(1) invalidation writes a single timestamp marker at {KeyPrefix}__tag:{tag}; no reverse-index hash.
        await cache.RemoveByTagAsync(tag, AbortToken);

        var markerKey = $"{prefix}{_TagMarkerNamespace}{tag}";
        (await _Database.KeyExistsAsync(markerKey)).Should().BeTrue();
        var marker = await _Database.StringGetAsync(markerKey);
        marker.HasValue.Should().BeTrue();
        long.TryParse(marker.ToString(), System.Globalization.CultureInfo.InvariantCulture, out _)
            .Should()
            .BeTrue("the marker is a unix-ms timestamp");
    }

    [Fact]
    public async Task should_write_clear_marker_under_reserved_key()
    {
        await FlushAsync();
        var prefix = $"{Faker.Random.AlphaNumeric(8)}:";
        using var cache = CreateCache(prefix);

        await cache.ClearAsync(AbortToken);

        var clearKey = $"{prefix}{_ClearMarkerSuffix}";
        (await _Database.KeyExistsAsync(clearKey)).Should().BeTrue();
    }

    [Fact]
    public async Task should_write_tag_marker_raise_only()
    {
        await FlushAsync();
        var prefix = $"{Faker.Random.AlphaNumeric(8)}:";
        using var cache = CreateCache(prefix);
        var writer = (ISeedableTagMarkerCache)cache;
        var tag = Faker.Random.AlphaNumeric(8);
        var markerKey = $"{prefix}{_TagMarkerNamespace}{tag}";

        var t1 = DateTimeOffset.UtcNow;
        var t0 = t1 - TimeSpan.FromSeconds(10);
        var t2 = t1 + TimeSpan.FromSeconds(10);

        // A durable write establishes the marker.
        await writer.WriteTagMarkerAsync(tag, t1, AbortToken);
        var afterT1 = (await _Database.StringGetAsync(markerKey)).ToString();

        // An OLDER write must not lower it (raise-only — this is what makes an auto-recovery replay safe).
        await writer.WriteTagMarkerAsync(tag, t0, AbortToken);
        (await _Database.StringGetAsync(markerKey))
            .ToString()
            .Should()
            .Be(afterT1, "an older raise-only write must not lower the stored marker");

        // A NEWER write does raise it.
        await writer.WriteTagMarkerAsync(tag, t2, AbortToken);
        (await _Database.StringGetAsync(markerKey)).ToString().Should().NotBe(afterT1);
    }

    [Fact]
    public async Task should_miss_tagged_entry_after_remove_by_tag()
    {
        await FlushAsync();
        var prefix = $"{Faker.Random.AlphaNumeric(8)}:";
        using var cache = CreateCache(prefix);
        var key = Faker.Random.AlphaNumeric(10);
        var tag = Faker.Random.AlphaNumeric(8);

        await cache.UpsertEntryAsync(
            key,
            "value",
            new CacheEntryOptions { Duration = TimeSpan.FromMinutes(5), Tags = [tag] },
            AbortToken
        );

        // Small delay so the marker timestamp is strictly newer than the entry's birth time (both are ms).
        await Task.Delay(5, AbortToken);
        await cache.RemoveByTagAsync(tag, AbortToken);

        (await cache.GetAsync<string>(key, AbortToken)).HasValue.Should().BeFalse();
        (await cache.ExistsAsync(key, AbortToken)).Should().BeFalse();

        // The entry is still physically present (the marker invalidates logically, not by deletion).
        (await _Database.KeyExistsAsync($"{prefix}{key}"))
            .Should()
            .BeTrue();
    }

    [Fact]
    public async Task should_not_invalidate_entry_overwritten_without_tag()
    {
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

        // Plain untagged overwrite stamps a fresh birth time and carries no tags.
        await cache.UpsertAsync(key, "overwritten", TimeSpan.FromMinutes(10), AbortToken);

        await cache.RemoveByTagAsync(tag, AbortToken);

        var cached = await cache.GetAsync<string>(key, AbortToken);
        cached.HasValue.Should().BeTrue();
        cached.Value.Should().Be("overwritten");
    }

    [Fact]
    public async Task should_not_invalidate_entry_recreated_after_tag_bump()
    {
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

        // Re-tag the same key. The new write's birth time is newer than the oldTag marker bumped below.
        await cache.UpsertEntryAsync(
            key,
            "v2",
            new CacheEntryOptions { Duration = TimeSpan.FromMinutes(5), Tags = [newTag] },
            AbortToken
        );

        // oldTag no longer applies to the live entry (it carries newTag now), so invalidating oldTag is a no-op.
        await cache.RemoveByTagAsync(oldTag, AbortToken);
        (await cache.GetAsync<string>(key, AbortToken)).HasValue.Should().BeTrue();

        // newTag does apply: the entry reads as a miss (delay so the marker postdates the v2 write).
        await Task.Delay(5, AbortToken);
        await cache.RemoveByTagAsync(newTag, AbortToken);
        (await cache.GetAsync<string>(key, AbortToken)).HasValue.Should().BeFalse();
    }

    [Fact]
    public async Task should_preserve_failsafe_reserve_under_tag_invalidation()
    {
        await FlushAsync();
        var prefix = $"{Faker.Random.AlphaNumeric(8)}:";
        using var cache = CreateCache(prefix);
        var key = Faker.Random.AlphaNumeric(10);
        var tag = Faker.Random.AlphaNumeric(8);
        var failSafeOptions = new CacheEntryOptions
        {
            Duration = TimeSpan.FromMinutes(5),
            IsFailSafeEnabled = true,
            FailSafeMaxDuration = TimeSpan.FromMinutes(30),
            FailSafeThrottleDuration = TimeSpan.FromSeconds(1),
            Tags = [tag],
        };

        await cache.GetOrAddAsync(key, _ => ValueTask.FromResult<string?>("value"), failSafeOptions, AbortToken);

        await Task.Delay(5, AbortToken);
        await cache.RemoveByTagAsync(tag, AbortToken);

        // Direct read misses, but the physical reserve survives.
        (await cache.GetAsync<string>(key, AbortToken))
            .HasValue.Should()
            .BeFalse();
        (await _Database.KeyExistsAsync($"{prefix}{key}")).Should().BeTrue();

        // A failing fail-safe factory still serves the stale reserve.
        var result = await cache.GetOrAddAsync<string>(
            key,
            _ => throw new InvalidOperationException("downstream unavailable"),
            failSafeOptions,
            AbortToken
        );
        result.HasValue.Should().BeTrue();
        result.Value.Should().Be("value");
        result.IsStale.Should().BeTrue();
    }

    [Fact]
    public async Task should_logically_clear_with_clear_async_preserving_physical_entries()
    {
        await FlushAsync();
        var prefix = $"{Faker.Random.AlphaNumeric(8)}:";
        using var cache = CreateCache(prefix);
        var key = Faker.Random.AlphaNumeric(10);

        await cache.UpsertAsync(key, "value", TimeSpan.FromMinutes(5), AbortToken);

        await Task.Delay(5, AbortToken);
        await cache.ClearAsync(AbortToken);

        // Logical clear: direct read misses, but the key is physically retained (unlike FlushAsync).
        (await cache.GetAsync<string>(key, AbortToken))
            .HasValue.Should()
            .BeFalse();
        (await _Database.KeyExistsAsync($"{prefix}{key}")).Should().BeTrue();
    }

    [Fact]
    public async Task should_logically_remove_with_flush_async_dropping_reserves_keeping_keys_physical()
    {
        await FlushAsync();
        var prefix = $"{Faker.Random.AlphaNumeric(8)}:";
        using var cache = CreateCache(prefix);
        var key = Faker.Random.AlphaNumeric(10);
        var failSafeOptions = new CacheEntryOptions
        {
            Duration = TimeSpan.FromMinutes(5),
            IsFailSafeEnabled = true,
            FailSafeMaxDuration = TimeSpan.FromMinutes(30),
            FailSafeThrottleDuration = TimeSpan.FromSeconds(1),
        };

        await cache.GetOrAddAsync(key, _ => ValueTask.FromResult<string?>("value"), failSafeOptions, AbortToken);

        await Task.Delay(5, AbortToken);
        await cache.FlushAsync(AbortToken);

        // Logical flush (FusionCache Clear(false)): no FLUSHDB — the entry key is physically retained and the
        // remove-generation marker is written under its reserved key.
        (await _Database.KeyExistsAsync($"{prefix}{key}"))
            .Should()
            .BeTrue();
        (await _Database.KeyExistsAsync($"{prefix}{_RemoveMarkerSuffix}")).Should().BeTrue();

        // ...yet the entry reads as a hard miss.
        (await cache.GetAsync<string>(key, AbortToken))
            .HasValue.Should()
            .BeFalse();

        // Unlike ClearAsync, FlushAsync drops the fail-safe reserve: a failing factory cannot serve the stale value.
        var act = async () =>
            await cache.GetOrAddAsync<string>(
                key,
                _ => throw new InvalidOperationException("downstream unavailable"),
                failSafeOptions,
                AbortToken
            );
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task should_not_invalidate_when_marker_is_lost()
    {
        await FlushAsync();
        var prefix = $"{Faker.Random.AlphaNumeric(8)}:";
        // Tiny refresh window so the deleted marker is re-resolved (and seen as absent) immediately.
        using var cache = CreateCache(prefix, TimeSpan.FromMilliseconds(1));
        var key = Faker.Random.AlphaNumeric(10);
        var tag = Faker.Random.AlphaNumeric(8);

        await cache.UpsertEntryAsync(
            key,
            "value",
            new CacheEntryOptions { Duration = TimeSpan.FromMinutes(5), Tags = [tag] },
            AbortToken
        );

        await Task.Delay(5, AbortToken);
        await cache.RemoveByTagAsync(tag, AbortToken);
        (await cache.GetAsync<string>(key, AbortToken)).HasValue.Should().BeFalse();

        // Simulate marker loss (e.g. its own TTL elapsed or it was evicted): the entry is no longer invalidated,
        // and its physical TTL is the staleness backstop.
        await _Database.KeyDeleteAsync($"{prefix}{_TagMarkerNamespace}{tag}");
        await Task.Delay(20, AbortToken);

        var cached = await cache.GetAsync<string>(key, AbortToken);
        cached.HasValue.Should().BeTrue("a missing marker means not-invalidated");
        cached.Value.Should().Be("value");
    }

    [Fact]
    public async Task should_propagate_tag_invalidation_across_instances_within_refresh_window()
    {
        await FlushAsync();
        var prefix = $"{Faker.Random.AlphaNumeric(8)}:";
        var window = TimeSpan.FromMilliseconds(50);
        using var writer = CreateCache(prefix, window);
        using var reader = CreateCache(prefix, window);
        var key = Faker.Random.AlphaNumeric(10);
        var tag = Faker.Random.AlphaNumeric(8);

        await writer.UpsertEntryAsync(
            key,
            "value",
            new CacheEntryOptions { Duration = TimeSpan.FromMinutes(5), Tags = [tag] },
            AbortToken
        );

        // Reader caches the (absent) marker now.
        (await reader.GetAsync<string>(key, AbortToken))
            .HasValue.Should()
            .BeTrue();

        // Writer invalidates the tag (writes the L2 marker), delayed so the marker postdates the entry.
        await Task.Delay(5, AbortToken);
        await writer.RemoveByTagAsync(tag, AbortToken);

        // After the reader's refresh window elapses it re-resolves the marker and sees the invalidation.
        await Task.Delay(window + TimeSpan.FromMilliseconds(50), AbortToken);

        (await reader.GetAsync<string>(key, AbortToken)).HasValue.Should().BeFalse();
    }

    [Fact]
    public async Task should_invalidate_immediately_on_the_issuing_instance()
    {
        await FlushAsync();
        var prefix = $"{Faker.Random.AlphaNumeric(8)}:";
        // A long window proves the issuing instance does NOT depend on a refresh to observe its own bump.
        using var cache = CreateCache(prefix, TimeSpan.FromHours(1));
        var key = Faker.Random.AlphaNumeric(10);
        var tag = Faker.Random.AlphaNumeric(8);

        await cache.UpsertEntryAsync(
            key,
            "value",
            new CacheEntryOptions { Duration = TimeSpan.FromMinutes(5), Tags = [tag] },
            AbortToken
        );

        await Task.Delay(5, AbortToken);
        await cache.RemoveByTagAsync(tag, AbortToken);

        (await cache.GetAsync<string>(key, AbortToken)).HasValue.Should().BeFalse();
    }

    [Fact]
    public async Task should_be_noop_for_unknown_tag()
    {
        await FlushAsync();
        using var cache = CreateCache($"{Faker.Random.AlphaNumeric(8)}:");

        // RemoveByTag for a tag no entry carries is a harmless marker write.
        var act = async () => await cache.RemoveByTagAsync(Faker.Random.AlphaNumeric(12), AbortToken);
        await act.Should().NotThrowAsync();
    }
}
