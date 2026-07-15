// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Headless.Caching;
using Headless.Messaging;
using Headless.Testing.Tests;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

public sealed class HybridCacheTierMetricsTests : TestBase
{
    private readonly FakeTimeProvider _timeProvider = new();
    private readonly List<object> _disposables = [];

    protected override async ValueTask DisposeAsyncCore()
    {
        foreach (var disposable in _disposables)
        {
            switch (disposable)
            {
                case IAsyncDisposable asyncDisposable:
                    await asyncDisposable.DisposeAsync();
                    break;
                case IDisposable syncDisposable:
                    syncDisposable.Dispose();
                    break;
            }
        }

        _disposables.Clear();
        await base.DisposeAsyncCore();
    }

    // AE2: a two-tier hybrid cache with a key present only in L2. GetOrAddAsync records requests with
    // {outcome=miss,tier=l1} and {outcome=hit,tier=l2}, and the factory does not run.
    [Fact]
    public async Task should_attribute_l1_miss_and_l2_hit_per_tier_when_key_is_only_in_l2()
    {
        // given — a hybrid with an empty L1 and a value seeded only into the L2 backing store
        var l1 = new InMemoryCache(_timeProvider, new InMemoryCacheOptions { CloneValues = true });
        var l2Backing = new InMemoryCache(_timeProvider, new InMemoryCacheOptions { CloneValues = true });
        var l2 = new InMemoryRemoteCacheAdapter(l2Backing);
        var publisher = Substitute.For<IBus>();
        publisher
            .PublishAsync(Arg.Any<CacheInvalidationMessage>(), Arg.Any<PublishOptions?>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        // A unique cache name isolates this test's measurements from other parallel tests sharing the meter.
        var cacheName = "test-" + Guid.NewGuid().ToString("N");
        var hybridOptions = new HybridCacheOptions { CacheName = cacheName };
        var cache = new HybridCache(l1, l2, publisher, hybridOptions, timeProvider: _timeProvider);
        _disposables.Add(cache);
        _disposables.Add(l1);
        _disposables.Add(l2Backing);

        var key = Faker.Random.AlphaNumeric(10);
        var value = Faker.Random.AlphaNumeric(6);
        var options = new CacheEntryOptions { Duration = TimeSpan.FromMinutes(5) };
        await l2Backing.UpsertEntryAsync(key, value, options, AbortToken);

        using var metrics = new MetricCollector();
        var factoryRan = false;

        // when
        var result = await cache.GetOrAddAsync<string>(
            key,
            _ =>
            {
                factoryRan = true;
                return new ValueTask<string?>("factory");
            },
            options,
            AbortToken
        );

        // then
        result.Value.Should().Be(value);
        factoryRan.Should().BeFalse();

        metrics
            .Count(
                "headless.cache.requests",
                ("headless.cache.name", cacheName),
                ("headless.cache.outcome", "miss"),
                ("headless.cache.tier", "l1")
            )
            .Should()
            .Be(1);

        metrics
            .Count(
                "headless.cache.requests",
                ("headless.cache.name", cacheName),
                ("headless.cache.outcome", "hit"),
                ("headless.cache.tier", "l2")
            )
            .Should()
            .Be(1);
    }

    // Collects long-valued caching measurements (counters) with their tags for the Headless.Caching meter.
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
            return _measurements
                .Where(m => string.Equals(m.Name, instrumentName, StringComparison.Ordinal))
                .Where(m =>
                    requiredTags.All(rt =>
                        m.Tags.Any(t =>
                            string.Equals(t.Key, rt.Key, StringComparison.Ordinal)
                            && string.Equals(t.Value as string, rt.Value, StringComparison.Ordinal)
                        )
                    )
                )
                .Sum(m => m.Value);
        }

        public void Dispose()
        {
            _listener.Dispose();
        }
    }
}
