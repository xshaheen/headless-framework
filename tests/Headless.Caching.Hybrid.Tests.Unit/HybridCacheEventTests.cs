// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Caching;
using Headless.Messaging;
using Headless.Testing.Tests;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

public sealed class HybridCacheEventTests : TestBase
{
    private readonly FakeTimeProvider _timeProvider = new();
    private readonly List<object> _disposables = [];

    private (HybridCache cache, IInMemoryCache l1, IRemoteCache l2) _CreateCache()
    {
        var l1 = new InMemoryCache(_timeProvider, new InMemoryCacheOptions { CloneValues = true });
        var l2Inner = new InMemoryCache(_timeProvider, new InMemoryCacheOptions { CloneValues = true });
        var l2 = new InMemoryRemoteCacheAdapter(l2Inner);

        var publisher = Substitute.For<IBus>();
        publisher
            .PublishAsync(Arg.Any<CacheInvalidationMessage>(), Arg.Any<PublishOptions?>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var cache = new HybridCache(
            l1,
            l2,
            publisher,
            new HybridCacheOptions(),
            timeProvider: _timeProvider,
            eventsConfig: new CacheEventsConfig { SyncHandlers = true }
        );

        _disposables.Add(l1);
        _disposables.Add(l2Inner);

        return (cache, l1, l2);
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        foreach (var disposable in _disposables)
        {
            switch (disposable)
            {
                case IAsyncDisposable a:
                    await a.DisposeAsync().ConfigureAwait(false);
                    break;
                case IDisposable s:
                    s.Dispose();
                    break;
            }
        }

        _disposables.Clear();
        await base.DisposeAsyncCore().ConfigureAwait(false);
    }

    private static CacheEntryOptions _Options() => new() { Duration = TimeSpan.FromMinutes(5) };

    [Fact]
    public async Task should_raise_per_tier_events_on_l1_miss_l2_hit()
    {
        // given — a value in both tiers, then L1 wiped so the next read misses L1 and hits L2
        var (cache, l1, _) = _CreateCache();
        var key = Faker.Random.AlphaNumeric(8);
        await cache.GetOrAddAsync<string>(key, _ => new("v"), _Options(), AbortToken);
        await l1.FlushAsync(AbortToken);

        var memoryMiss = false;
        var distributedHit = false;
        var rootHits = new ConcurrentBag<CacheHitEventArgs>();
        cache.Events.Memory!.Miss += (_, _) => memoryMiss = true;
        cache.Events.Distributed!.Hit += (_, _) => distributedHit = true;
        cache.Events.Hit += (_, e) => rootHits.Add(e);

        // when — L1 miss, L2 hit
        var result = await cache.GetOrAddAsync<string>(key, _ => throw new(), _Options(), AbortToken);

        // then — per-tier signals fire, and the aggregate root hit fires once with tier=hybrid (no double count)
        result.Value.Should().Be("v");
        memoryMiss.Should().BeTrue();
        distributedHit.Should().BeTrue();
        rootHits.Should().ContainSingle(h => h.Tier == CacheTier.Hybrid);
    }

    [Fact]
    public async Task should_raise_root_direct_op_events()
    {
        // given
        var (cache, _, _) = _CreateCache();
        var key = Faker.Random.AlphaNumeric(8);
        CacheKeyEventArgs? set = null;
        CacheKeyEventArgs? removed = null;
        cache.Events.Set += (_, e) => set = e;
        cache.Events.Remove += (_, e) => removed = e;

        // when
        await cache.UpsertAsync(key, "v", TimeSpan.FromMinutes(5), AbortToken);
        await cache.RemoveAsync(key, AbortToken);

        // then
        set!.Key.Should().Be(key);
        set.Tier.Should().Be(CacheTier.Hybrid);
        removed!.Key.Should().Be(key);
    }

    [Fact]
    public async Task should_raise_invalidation_publish_events()
    {
        // given
        var (cache, _, _) = _CreateCache();
        var invalidations = new ConcurrentBag<CacheInvalidationEventArgs>();
        cache.Events.Invalidation += (_, e) => invalidations.Add(e);

        // when
        await cache.RemoveByTagAsync("tag-1", AbortToken);
        await cache.ClearAsync(AbortToken);
        await cache.FlushAsync(AbortToken);

        // then
        invalidations
            .Should()
            .ContainSingle(i =>
                i.Kind == CacheInvalidationKind.Tag
                && i.Direction == CacheInvalidationDirection.Publish
                && i.Tag == "tag-1"
            );
        invalidations
            .Should()
            .Contain(i => i.Kind == CacheInvalidationKind.Clear && i.Direction == CacheInvalidationDirection.Publish);
        invalidations
            .Should()
            .Contain(i => i.Kind == CacheInvalidationKind.Flush && i.Direction == CacheInvalidationDirection.Publish);
    }

    [Fact]
    public async Task should_raise_invalidation_receive_event_from_peer_message()
    {
        // given
        var (cache, _, _) = _CreateCache();
        CacheInvalidationEventArgs? received = null;
        cache.Events.Invalidation += (_, e) => received = e;

        // when — a peer broadcasts a flush-all invalidation
        var message = new CacheInvalidationMessage { InstanceId = "peer", FlushAll = true };
        await cache.HandleInvalidationAsync(message, AbortToken);

        // then
        received.Should().NotBeNull();
        received!.Kind.Should().Be(CacheInvalidationKind.Flush);
        received.Direction.Should().Be(CacheInvalidationDirection.Receive);
    }
}
