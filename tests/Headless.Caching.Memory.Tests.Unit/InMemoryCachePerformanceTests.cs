// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
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

        for (int i = 0; i < count; i++)
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
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance
        );

        sw.Restart();
        await (Task)maintenanceMethod!.Invoke(cache, null)!;
        sw.Stop();

        var timeWith100k = sw.ElapsedMilliseconds;

        // Add more items
        for (int i = count; i < count * 2; i++)
        {
            await cache.UpsertAsync($"key{i}", "value", TimeSpan.FromHours(1), AbortToken);
        }

        sw.Restart();
        await (Task)maintenanceMethod!.Invoke(cache, null)!;
        sw.Stop();

        var timeWith200k = sw.ElapsedMilliseconds;

        // then - time should NOT have doubled (O(N) would double)
        // With O(1)/O(log N) expiration check, it should be near-zero
        // since nothing is actually expired.

        timeWith100k.Should().BeLessThan(100, "maintenance should be fast even with 100k items");
        timeWith200k.Should().BeLessThan(100, "maintenance should be fast even with 200k items");

        // The difference should be minimal
        Math.Abs(timeWith200k - timeWith100k)
            .Should()
            .BeLessThan(50, "overhead increase should be negligible between 100k and 200k items");
    }
}
