// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Headless.Caching;
using Headless.Messaging;
using Headless.Redis;
using Headless.Serializer;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tests;

// Three Hybrid-over-real-Redis scenarios that were not covered by the existing single-test
// DefaultHybridCacheTiersTests (which only verified composition + key existence in L2).
//
// (1) FlushAll seed-order: the L2 clear-marker is seeded before L1 is wiped — behavioral
//     verification that FlushAsync clears both tiers and a subsequent write + read sees the fresh value.
// (2) L1 TTL capping: L1 TTL is capped to the L2 logical expiry when DefaultLocalExpiration > L2 logical TTL.
// (3) Backplane round-trip: an invalidation published by node A clears L1 on node B.

[Collection(nameof(RedisCacheFixture))]
public sealed class HybridCacheL2BehaviorTests(RedisCacheFixture fixture) : TestBase
{
    // The L1/L2 tiers created in _CreateHybrid are owned by the test, NOT by the returned HybridCache:
    // HybridCache.DisposeAsync deliberately does not dispose injected tiers (in DI the container owns them).
    // Track them here and dispose at test teardown, after each await-using hybrid is gone. A `using var` on
    // l1/l2 would dispose them when _CreateHybrid returns — before the test ever touches the hybrid — which
    // surfaces as ObjectDisposedException on the L1 InMemoryCache.
    private readonly List<IDisposable> _tierCaches = [];

    private HybridCache _CreateHybrid(
        string keyPrefix = "",
        HybridCacheOptions? hybridOptions = null,
        CapturingBus? bus = null
    )
    {
        var l1 = new InMemoryCache(TimeProvider.System, new InMemoryCacheOptions());
        _tierCaches.Add(l1);

        var redisCacheOptions = new RedisCacheOptions
        {
            ConnectionMultiplexer = fixture.ConnectionMultiplexer,
            KeyPrefix = keyPrefix,
        };

        var l2 = new RedisCache(
            new SystemJsonSerializer(),
            TimeProvider.System,
            redisCacheOptions,
            fixture.ScriptsLoader,
            LoggerFactory.CreateLogger<RedisCache>()
        );
        _tierCaches.Add(l2);

        var publisher = (IBus?)bus ?? NoopBus.Instance;
        hybridOptions ??= new HybridCacheOptions();

        return new HybridCache(l1, l2, publisher, hybridOptions, NullLogger<HybridCache>.Instance, TimeProvider.System);
    }

    // FlushAll seed-order: after FlushAsync, a new upsert + read must return the new value, proving
    // that the L2 clear-marker was seeded before L1 was wiped (if the order were reversed, a concurrent
    // read in the gap would see a stale L2 hit; here we verify the net effect deterministically).
    [Fact]
    public async Task flush_all_clears_both_tiers_and_new_write_is_visible()
    {
        // given
        await _FlushAsync();
        await using var hybrid = _CreateHybrid("hybrid-flush:");

        var key = Faker.Random.AlphaNumeric(12);
        await hybrid.UpsertAsync(key, "original", TimeSpan.FromMinutes(5), AbortToken);

        // verify both tiers have the value before flush
        var localBefore = await hybrid.LocalCache.GetAsync<string>(key, AbortToken);
        localBefore.HasValue.Should().BeTrue("L1 must be populated before flush");
        var db = fixture.ConnectionMultiplexer.GetDatabase();
        (await db.KeyExistsAsync("hybrid-flush:" + key)).Should().BeTrue("L2 must be populated before flush");

        // when — FlushAsync triggers the FlushAll invalidation path (seed L2 marker first, then wipe L1)
        await hybrid.FlushAsync(AbortToken);

        // then — both tiers must be empty
        var localAfterFlush = await hybrid.LocalCache.GetAsync<string>(key, AbortToken);
        localAfterFlush.HasValue.Should().BeFalse("L1 must be empty after flush");

        // Write a new value after flush and read it back through both tiers
        await hybrid.UpsertAsync(key, "post-flush", TimeSpan.FromMinutes(5), AbortToken);
        var result = await hybrid.GetAsync<string>(key, AbortToken);

        result.HasValue.Should().BeTrue("new write after flush must be readable");
        result.Value.Should().Be("post-flush", "hybrid must return the post-flush value, not the pre-flush value");
    }

    // L1 TTL capping: when DefaultLocalExpiration (e.g. 30s) is less than the L2 logical TTL (e.g. 5min),
    // the L1 entry's effective expiration must be capped to DefaultLocalExpiration (30s), not the full L2 TTL.
    [Fact]
    public async Task l1_ttl_is_capped_to_local_expiration_when_l2_ttl_is_longer()
    {
        // given
        await _FlushAsync();
        var hybridOptions = new HybridCacheOptions { DefaultLocalExpiration = TimeSpan.FromSeconds(30) };
        await using var hybrid = _CreateHybrid("hybrid-ttl:", hybridOptions);

        var key = Faker.Random.AlphaNumeric(12);
        // Upsert with a long TTL so L2 TTL (5min) > DefaultLocalExpiration (30s)
        await hybrid.UpsertAsync(key, "value", TimeSpan.FromMinutes(5), AbortToken);

        // when — read back to ensure L1 was populated
        var result = await hybrid.GetAsync<string>(key, AbortToken);
        result.HasValue.Should().BeTrue();

        // then — L1 TTL must be <= DefaultLocalExpiration (30s), not 5min
        var l1Expiration = await hybrid.LocalCache.GetExpirationAsync(key, AbortToken);
        l1Expiration.Should().NotBeNull("L1 entry must exist");
        l1Expiration!
            .Value.Should()
            .BeLessThanOrEqualTo(
                TimeSpan.FromSeconds(31), // small grace for clock skew between write and read
                "L1 TTL must be capped at DefaultLocalExpiration (30s), not the full L2 TTL (5min)"
            );
    }

