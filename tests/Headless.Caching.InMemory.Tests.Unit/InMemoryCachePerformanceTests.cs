// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections;
using System.Reflection;
using Headless.Caching;
using Headless.Testing.Tests;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

public sealed class InMemoryCachePerformanceTests : TestBase
{
    private readonly FakeTimeProvider _timeProvider = new();

    private InMemoryCache _CreateCache(InMemoryCacheOptions? options = null)
    {
        options ??= new InMemoryCacheOptions();
        return new InMemoryCache(_timeProvider, options);
    }

    // Acceptance gate: sliding re-arm must be throttled, never unconditional. A hot key read many times
    // within the first half of its idle window must NOT be re-written on every read (which would hammer the
    // store under load); the logical deadline only advances once at least half the window has elapsed.
    [Fact]
    public async Task sliding_rearm_is_throttled_on_a_hot_key_and_does_not_rewrite_on_every_read()
    {
        // given - a sliding entry whose idle window (1s) is well inside the absolute cap (30s)
        using var cache = _CreateCache();
        var store = (IFactoryCacheStore)cache;
        var key = Faker.Random.AlphaNumeric(10);
        var sliding = TimeSpan.FromSeconds(1);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var options = new CacheEntryOptions { Duration = TimeSpan.FromSeconds(30), SlidingExpiration = sliding };
        await cache.GetOrAddAsync(key, _ => new ValueTask<string?>("value"), options, AbortToken);

        // when - a burst of hot reads with no time advance (more than half the window still remains)
        for (var i = 0; i < 1_000; i++)
        {
            (await cache.GetAsync<string>(key, AbortToken)).Value.Should().Be("value");
        }

        // then - the throttle suppressed every re-arm: the logical deadline is unchanged from the initial set.
        // TryGetEntryAsync is a non-re-arming inspection read, so it does not perturb the measurement.
        var afterBurst = await store.TryGetEntryAsync<string>(key, cancellationToken: AbortToken);
        afterBurst.LogicalExpiresAt.Should().Be(now.Add(sliding));

        // and when - more than half the idle window elapses, the next read re-arms exactly once...
        _timeProvider.Advance(TimeSpan.FromMilliseconds(600));
        var rearmAt = _timeProvider.GetUtcNow().UtcDateTime;
        (await cache.GetAsync<string>(key, AbortToken)).Value.Should().Be("value");

        var afterThreshold = await store.TryGetEntryAsync<string>(key, cancellationToken: AbortToken);
        afterThreshold.LogicalExpiresAt.Should().Be(rearmAt.Add(sliding));

        // ...and an immediate follow-up read is throttled again (back above the half-window).
        (await cache.GetAsync<string>(key, AbortToken))
            .Value.Should()
            .Be("value");
        var afterSecond = await store.TryGetEntryAsync<string>(key, cancellationToken: AbortToken);
        afterSecond.LogicalExpiresAt.Should().Be(rearmAt.Add(sliding));
    }

    [Fact]
    public async Task should_reap_every_expired_entry_in_one_maintenance_sweep()
    {
        // given - far more expired write-once keys than one compaction may evict
        using var cache = _CreateCache(new InMemoryCacheOptions { MaxItems = null, MaxEvictionsPerCompaction = 10 });

        for (var i = 0; i < 200; i++)
        {
            await cache.UpsertAsync($"short-{i}", i, TimeSpan.FromSeconds(1), AbortToken);
        }

        _timeProvider.Advance(TimeSpan.FromSeconds(2));

        // when
        await _RunMaintenanceAsync(cache);

        // then - the eviction cap bounds capacity compaction, not expiry reaping
        _GetResidentCount(cache).Should().Be(0);
    }

    [Fact]
    public async Task should_reap_expired_entries_before_evicting_live_ones_when_capacity_compacts()
    {
        // given - a full cache whose least recently used entries are live and whose newer ones have expired. An exact
        // LRU sample and a maintenance interval longer than the test keep the background sweep out of the picture.
        using var cache = _CreateCache(
            new InMemoryCacheOptions
            {
                MaxItems = 10,
                EvictionSampleSize = 100,
                MaintenanceInterval = TimeSpan.FromHours(1),
            }
        );

        // The first write schedules the interval's one background maintenance pass; let it finish before the clock
        // moves, or it would read the advanced clock and reap the expired entries before compaction runs.
        await cache.UpsertAsync("live-0", 0, TimeSpan.FromHours(1), AbortToken);
        await _WaitForMaintenanceIdleAsync(cache);

        for (var i = 1; i < 5; i++)
        {
            await cache.UpsertAsync($"live-{i}", i, TimeSpan.FromHours(1), AbortToken);
        }

        _timeProvider.Advance(TimeSpan.FromSeconds(1));

        for (var i = 0; i < 5; i++)
        {
            await cache.UpsertAsync($"short-{i}", i, TimeSpan.FromSeconds(1), AbortToken);
        }

        _timeProvider.Advance(TimeSpan.FromSeconds(5));

        // when - one more write pushes the cache over MaxItems
        await cache.UpsertAsync("fresh", 42, TimeSpan.FromHours(1), AbortToken);

        // then - the expired entries made room; no live entry was evicted
        for (var i = 0; i < 5; i++)
        {
            (await cache.GetAsync<int>($"live-{i}", AbortToken)).HasValue.Should().BeTrue($"live-{i} is still live");
        }

        (await cache.GetAsync<int>("fresh", AbortToken)).Value.Should().Be(42);
        _GetResidentCount(cache).Should().Be(6);
    }

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

    private static async Task _WaitForMaintenanceIdleAsync(InMemoryCache cache)
    {
        var field = typeof(InMemoryCache).GetField(
            "_maintenanceRunning",
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly
        );
        field.Should().NotBeNull();

        var deadline = DateTime.UtcNow.AddSeconds(10);

        while ((int)field!.GetValue(cache)! != 0)
        {
            DateTime.UtcNow.Should().BeBefore(deadline, "the background maintenance pass must finish");
            await Task.Delay(10, AbortToken);
        }
    }

    // Physically resident entries, expired ones included (GetCountAsync skips expired entries).
    private static int _GetResidentCount(InMemoryCache cache)
    {
        var field = typeof(InMemoryCache).GetField(
            "_memory",
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly
        );
        field.Should().NotBeNull();

        return ((ICollection)field!.GetValue(cache)!).Count;
    }
}
