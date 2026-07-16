// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Headless.Caching;
using Headless.Testing.Tests;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

/// <summary>
/// Read-metering coverage for <see cref="InMemoryCache"/>'s direct <see cref="ICache"/> surface (issue #694):
/// <see cref="InMemoryCache.GetAsync{T}"/>, <see cref="InMemoryCache.GetAllAsync{T}"/>,
/// <see cref="InMemoryCache.ExistsAsync"/>, and the remove paths all now feed the shared
/// <c>headless.cache.requests</c> / <c>headless.cache.writes</c> counters at tier <c>l1</c>. The coordinator's own
/// get-or-add path (<see cref="IFactoryCacheStore.TryGetEntryAsync{T}"/>) is deliberately NOT instrumented here —
/// see the last test, which asserts a get-or-add call records exactly once (no double count).
/// </summary>
public sealed class InMemoryCacheRequestMetricsTests : TestBase
{
    private readonly FakeTimeProvider _timeProvider = new();

    private InMemoryCache _CreateCache(string cacheName)
    {
        return new InMemoryCache(_timeProvider, new InMemoryCacheOptions { CacheName = cacheName });
    }

    private static string _UniqueName()
    {
        return "test-" + Guid.NewGuid().ToString("N");
    }

    [Fact]
    public async Task should_record_hit_on_get_async_when_key_present()
    {
        // given
        var cacheName = _UniqueName();
        using var cache = _CreateCache(cacheName);
        var key = Faker.Random.AlphaNumeric(8);
        await cache.UpsertAsync(key, "value", TimeSpan.FromMinutes(5), AbortToken);

        using var metrics = new MetricCollector();

        // when
        var result = await cache.GetAsync<string>(key, AbortToken);

        // then
        result.HasValue.Should().BeTrue();

        metrics
            .Count(
                "headless.cache.requests",
                ("headless.cache.name", cacheName),
                ("headless.cache.operation", "get"),
                ("headless.cache.outcome", "hit"),
                ("headless.cache.tier", "l1")
            )
            .Should()
            .Be(1);
    }

    [Fact]
    public async Task should_record_miss_on_get_async_when_key_absent()
    {
        // given
        var cacheName = _UniqueName();
        using var cache = _CreateCache(cacheName);
        var key = Faker.Random.AlphaNumeric(8);

        using var metrics = new MetricCollector();

        // when
        var result = await cache.GetAsync<string>(key, AbortToken);

        // then
        result.HasValue.Should().BeFalse();

        metrics
            .Count(
                "headless.cache.requests",
                ("headless.cache.name", cacheName),
                ("headless.cache.operation", "get"),
                ("headless.cache.outcome", "miss"),
                ("headless.cache.tier", "l1")
            )
            .Should()
            .Be(1);
    }

    [Fact]
    public async Task should_record_hit_and_miss_counts_summing_to_batch_size_on_get_all_async_partial_hit()
    {
        // given — two keys present, one absent
        var cacheName = _UniqueName();
        using var cache = _CreateCache(cacheName);
        var hitKeyA = Faker.Random.AlphaNumeric(8);
        var hitKeyB = Faker.Random.AlphaNumeric(8);
        var missKey = Faker.Random.AlphaNumeric(8);
        await cache.UpsertAsync(hitKeyA, "a", TimeSpan.FromMinutes(5), AbortToken);
        await cache.UpsertAsync(hitKeyB, "b", TimeSpan.FromMinutes(5), AbortToken);

        using var metrics = new MetricCollector();

        // when
        var result = await cache.GetAllAsync<string>([hitKeyA, hitKeyB, missKey], AbortToken);

        // then
        result[hitKeyA].HasValue.Should().BeTrue();
        result[hitKeyB].HasValue.Should().BeTrue();
        result[missKey].HasValue.Should().BeFalse();

        var hitCount = metrics.Count(
            "headless.cache.requests",
            ("headless.cache.name", cacheName),
            ("headless.cache.operation", "get_all"),
            ("headless.cache.outcome", "hit"),
            ("headless.cache.tier", "l1")
        );

        var missCount = metrics.Count(
            "headless.cache.requests",
            ("headless.cache.name", cacheName),
            ("headless.cache.operation", "get_all"),
            ("headless.cache.outcome", "miss"),
            ("headless.cache.tier", "l1")
        );

        hitCount.Should().Be(2);
        missCount.Should().Be(1);
        (hitCount + missCount).Should().Be(3);

        // Exactly two adds per call (skip an add when its count is 0) — never per-key "get" records.
        metrics.Measurements("headless.cache.requests", ("headless.cache.name", cacheName)).Should().HaveCount(2);
    }

