// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Messaging;
using Headless.Testing.Tests;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

/// <summary>
/// Regression tests for the stale <c>_minExpiryItem</c> pointer bug: the expiry-sweep path inside
/// <see cref="HybridCacheRecoveryQueue.ProcessAsync"/> was removing expired items without nulling the
/// incremental-minimum pointer. The next queue-full <c>Enqueue</c> would call <c>_FindEarliestExpiry</c>,
/// get the stale pointer (fast path), try to evict the already-removed item (no-op), then add the new
/// item anyway — causing <c>Count</c> to overshoot <c>AutoRecoveryMaxItems</c>.
/// </summary>
public sealed class HybridCacheRecoveryQueueMinExpiryStaleTests : TestBase
{
    private readonly FakeTimeProvider _timeProvider = new();

    private (HybridCache cache, TogglableRemoteCache l2) _CreateCache(int maxItems)
    {
        var options = new HybridCacheOptions { EnableAutoRecovery = true, AutoRecoveryMaxItems = maxItems };

        var l1 = new InMemoryCache(_timeProvider, new InMemoryCacheOptions { CloneValues = true });
        var l2 = new TogglableRemoteCache(_timeProvider);

        var publisher = Substitute.For<IBus>();
        publisher
            .PublishAsync(Arg.Any<CacheInvalidationMessage>(), Arg.Any<PublishOptions?>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var cache = new HybridCache(l1, l2, publisher, options, timeProvider: _timeProvider);

        return (cache, l2);
    }

    [Fact]
    public async Task count_must_not_overshoot_max_items_after_expiry_sweep_removes_tracked_minimum()
    {
        // given — capacity of 3; L2 is down so every write queues a recovery item
        const int maxItems = 3;
        var (cache, l2) = _CreateCache(maxItems);
        await using var _ = cache;

        l2.FailWrites = true;

        // Fill the queue: the item with the shortest TTL becomes _minExpiryItem.
        // Enqueue order: short (2 min) first so it becomes the tracked minimum,
        // then two items with longer TTLs to fill the remaining slots.
        await cache.GetOrAddAsync("key-short", _ => new ValueTask<int?>(1), TimeSpan.FromMinutes(2), AbortToken);
        await cache.GetOrAddAsync("key-long-a", _ => new ValueTask<int?>(2), TimeSpan.FromMinutes(10), AbortToken);
        await cache.GetOrAddAsync("key-long-b", _ => new ValueTask<int?>(3), TimeSpan.FromMinutes(10), AbortToken);

        cache.RecoveryQueue!.Count.Should().Be(maxItems);
        cache.RecoveryQueue.Contains("key-short").Should().BeTrue("the short-TTL item must be in the queue");

        // when — advance time past the short item's recovery window so the sweep inside ProcessAsync
        // removes it, leaving _minExpiryItem pointing at the now-deleted item (the bug).
        _timeProvider.Advance(TimeSpan.FromMinutes(3));
        await cache.RecoveryQueue.ProcessAsync(AbortToken);

        // The short-TTL item was swept; the queue now holds the two long items.
        cache.RecoveryQueue.Contains("key-short").Should().BeFalse("the expired item must have been swept");
        cache.RecoveryQueue.Count.Should().Be(2);

        // and — enqueue two new items with longer TTLs to push the queue back to (and then over) capacity.
        // Without the fix, both would be admitted because _FindEarliestExpiry returns the stale pointer,
        // TryRemove is a no-op, and the new item is added unconditionally — making Count exceed max_items.
        await cache.GetOrAddAsync("key-new-1", _ => new ValueTask<int?>(4), TimeSpan.FromMinutes(15), AbortToken);
        await cache.GetOrAddAsync("key-new-2", _ => new ValueTask<int?>(5), TimeSpan.FromMinutes(15), AbortToken);

        // then — Count must never exceed the configured cap
        cache
            .RecoveryQueue.Count.Should()
            .BeLessThanOrEqualTo(
                maxItems,
                "the queue must not overshoot AutoRecoveryMaxItems after the expiry sweep removes the tracked minimum"
            );
    }
}
