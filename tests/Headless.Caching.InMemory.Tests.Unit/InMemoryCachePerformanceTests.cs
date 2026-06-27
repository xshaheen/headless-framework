// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
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

    [Fact]
    public async Task maintenance_overhead_should_be_independent_of_cache_size()
    {
        // given - a large cache
        using var cache = _CreateCache();
        var count = 100_000;

        for (var i = 0; i < count; i++)
        {
            await cache.UpsertAsync($"key{i}", "value", TimeSpan.FromHours(1), AbortToken);
        }

        // when - trigger maintenance
        var sw = Stopwatch.StartNew();

        // Use reflection to trigger the private maintenance method if needed,
        // but here we just trigger it normally and wait for completion.
        // Actually, _StartMaintenanceAsync is already called on every write.
        // We want to measure the execution time of _DoMaintenanceAsync specifically.

        var maintenanceMethod = typeof(InMemoryCache).GetMethod(
            "_DoMaintenanceAsync",
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly,
            null,
            Type.EmptyTypes,
            null
        );

        sw.Restart();
        await (Task)maintenanceMethod!.Invoke(cache, null)!;
        sw.Stop();

        var timeWith100K = sw.ElapsedMilliseconds;

        // Add more items
        for (var i = count; i < count * 2; i++)
        {
            await cache.UpsertAsync($"key{i}", "value", TimeSpan.FromHours(1), AbortToken);
        }

        sw.Restart();
        await (Task)maintenanceMethod!.Invoke(cache, null)!;
        sw.Stop();

        var timeWith200K = sw.ElapsedMilliseconds;

        // then - time should NOT have doubled (O(N) would double)
        // With O(1)/O(log N) expiration check, it should be near-zero
        // since nothing is actually expired.

        timeWith100K.Should().BeLessThan(100, "maintenance should be fast even with 100k items");
        timeWith200K.Should().BeLessThan(100, "maintenance should be fast even with 200k items");

        // The difference should be minimal
        Math.Abs(timeWith200K - timeWith100K)
            .Should()
            .BeLessThan(50, "overhead increase should be negligible between 100k and 200k items");
    }

    // KTD-9 acceptance gate: sliding re-arm must be throttled, never unconditional. A hot key read many times
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
        var afterBurst = await store.TryGetEntryAsync<string>(key, AbortToken);
        afterBurst.LogicalExpiresAt.Should().Be(now.Add(sliding));

        // and when - more than half the idle window elapses, the next read re-arms exactly once...
        _timeProvider.Advance(TimeSpan.FromMilliseconds(600));
        var rearmAt = _timeProvider.GetUtcNow().UtcDateTime;
        (await cache.GetAsync<string>(key, AbortToken)).Value.Should().Be("value");

        var afterThreshold = await store.TryGetEntryAsync<string>(key, AbortToken);
        afterThreshold.LogicalExpiresAt.Should().Be(rearmAt.Add(sliding));

        // ...and an immediate follow-up read is throttled again (back above the half-window).
        (await cache.GetAsync<string>(key, AbortToken))
            .Value.Should()
            .Be("value");
        var afterSecond = await store.TryGetEntryAsync<string>(key, AbortToken);
        afterSecond.LogicalExpiresAt.Should().Be(rearmAt.Add(sliding));
    }
}