    [Fact]
    public async Task should_skip_the_miss_add_on_get_all_async_when_every_key_hits()
    {
        // given — an all-hit batch: the miss count is 0, so RecordRequest's count<=0 guard must skip that add.
        var cacheName = _UniqueName();
        using var cache = _CreateCache(cacheName);
        var key = Faker.Random.AlphaNumeric(8);
        await cache.UpsertAsync(key, "value", TimeSpan.FromMinutes(5), AbortToken);

        using var metrics = new MetricCollector();

        // when
        await cache.GetAllAsync<string>([key], AbortToken);

        // then — only the hit add fired.
        metrics.Measurements("headless.cache.requests", ("headless.cache.name", cacheName)).Should().ContainSingle();

        metrics
            .Count(
                "headless.cache.requests",
                ("headless.cache.name", cacheName),
                ("headless.cache.operation", "get_all"),
                ("headless.cache.outcome", "hit"),
                ("headless.cache.tier", "l1")
            )
            .Should()
            .Be(1);
    }

    [Fact]
    public async Task should_record_hit_on_exists_async_when_key_present()
    {
        // given
        var cacheName = _UniqueName();
        using var cache = _CreateCache(cacheName);
        var key = Faker.Random.AlphaNumeric(8);
        await cache.UpsertAsync(key, "value", TimeSpan.FromMinutes(5), AbortToken);

        using var metrics = new MetricCollector();

        // when
        var exists = await cache.ExistsAsync(key, AbortToken);

        // then
        exists.Should().BeTrue();

        metrics
            .Count(
                "headless.cache.requests",
                ("headless.cache.name", cacheName),
                ("headless.cache.operation", "exists"),
                ("headless.cache.outcome", "hit"),
                ("headless.cache.tier", "l1")
            )
            .Should()
            .Be(1);
    }

    [Fact]
    public async Task should_record_miss_on_exists_async_when_key_absent()
    {
        // given
        var cacheName = _UniqueName();
        using var cache = _CreateCache(cacheName);
        var key = Faker.Random.AlphaNumeric(8);

        using var metrics = new MetricCollector();

        // when
        var exists = await cache.ExistsAsync(key, AbortToken);

        // then
        exists.Should().BeFalse();

        metrics
            .Count(
                "headless.cache.requests",
                ("headless.cache.name", cacheName),
                ("headless.cache.operation", "exists"),
                ("headless.cache.outcome", "miss"),
                ("headless.cache.tier", "l1")
            )
            .Should()
            .Be(1);
    }

    [Fact]
    public async Task should_record_write_counter_on_remove_async_when_key_removed()
    {
        // given
        var cacheName = _UniqueName();
        using var cache = _CreateCache(cacheName);
        var key = Faker.Random.AlphaNumeric(8);
        await cache.UpsertAsync(key, "value", TimeSpan.FromMinutes(5), AbortToken);

        using var metrics = new MetricCollector();

        // when
        var removed = await cache.RemoveAsync(key, AbortToken);

        // then
        removed.Should().BeTrue();

        metrics
            .Count(
                "headless.cache.writes",
                ("headless.cache.name", cacheName),
                ("headless.cache.operation", "remove"),
                ("headless.cache.tier", "l1")
            )
            .Should()
            .Be(1);
    }

