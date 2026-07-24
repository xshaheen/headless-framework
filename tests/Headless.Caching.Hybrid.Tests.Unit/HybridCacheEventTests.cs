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

        // Per-tier Events.Memory.*/Events.Distributed.* always dispatch on a background task (even with
        // SyncHandlers), so observe them via a TaskCompletionSource rather than a synchronous flag.
        var memoryMiss = new TaskCompletionSource();
        var distributedHit = new TaskCompletionSource();
        var rootHits = new ConcurrentBag<CacheHitEventArgs>();
        using var _1 = cache.Events.Memory!.Miss.AddHandler(_ => memoryMiss.TrySetResult());
        using var _2 = cache.Events.Distributed!.Hit.AddHandler(_ => distributedHit.TrySetResult());
        using var _3 = cache.Events.Hit.AddHandler(e => rootHits.Add(e));

        // when — L1 miss, L2 hit
        var result = await cache.GetOrAddAsync<string>(key, _ => throw new(), _Options(), AbortToken);

        // then — per-tier signals fire (background), and the aggregate root hit fires once (honor-sync, inline) with
        // tier=hybrid (no double count)
        result.Value.Should().Be("v");
        await memoryMiss.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
        await distributedHit.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
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
        using var _1 = cache.Events.Set.AddHandler(e => set = e);
        using var _2 = cache.Events.Remove.AddHandler(e => removed = e);

        // when
        await cache.UpsertAsync(key, "v", TimeSpan.FromMinutes(5), AbortToken);
        await cache.RemoveAsync(key, AbortToken);

        // then
        set!.Key.Should().Be(key);
        set.Tier.Should().Be(CacheTier.Hybrid);
        removed!.Key.Should().Be(key);
    }

    [Fact]
    public async Task should_raise_remove_on_successful_remove_if_equal()
    {
        // given
        var (cache, _, _) = _CreateCache();
        var key = Faker.Random.AlphaNumeric(8);
        await cache.UpsertAsync(key, "v", TimeSpan.FromMinutes(5), AbortToken);
        CacheKeyEventArgs? removed = null;
        using var _ = cache.Events.Remove.AddHandler(e => removed = e);

        // when — a matching compare-and-delete succeeds
        var result = await cache.RemoveIfEqualAsync(key, "v", AbortToken);

        // then — the hybrid emits the root Remove event (parity with InMemory/Redis)
        result.Should().BeTrue();
        removed!.Key.Should().Be(key);
    }

    [Fact]
    public async Task should_not_raise_set_when_both_tiers_skipped()
    {
        // given — a positive-duration write that skips both tiers writes nothing
        var (cache, _, _) = _CreateCache();
        var setFired = false;
        using var _ = cache.Events.Set.AddHandler(_ => setFired = true);

        // when
        await cache.UpsertEntryAsync(
            "k",
            "v",
            new CacheEntryOptions
            {
                Duration = TimeSpan.FromMinutes(5),
                SkipMemoryCacheWrite = true,
                SkipDistributedCacheWrite = true,
            },
            AbortToken
        );

        // then — no Set is reported when neither tier was written
        setFired.Should().BeFalse();
    }

    [Fact]
    public async Task should_raise_invalidation_publish_events()
    {
        // given
        var (cache, _, _) = _CreateCache();
        var invalidations = new ConcurrentBag<CacheInvalidationEventArgs>();
        using var _ = cache.Events.Invalidation.AddHandler(e => invalidations.Add(e));

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
        using var _ = cache.Events.Invalidation.AddHandler(e => received = e);

        // when — a peer broadcasts a flush-all invalidation
        var message = new CacheInvalidationMessage { InstanceId = "peer", FlushAll = true };
        await cache.HandleInvalidationAsync(message, AbortToken);

        // then
        received.Should().NotBeNull();
        received!.Kind.Should().Be(CacheInvalidationKind.Flush);
        received.Direction.Should().Be(CacheInvalidationDirection.Receive);
    }
}
