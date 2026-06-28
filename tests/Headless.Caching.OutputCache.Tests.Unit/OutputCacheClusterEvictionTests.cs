// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

/// <summary>
/// The headline guarantee: a tag evicted through the store on node A makes node B's cached entry a miss, carried
/// by the backplane. Uses the engine's two-node convergence pattern (a synchronous in-memory bus over two
/// <see cref="HybridCache"/> nodes sharing one L2 backend) wrapped in <see cref="HeadlessOutputCacheStore"/>, so
/// no broker / Testcontainers is needed — the new surface under test is only the store wrapper over each node.
///
/// Each node caches its own response via its own <c>SetAsync</c> — the real per-instance output-cache behavior,
/// where every app instance renders and stores the response under the same key+tags. The engine's logical tag
/// invalidation version-pins entries by their own node's write (a plain key read-backfill does not re-tag the
/// promoted L1 copy), so the cluster guarantee is proven against entries each node wrote itself.
/// </summary>
public sealed class OutputCacheClusterEvictionTests
{
    private static readonly TimeSpan _Ttl = TimeSpan.FromMinutes(30);
    private static readonly byte[] _Payload = [0xCA, 0xFE, 0xBA, 0xBE];

    [Fact]
    public async Task evict_by_tag_on_node_a_makes_the_cached_entry_a_miss_on_node_b_through_the_backplane()
    {
        // given — both instances have independently cached the same tagged response in their own L1
        await using var harness = new TwoNodeStoreHarness();
        const string key = "ock:products:1";
        await harness.A.Store.SetAsync(key, _Payload, ["products"], _Ttl, CancellationToken.None);
        await harness.B.Store.SetAsync(key, _Payload, ["products"], _Ttl, CancellationToken.None);
        (await harness.B.Store.GetAsync(key, CancellationToken.None)).Should().Equal(_Payload);

        // when — node A evicts the tag (marker must postdate the entries' CreatedAt)
        harness.Time.Advance(TimeSpan.FromSeconds(1));
        await harness.A.Store.EvictByTagAsync("products", CancellationToken.None);

        // then — node B observes the eviction without any call to its own store's evict
        (await harness.B.Store.GetAsync(key, CancellationToken.None))
            .Should()
            .BeNull();
    }

    [Fact]
    public async Task entry_under_a_different_tag_survives_the_eviction_on_node_b()
    {
        // given — node B has cached two responses under different tags
        await using var harness = new TwoNodeStoreHarness();
        var survivor = new byte[] { 1, 1, 2, 3, 5 };
        await harness.B.Store.SetAsync("ock:products:1", _Payload, ["products"], _Ttl, CancellationToken.None);
        await harness.B.Store.SetAsync("ock:catalog:1", survivor, ["catalog"], _Ttl, CancellationToken.None);

        // when
        harness.Time.Advance(TimeSpan.FromSeconds(1));
        await harness.A.Store.EvictByTagAsync("products", CancellationToken.None);

        // then — only the "products" entry is gone; the "catalog" entry is untouched (no over-eviction)
        (await harness.B.Store.GetAsync("ock:products:1", CancellationToken.None))
            .Should()
            .BeNull();
        (await harness.B.Store.GetAsync("ock:catalog:1", CancellationToken.None)).Should().Equal(survivor);
    }

    [Fact]
    public async Task node_a_own_entry_is_also_a_miss_after_its_own_eviction()
    {
        // given — sanity that the eviction is not peer-only: node A's own L1 entry is dropped too
        await using var harness = new TwoNodeStoreHarness();
        const string key = "ock:products:1";
        await harness.A.Store.SetAsync(key, _Payload, ["products"], _Ttl, CancellationToken.None);
        (await harness.A.Store.GetAsync(key, CancellationToken.None)).Should().Equal(_Payload);

        // when
        harness.Time.Advance(TimeSpan.FromSeconds(1));
        await harness.A.Store.EvictByTagAsync("products", CancellationToken.None);

        // then
        (await harness.A.Store.GetAsync(key, CancellationToken.None))
            .Should()
            .BeNull();
    }
}

/// <summary>
/// Two store-wrapped <see cref="HybridCache"/> nodes (A and B), each with its own L1, sharing one in-memory L2
/// backend and one synchronous backplane bus, all driven by a single <see cref="FakeTimeProvider"/>.
/// </summary>
internal sealed class TwoNodeStoreHarness : IAsyncDisposable
{
    private readonly InMemoryCache _sharedL2Backend;

    public TwoNodeStoreHarness()
    {
        _sharedL2Backend = new InMemoryCache(Time, new InMemoryCacheOptions { CloneValues = true });
        A = _CreateNode("node-a");
        B = _CreateNode("node-b");
        Bus.Attach(A.Cache);
        Bus.Attach(B.Cache);
    }

    public FakeTimeProvider Time { get; } = new();

    public FakeBackplaneBus Bus { get; } = new();

    public StoreNode A { get; }

    public StoreNode B { get; }

    private StoreNode _CreateNode(string instanceId)
    {
        var l1 = new InMemoryCache(Time, new InMemoryCacheOptions { CloneValues = true });
        var l2 = new InMemoryRemoteCacheAdapter(_sharedL2Backend);
        var cache = new HybridCache(
            l1,
            l2,
            Bus,
            new HybridCacheOptions { InstanceId = instanceId },
            NullLogger<HybridCache>.Instance,
            Time
        );
        var store = new HeadlessOutputCacheStore(cache, Options.Create(new HeadlessOutputCacheStoreOptions()));

        return new StoreNode(cache, l1, store);
    }

    public async ValueTask DisposeAsync()
    {
        await A.Cache.DisposeAsync();
        await B.Cache.DisposeAsync();
        A.L1.Dispose();
        B.L1.Dispose();
        _sharedL2Backend.Dispose();
    }
}

/// <summary>One node: its hybrid cache, its private L1, and the output-cache store wrapping the cache.</summary>
internal sealed record StoreNode(HybridCache Cache, InMemoryCache L1, HeadlessOutputCacheStore Store);