    [Fact]
    public async Task should_not_record_write_counter_on_remove_async_when_key_absent()
    {
        // given — RemoveAsync's write metric mirrors the pre-existing eviction metric: only actual removals count.
        var cacheName = _UniqueName();
        using var cache = _CreateCache(cacheName);
        var key = Faker.Random.AlphaNumeric(8);

        using var metrics = new MetricCollector();

        // when
        var removed = await cache.RemoveAsync(key, AbortToken);

        // then
        removed.Should().BeFalse();
        metrics.Measurements("headless.cache.writes", ("headless.cache.name", cacheName)).Should().BeEmpty();
    }

    [Fact]
    public async Task should_record_write_counter_with_removed_count_on_remove_all_async()
    {
        // given
        var cacheName = _UniqueName();
        using var cache = _CreateCache(cacheName);
        var keyA = Faker.Random.AlphaNumeric(8);
        var keyB = Faker.Random.AlphaNumeric(8);
        var missingKey = Faker.Random.AlphaNumeric(8);
        await cache.UpsertAsync(keyA, "a", TimeSpan.FromMinutes(5), AbortToken);
        await cache.UpsertAsync(keyB, "b", TimeSpan.FromMinutes(5), AbortToken);

        using var metrics = new MetricCollector();

        // when
        var removedCount = await cache.RemoveAllAsync([keyA, keyB, missingKey], AbortToken);

        // then
        removedCount.Should().Be(2);

        metrics
            .Count(
                "headless.cache.writes",
                ("headless.cache.name", cacheName),
                ("headless.cache.operation", "remove"),
                ("headless.cache.tier", "l1")
            )
            .Should()
            .Be(2);
    }

    [Fact]
    public async Task should_record_write_counter_with_removed_count_on_remove_by_prefix_async()
    {
        // given
        var cacheName = _UniqueName();
        using var cache = _CreateCache(cacheName);
        var prefix = "order:";
        await cache.UpsertAsync($"{prefix}1", "a", TimeSpan.FromMinutes(5), AbortToken);
        await cache.UpsertAsync($"{prefix}2", "b", TimeSpan.FromMinutes(5), AbortToken);
        await cache.UpsertAsync("other-key", "c", TimeSpan.FromMinutes(5), AbortToken);

        using var metrics = new MetricCollector();

        // when
        var removedCount = await cache.RemoveByPrefixAsync(prefix, AbortToken);

        // then
        removedCount.Should().Be(2);

        metrics
            .Count(
                "headless.cache.writes",
                ("headless.cache.name", cacheName),
                ("headless.cache.operation", "remove_by_prefix"),
                ("headless.cache.tier", "l1")
            )
            .Should()
            .Be(2);
    }

    [Fact]
    public async Task should_record_single_write_counter_on_remove_by_tag_async()
    {
        // given — tag invalidation is O(1) marker bump (no member enumeration), so a single write is recorded
        // regardless of how many entries carry the tag.
        var cacheName = _UniqueName();
        using var cache = _CreateCache(cacheName);
        var tag = Faker.Random.AlphaNumeric(6);

        using var metrics = new MetricCollector();

        // when
        await cache.RemoveByTagAsync(tag, AbortToken);

        // then
        metrics
            .Count(
                "headless.cache.writes",
                ("headless.cache.name", cacheName),
                ("headless.cache.operation", "remove_by_tag"),
                ("headless.cache.tier", "l1")
            )
            .Should()
            .Be(1);
    }

    [Fact]
    public async Task should_complete_normally_without_a_listener_attached()
    {
        // given — no MeterListener subscribed for this test; the RecordRequest/RecordWrite early-outs must not
        // affect correctness when unobserved.
        var cacheName = _UniqueName();
        using var cache = _CreateCache(cacheName);
        var key = Faker.Random.AlphaNumeric(8);

        // when
        var missResult = await cache.GetAsync<string>(key, AbortToken);
        await cache.UpsertAsync(key, "value", TimeSpan.FromMinutes(5), AbortToken);
        var hitResult = await cache.GetAsync<string>(key, AbortToken);
        var exists = await cache.ExistsAsync(key, AbortToken);
        var removed = await cache.RemoveAsync(key, AbortToken);

        // then — behavior is correct with zero listeners attached.
        missResult.HasValue.Should().BeFalse();
        hitResult.HasValue.Should().BeTrue();
        exists.Should().BeTrue();
        removed.Should().BeTrue();
    }