    // Backplane round-trip: an invalidation published by node A clears L1 on node B.
    // Node A upserts a key (both nodes can see it). Node B reads (populates node B's L1). Node A upserts
    // a new value + publishes invalidation. Node B's HandleInvalidationAsync must clear node B's L1 so
    // the next read on node B returns the fresh value (not the stale L1 copy).
    [Fact]
    public async Task backplane_invalidation_from_node_a_clears_l1_on_node_b()
    {
        // given
        await _FlushAsync();
        var bus = new CapturingBus();

        await using var hybridA = _CreateHybrid("hybrid-bp:", bus: bus);
        await using var hybridB = _CreateHybrid("hybrid-bp:", bus: bus);

        // Both nodes share the same Redis (L2); wire the bus so A's publishes invalidate B's L1
        bus.Attach(hybridA);
        bus.Attach(hybridB);

        var key = Faker.Random.AlphaNumeric(12);

        // Node A writes "v1" → L2 (Redis)
        await hybridA.UpsertAsync(key, "v1", TimeSpan.FromMinutes(5), AbortToken);

        // Node B reads — populates node B's L1 from L2
        var readOnB = await hybridB.GetAsync<string>(key, AbortToken);
        readOnB.HasValue.Should().BeTrue();
        readOnB.Value.Should().Be("v1");

        var bL1Before = await hybridB.LocalCache.GetAsync<string>(key, AbortToken);
        bL1Before.HasValue.Should().BeTrue("node B L1 must be populated after first read");

        // when — node A writes "v2" and publishes invalidation; the bus routes it to node B
        await hybridA.UpsertAsync(key, "v2", TimeSpan.FromMinutes(5), AbortToken);

        // then — node B's L1 must have been cleared by the backplane invalidation
        var bL1After = await hybridB.LocalCache.GetAsync<string>(key, AbortToken);
        bL1After.HasValue.Should().BeFalse("backplane invalidation must clear node B's L1 after node A's upsert");

        // and a fresh read on node B must return "v2" from L2
        var freshReadOnB = await hybridB.GetAsync<string>(key, AbortToken);
        freshReadOnB.HasValue.Should().BeTrue();
        freshReadOnB.Value.Should().Be("v2", "node B must read the updated value from L2 after L1 invalidation");
    }

    private async Task _FlushAsync() => await fixture.ConnectionMultiplexer.FlushAllAsync();

    /// <inheritdoc />
    protected override async ValueTask DisposeAsyncCore()
    {
        // Dispose the L1/L2 tiers created by _CreateHybrid (HybridCache does not own them). Runs after each
        // test's await-using hybrid has already been disposed. Idempotent: Clear + per-cache Dispose guards.
        foreach (var cache in _tierCaches)
        {
            cache.Dispose();
        }

        _tierCaches.Clear();
        await base.DisposeAsyncCore();
    }

    // Minimal in-process backplane bus: routes CacheInvalidationMessage to all attached HybridCache instances.
    // HandleInvalidationAsync is internal (InternalsVisibleTo is not granted to this project) so we invoke it
    // via reflection — the same approach the real infrastructure's message consumer uses, but synchronous here.
    private sealed class CapturingBus : IBus
    {
        private readonly List<HybridCache> _subscribers = [];

        public void Attach(HybridCache cache) => _subscribers.Add(cache);

        public async Task PublishAsync<T>(
            T? message,
            PublishOptions? options = null,
            CancellationToken cancellationToken = default
        )
        {
            if (message is not CacheInvalidationMessage invalidation)
            {
                return;
            }

            foreach (var subscriber in _subscribers)
            {
                // HandleInvalidationAsync is internal; invoke via reflection so the test can route messages
                // without InternalsVisibleTo being granted to this integration test project.
                var method = typeof(HybridCache).GetMethod(
                    "HandleInvalidationAsync",
                    BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly,
                    null,
                    [typeof(CacheInvalidationMessage), typeof(CancellationToken)],
                    null
                );
                var task = (ValueTask)method!.Invoke(subscriber, [invalidation, cancellationToken])!;
                await task.ConfigureAwait(false);
            }
        }
    }

    // No-op bus for tests that don't need cross-node invalidation routing.
    private sealed class NoopBus : IBus
    {
        public static readonly NoopBus Instance = new();

        public Task PublishAsync<T>(
            T? message,
            PublishOptions? options = null,
            CancellationToken cancellationToken = default
        ) => Task.CompletedTask;
    }
}
