// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;
using Headless.Caching;
using Headless.Messaging;
using Headless.Testing.Tests;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

public sealed class HybridCacheTagTests : TestBase
{
    private readonly FakeTimeProvider _timeProvider = new();

    private (HybridCache cache, IInMemoryCache l1, IRemoteCache l2, IBus publisher) _CreateCache(
        HybridCacheOptions? options = null
    )
    {
        options ??= new HybridCacheOptions();
        var l1Options = new InMemoryCacheOptions { CloneValues = true };
        var l1 = new InMemoryCache(_timeProvider, l1Options);

        // Create a separate in-memory cache as the "distributed" cache for testing
        var l2Options = new InMemoryCacheOptions { CloneValues = true };
        var l2 = new InMemoryRemoteCacheAdapter(new InMemoryCache(_timeProvider, l2Options));

        var publisher = Substitute.For<IBus>();
        publisher
            .PublishAsync(Arg.Any<CacheInvalidationMessage>(), Arg.Any<PublishOptions?>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var cache = new HybridCache(l1, l2, publisher, options, timeProvider: _timeProvider);

        return (cache, l1, l2, publisher);
    }

    [Fact]
    public async Task should_publish_tag_invalidation_when_removing_by_tag()
    {
        // given
        var (cache, _, _, publisher) = _CreateCache();
        await using var _ = cache;

        var key = Faker.Random.AlphaNumeric(10);
        var tag = Faker.Random.AlphaNumeric(8);

        await cache.GetOrAddAsync(
            key,
            _ => ValueTask.FromResult<string?>("value"),
            new CacheEntryOptions { Duration = TimeSpan.FromMinutes(5), Tags = [tag] },
            AbortToken
        );

        // when
        await cache.RemoveByTagAsync(tag, AbortToken);

        // then — the invalidation is published with the tag
        await publisher
            .Received(1)
            .PublishAsync(
                Arg.Is<CacheInvalidationMessage>(m => m.Tag == tag && m.Key == null && !m.FlushAll && !m.Clear),
                Arg.Any<PublishOptions?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_seed_l2_tag_marker_when_receiving_foreign_tag_invalidation()
    {
        // given — a peer's tag invalidation arrives over the backplane (a different instance id)
        var (cache, _, l2, _) = _CreateCache();
        await using var _ = cache;
        var tag = Faker.Random.AlphaNumeric(8);
        var at = _timeProvider.GetUtcNow();

        // when
        await cache.HandleInvalidationAsync(
            new CacheInvalidationMessage
            {
                InstanceId = "other-instance",
                Tag = tag,
                Timestamp = at,
            },
            AbortToken
        );

        // then — the receiver pushes the marker into the L2 marker cache (no L2 round-trip, no refresh-window
        // wait), matching FusionCache's payload-carrying backplane optimization.
        var adapter = (InMemoryRemoteCacheAdapter)l2;
        adapter.SeededTagMarkers.Should().ContainSingle().Which.Should().Be((tag, at));
    }

    [Fact]
    public async Task should_seed_l2_clear_marker_when_receiving_foreign_clear()
    {
        // given — a peer's logical clear arrives over the backplane
        var (cache, _, l2, _) = _CreateCache();
        await using var _ = cache;
        var at = _timeProvider.GetUtcNow();

        // when
        await cache.HandleInvalidationAsync(
            new CacheInvalidationMessage
            {
                InstanceId = "other-instance",
                Clear = true,
                Timestamp = at,
            },
            AbortToken
        );

        // then — the clear generation is pushed into the L2 marker cache immediately
        var adapter = (InMemoryRemoteCacheAdapter)l2;
        adapter.SeededClearMarkers.Should().ContainSingle().Which.Should().Be(at);
    }

    [Fact]
    public async Task should_seed_l2_remove_marker_when_receiving_foreign_flush_all()
    {
        // given — a peer's flush arrives over the backplane
        var (cache, _, l2, _) = _CreateCache();
        await using var _ = cache;
        var at = _timeProvider.GetUtcNow();

        // when
        await cache.HandleInvalidationAsync(
            new CacheInvalidationMessage
            {
                InstanceId = "other-instance",
                FlushAll = true,
                Timestamp = at,
            },
            AbortToken
        );

        // then — the remove generation is pushed into the L2 marker cache immediately (the receiver also wipes L1)
        var adapter = (InMemoryRemoteCacheAdapter)l2;
        adapter.SeededRemoveMarkers.Should().ContainSingle().Which.Should().Be(at);
    }

    [Fact]
    public async Task should_publish_clear_when_clearing()
    {
        // given
        var (cache, _, _, publisher) = _CreateCache();
        await using var _ = cache;

        // when
        await cache.ClearAsync(AbortToken);

        // then — a Clear message is published (distinct from FlushAll)
        await publisher
            .Received(1)
            .PublishAsync(
                Arg.Is<CacheInvalidationMessage>(m => m.Clear && !m.FlushAll && m.Tag == null && m.Key == null),
                Arg.Any<PublishOptions?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_remove_both_tiers_when_removing_by_tag()
    {
        // given
        var (cache, l1, l2, _) = _CreateCache();
        await using var _ = cache;

        var key = Faker.Random.AlphaNumeric(10);
        var tag = Faker.Random.AlphaNumeric(8);

        await cache.GetOrAddAsync(
            key,
            _ => ValueTask.FromResult<string?>("value"),
            new CacheEntryOptions { Duration = TimeSpan.FromMinutes(5), Tags = [tag] },
            AbortToken
        );

        // when (advance so the marker postdates the entry's birth time)
        _timeProvider.Advance(TimeSpan.FromMilliseconds(10));
        await cache.RemoveByTagAsync(tag, AbortToken);

        // then
        (await l1.GetAsync<string>(key, AbortToken))
            .HasValue.Should()
            .BeFalse();
        (await l2.GetAsync<string>(key, AbortToken)).HasValue.Should().BeFalse();
    }

    [Fact]
    public async Task should_remove_matching_l1_entries_when_tag_invalidation_received()
    {
        // given — the entry was promoted into L1 with its tags via the envelope
        var options = new HybridCacheOptions { InstanceId = "instance-1" };
        var (cache, l1, _, _) = _CreateCache(options);
        await using var _ = cache;

        var key = Faker.Random.AlphaNumeric(10);
        var tag = Faker.Random.AlphaNumeric(8);

        await cache.GetOrAddAsync(
            key,
            _ => ValueTask.FromResult<string?>("value"),
            new CacheEntryOptions { Duration = TimeSpan.FromMinutes(5), Tags = [tag] },
            AbortToken
        );

        var message = new CacheInvalidationMessage
        {
            InstanceId = "instance-2", // Different instance
            Tag = tag,
        };

        // when (advance so the receiver's marker bump postdates the entry)
        _timeProvider.Advance(TimeSpan.FromMilliseconds(10));
        await cache.HandleInvalidationAsync(message, AbortToken);

        // then — only the local tier is invalidated by the consumer path (L1 marker bump)
        (await l1.GetAsync<string>(key, AbortToken))
            .HasValue.Should()
            .BeFalse();
        cache.InvalidateCacheCalls.Should().Be(1);
    }

    [Fact]
    public async Task should_logically_clear_l1_when_clear_invalidation_received()
    {
        // given
        var options = new HybridCacheOptions { InstanceId = "instance-1" };
        var (cache, l1, _, _) = _CreateCache(options);
        await using var _ = cache;

        var key = Faker.Random.AlphaNumeric(10);
        await cache.GetOrAddAsync(
            key,
            _ => ValueTask.FromResult<string?>("value"),
            new CacheEntryOptions { Duration = TimeSpan.FromMinutes(5) },
            AbortToken
        );

        var message = new CacheInvalidationMessage { InstanceId = "instance-2", Clear = true };

        // when (advance so the clear marker postdates the entry)
        _timeProvider.Advance(TimeSpan.FromMilliseconds(10));
        await cache.HandleInvalidationAsync(message, AbortToken);

        // then — the local tier is logically cleared (even though the entry carried no tag)
        (await l1.GetAsync<string>(key, AbortToken))
            .HasValue.Should()
            .BeFalse();
    }

    [Fact]
    public async Task should_ignore_self_originated_tag_invalidation()
    {
        // given
        var options = new HybridCacheOptions { InstanceId = "instance-1" };
        var (cache, l1, _, _) = _CreateCache(options);
        await using var _ = cache;

        var key = Faker.Random.AlphaNumeric(10);
        var tag = Faker.Random.AlphaNumeric(8);

        await cache.GetOrAddAsync(
            key,
            _ => ValueTask.FromResult<string?>("value"),
            new CacheEntryOptions { Duration = TimeSpan.FromMinutes(5), Tags = [tag] },
            AbortToken
        );

        var message = new CacheInvalidationMessage { InstanceId = "instance-1", Tag = tag };

        // when
        await cache.HandleInvalidationAsync(message, AbortToken);

        // then
        (await l1.GetAsync<string>(key, AbortToken))
            .HasValue.Should()
            .BeTrue();
        cache.InvalidateCacheCalls.Should().Be(0);
    }

    [Fact]
    public async Task should_invalidate_buffer_cold_seeded_entry_when_removing_by_tag()
    {
        // given — a tagged byte[] entry lives in L2 only (a fresh node whose L1 has never held it). Writing through
        // the L2 adapter stamps Tags + CreatedAt without ever touching the hybrid's L1.
        var (cache, l1, l2, _) = _CreateCache();
        await using var _ = cache;

        var key = Faker.Random.AlphaNumeric(10);
        var tag = Faker.Random.AlphaNumeric(8);
        var payload = Faker.Random.Bytes(16);

        await l2.UpsertEntryAsync(
            key,
            payload,
            new CacheEntryOptions { Duration = TimeSpan.FromMinutes(5), Tags = [tag] },
            AbortToken
        );
        (await l1.GetAsync<byte[]>(key, AbortToken)).HasValue.Should().BeFalse("the seed must start with an empty L1");

        // when — the buffer cold path (L1 miss -> L2 hit) seeds L1 with the entry's value AND its tag metadata.
        var coldReader = new ArrayBufferWriter<byte>();
        (await cache.TryGetToAsync(key, coldReader, AbortToken)).Should().BeTrue();
        coldReader.WrittenSpan.ToArray().Should().Equal(payload);

        // advance so the tag marker postdates the seeded entry's birth time, then invalidate the tag.
        _timeProvider.Advance(TimeSpan.FromMilliseconds(10));
        await cache.RemoveByTagAsync(tag, AbortToken);

        // then — the buffer-seeded L1 entry carries the tag, so the next read misses on both the buffer path and the
        // generic path. Before the fix the cold seed dropped the tags and this entry survived until physical TTL.
        var afterEvict = new ArrayBufferWriter<byte>();
        (await cache.TryGetToAsync(key, afterEvict, AbortToken)).Should().BeFalse();
        (await cache.GetAsync<byte[]>(key, AbortToken)).HasValue.Should().BeFalse();
    }

    [Fact]
    public async Task should_only_invalidate_entries_carrying_the_removed_tag()
    {
        // given — three keys all carrying the same tag set.
        var (cache, l1, l2, _) = _CreateCache();
        await using var _ = cache;

        string[] keys = ["foo", "bar", "baz"];
        var tags = new[] { "x", "y", "z" };

        foreach (var key in keys)
        {
            await cache.GetOrAddAsync(
                key,
                _ => ValueTask.FromResult<string?>("value"),
                new CacheEntryOptions { Duration = TimeSpan.FromMinutes(5), Tags = tags },
                AbortToken
            );
        }

        // when — invalidating a tag that no key carries must leave every entry intact.
        await cache.RemoveByTagAsync("blah", AbortToken);

        foreach (var key in keys)
        {
            (await l1.GetAsync<string>(key, AbortToken)).HasValue.Should().BeTrue("no-op removal must not evict L1");
            (await l2.GetAsync<string>(key, AbortToken)).HasValue.Should().BeTrue("no-op removal must not evict L2");
        }

        // when — invalidating a real, shared tag logically invalidates every entry carrying it on both tiers.
        _timeProvider.Advance(TimeSpan.FromMilliseconds(10));
        await cache.RemoveByTagAsync("y", AbortToken);

        foreach (var key in keys)
        {
            (await l1.GetAsync<string>(key, AbortToken)).HasValue.Should().BeFalse();
            (await l2.GetAsync<string>(key, AbortToken)).HasValue.Should().BeFalse();
        }
    }
}