    [Fact]
    public async Task should_record_get_or_add_exactly_once_with_no_direct_read_double_count()
    {
        // given — GetOrAddAsync routes through IFactoryCacheStore.TryGetEntryAsync (the coordinator path), a
        // separate explicit-interface implementation from the instrumented public GetAsync/GetAllAsync surface.
        var cacheName = _UniqueName();
        using var cache = _CreateCache(cacheName);
        var key = Faker.Random.AlphaNumeric(8);

        using var metrics = new MetricCollector();

        // when
        await cache.GetOrAddAsync(
            key,
            _ => ValueTask.FromResult<string?>("value"),
            TimeSpan.FromMinutes(5),
            AbortToken
        );

        // then — exactly one get_or_add request, and no "get"/"get_all" leakage from the direct-read instrumentation.
        metrics
            .Count(
                "headless.cache.requests",
                ("headless.cache.name", cacheName),
                ("headless.cache.operation", "get_or_add"),
                ("headless.cache.tier", "l1")
            )
            .Should()
            .Be(1);

        metrics
            .Count(
                "headless.cache.requests",
                ("headless.cache.name", cacheName),
                ("headless.cache.operation", "get"),
                ("headless.cache.tier", "l1")
            )
            .Should()
            .Be(0);

        metrics
            .Count(
                "headless.cache.requests",
                ("headless.cache.name", cacheName),
                ("headless.cache.operation", "get_all"),
                ("headless.cache.tier", "l1")
            )
            .Should()
            .Be(0);
    }

    // Collects long-valued caching measurements (counters) with their tags for the Headless.Caching meter.
    // Mirrors Headless.Caching.Core.Tests.Unit's CachingDiagnosticsTests.MetricCollector (internal to that
    // assembly, so this project — which has no InternalsVisibleTo from Core — keeps its own copy).
    private sealed class MetricCollector : IDisposable
    {
        private readonly ConcurrentBag<(string Name, long Value, KeyValuePair<string, object?>[] Tags)> _measurements =
        [];
        private readonly MeterListener _listener;

        public MetricCollector()
        {
            _listener = new MeterListener
            {
                InstrumentPublished = (instrument, listener) =>
                {
                    if (string.Equals(instrument.Meter.Name, CachingDiagnostics.SourceName, StringComparison.Ordinal))
                    {
                        listener.EnableMeasurementEvents(instrument);
                    }
                },
            };

            _listener.SetMeasurementEventCallback<long>(
                (instrument, measurement, tags, _) => _measurements.Add((instrument.Name, measurement, tags.ToArray()))
            );

            _listener.Start();
        }

        public long Count(string instrumentName, params (string Key, string Value)[] requiredTags)
        {
            return Measurements(instrumentName, requiredTags).Sum(m => m.Value);
        }

        public IReadOnlyList<(string Name, long Value, KeyValuePair<string, object?>[] Tags)> Measurements(
            string instrumentName,
            params (string Key, string Value)[] requiredTags
        )
        {
            return
            [
                .. _measurements.Where(m =>
                    string.Equals(m.Name, instrumentName, StringComparison.Ordinal)
                    && requiredTags.All(rt =>
                        m.Tags.Any(t =>
                            string.Equals(t.Key, rt.Key, StringComparison.Ordinal)
                            && string.Equals(t.Value as string, rt.Value, StringComparison.Ordinal)
                        )
                    )
                ),
            ];
        }

        public void Dispose()
        {
            _listener.Dispose();
        }
    }
}
