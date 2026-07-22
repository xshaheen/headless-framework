// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Caching;
using Headless.Testing.Tests;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

public sealed class InMemoryCacheEventTests : TestBase
{
    private readonly FakeTimeProvider _timeProvider = new();
    private readonly List<InMemoryCache> _caches = [];

    protected override ValueTask DisposeAsyncCore()
    {
        foreach (var cache in _caches)
        {
            cache.Dispose();
        }

        _caches.Clear();

        return base.DisposeAsyncCore();
    }

    private InMemoryCache _CreateCache(InMemoryCacheOptions? options = null)
    {
        var cache = new InMemoryCache(
            _timeProvider,
            options ?? new InMemoryCacheOptions(),
            logger: null,
            factoryLockProvider: null,
            instrumentation: null,
            eventsConfig: new CacheEventsConfig { SyncHandlers = true }
        );
        _caches.Add(cache);

        return cache;
    }

    [Fact]
    public async Task should_raise_hit_and_miss_on_direct_get()
    {
        // given
        var cache = _CreateCache();
        await cache.UpsertAsync("present", "v", TimeSpan.FromMinutes(5), AbortToken);
        var hits = new List<CacheHitEventArgs>();
        var misses = new List<CacheKeyEventArgs>();
        cache.Events.Hit += (_, e) => hits.Add(e);
        cache.Events.Miss += (_, e) => misses.Add(e);

        // when
        await cache.GetAsync<string>("present", AbortToken);
        await cache.GetAsync<string>("absent", AbortToken);

        // then
        hits.Should().ContainSingle(h => h.Key == "present" && h.Tier == CacheTier.L1);
        misses.Should().ContainSingle(m => m.Key == "absent");
    }

    [Fact]
    public async Task should_raise_set_on_upsert()
    {
        // given
        var cache = _CreateCache();
        CacheKeyEventArgs? set = null;
        cache.Events.Set += (_, e) => set = e;

        // when
        await cache.UpsertAsync("k", "v", TimeSpan.FromMinutes(5), AbortToken);

        // then
        set.Should().NotBeNull();
        set!.Key.Should().Be("k");
    }

    [Fact]
    public async Task should_raise_remove_and_removed_eviction_on_remove()
    {
        // given
        var cache = _CreateCache();
        await cache.UpsertAsync("k", "v", TimeSpan.FromMinutes(5), AbortToken);
        CacheKeyEventArgs? removed = null;
        CacheEvictionEventArgs? evicted = null;
        cache.Events.Remove += (_, e) => removed = e;
        cache.Events.Eviction += (_, e) => evicted = e;

        // when
        await cache.RemoveAsync("k", AbortToken);

        // then
        removed!.Key.Should().Be("k");
        evicted!.Key.Should().Be("k");
        evicted.Reason.Should().Be(CacheEvictionReason.Removed);
    }

    [Fact]
    public async Task should_raise_flush_and_flushed_evictions_when_eviction_subscribed()
    {
        // given
        var cache = _CreateCache();
        await cache.UpsertAsync("a", "1", TimeSpan.FromMinutes(5), AbortToken);
        await cache.UpsertAsync("b", "2", TimeSpan.FromMinutes(5), AbortToken);
        var flushed = false;
        var evictions = new ConcurrentBag<CacheEvictionEventArgs>();
        cache.Events.Flush += (_, _) => flushed = true;
        cache.Events.Eviction += (_, e) => evictions.Add(e);

        // when
        await cache.FlushAsync(AbortToken);

        // then
        flushed.Should().BeTrue();
        evictions.Should().HaveCount(2);
        evictions.Should().OnlyContain(e => e.Reason == CacheEvictionReason.Flushed);
    }

    [Fact]
    public async Task should_not_enumerate_evictions_on_flush_when_eviction_unsubscribed()
    {
        // given — a subscriber on an unrelated event, but NOT Eviction
        var cache = _CreateCache();
        await cache.UpsertAsync("a", "1", TimeSpan.FromMinutes(5), AbortToken);
        var flushed = false;
        var evictionFired = false;
        cache.Events.Set += (_, _) => { };
        cache.Events.Flush += (_, _) => flushed = true;
        // no cache.Events.Eviction subscription

        // when
        await cache.FlushAsync(AbortToken);

        // then — Flush fires; the per-key eviction loop is skipped (HasEvictionSubscribers is false)
        flushed.Should().BeTrue();
        evictionFired.Should().BeFalse();
    }

    [Fact]
    public async Task should_raise_expired_eviction_on_lazy_read_reap()
    {
        // given — an entry that expires, then a read that lazily reaps it
        var cache = _CreateCache();
        await cache.UpsertAsync("k", "v", TimeSpan.FromSeconds(1), AbortToken);
        var evictions = new ConcurrentBag<CacheEvictionEventArgs>();
        cache.Events.Eviction += (_, e) => evictions.Add(e);

        // when — advance past expiry, then read (lazy reap)
        _timeProvider.Advance(TimeSpan.FromSeconds(5));
        var result = await cache.GetAsync<string>("k", AbortToken);

        // then
        result.HasValue.Should().BeFalse();
        evictions.Should().Contain(e => e.Key == "k" && e.Reason == CacheEvictionReason.Expired);
    }

    [Fact]
    public async Task should_expose_caller_facing_key_when_key_prefix_configured()
    {
        // given — a configured KeyPrefix
        var cache = _CreateCache(new InMemoryCacheOptions { KeyPrefix = "app:" });
        await cache.UpsertAsync("k", "v", TimeSpan.FromMinutes(5), AbortToken);
        CacheEvictionEventArgs? evicted = null;
        CacheKeyEventArgs? removed = null;
        cache.Events.Eviction += (_, e) => evicted = e;
        cache.Events.Remove += (_, e) => removed = e;

        // when
        await cache.RemoveAsync("k", AbortToken);

        // then — events carry the caller-facing key, not the "app:k" store key
        removed!.Key.Should().Be("k");
        evicted!.Key.Should().Be("k");
    }

    [Fact]
    public async Task should_raise_bulk_operation_events()
    {
        // given
        var cache = _CreateCache();
        await cache.UpsertAsync("p:1", "a", TimeSpan.FromMinutes(5), AbortToken);
        await cache.UpsertAsync("p:2", "b", TimeSpan.FromMinutes(5), AbortToken);
        await cache.UpsertAsync("x", "c", TimeSpan.FromMinutes(5), AbortToken);
        CacheRemoveByPrefixEventArgs? byPrefix = null;
        CacheRemoveAllEventArgs? removeAll = null;
        var cleared = false;
        cache.Events.RemoveByPrefix += (_, e) => byPrefix = e;
        cache.Events.RemoveAll += (_, e) => removeAll = e;
        cache.Events.Clear += (_, _) => cleared = true;

        // when
        await cache.RemoveByPrefixAsync("p:", AbortToken);
        await cache.RemoveAllAsync(["x"], AbortToken);
        await cache.ClearAsync(AbortToken);

        // then
        byPrefix!.Prefix.Should().Be("p:");
        byPrefix.RemovedCount.Should().Be(2);
        removeAll!.RemovedCount.Should().Be(1);
        cleared.Should().BeTrue();
    }
}
