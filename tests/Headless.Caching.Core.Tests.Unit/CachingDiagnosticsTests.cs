// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Headless.Caching;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

public sealed class CachingDiagnosticsTests : TestBase
{
    private readonly FakeTimeProvider _timeProvider = new();
    private readonly FakeFactoryCacheStore _store = new();
    private readonly List<FactoryCacheCoordinator> _coordinators = [];

    protected override ValueTask DisposeAsyncCore()
    {
        foreach (var coordinator in _coordinators)
        {
            coordinator.Dispose();
        }

        _coordinators.Clear();

        return base.DisposeAsyncCore();
    }

    // AE1: fail-safe enabled + stale reserve + throwing factory -> failsafe.activations{trigger=factory_error}
    // increments by 1, the caller receives the stale value, and no cache key appears on any metric dimension.
    [Fact]
    public async Task should_record_failsafe_activation_with_factory_error_trigger_and_never_put_key_on_metrics()
    {
        // given — a unique cache name isolates this test's measurements from other parallel tests sharing the meter.
        var cacheName = _UniqueName();
        var key = Faker.Random.AlphaNumeric(8);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        _store.SetEntry(key, "stale", now.AddSeconds(-1), now.AddMinutes(5));

        using var metrics = new MetricCollector();
        var coordinator = _CreateCoordinator(cacheName);

        // when
        var result = await coordinator.GetOrAddAsync<string>(
            _store,
            key,
            _ => throw new InvalidOperationException("downstream unavailable"),
            _CreateOptions(isFailSafeEnabled: true),
            AbortToken
        );

        // then
        result.Value.Should().Be("stale");
        result.IsStale.Should().BeTrue();

        metrics
            .Count(
                "headless.cache.failsafe.activations",
                ("headless.cache.name", cacheName),
                ("headless.cache.trigger", "factory_error")
            )
            .Should()
            .Be(1);

        // AE1 privacy: the raw key must never appear on any metric dimension (a global invariant).
        metrics.AllTagKeys().Should().NotContain("headless.cache.key");
        metrics.AllTagValues().Should().NotContain(key);
    }

    // AE3: hard timeout with no fail-safe reserve -> factory.executions{outcome=timeout} increments, the
    // cache.get_or_add span status is Error, and CacheFactoryTimeoutException propagates.
#pragma warning disable CA2025 // The started task is fully awaited before the listener collectors are disposed.
    [Fact]
    public async Task should_record_timeout_execution_and_error_span_when_hard_timeout_has_no_fallback()
    {
        // given
        var cacheName = _UniqueName();
        var key = Faker.Random.AlphaNumeric(8);
        var factoryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var options = _CreateOptions(factoryHardTimeout: TimeSpan.FromSeconds(1));

        using var metrics = new MetricCollector();
        using var activities = new ActivityCollector();
        var coordinator = _CreateCoordinator(cacheName);
        var timeoutRegistered = _WaitForFactoryTimeoutRegistered(coordinator);

        async ValueTask<string?> Factory(CancellationToken cancellationToken)
        {
            factoryStarted.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, _timeProvider, cancellationToken).ConfigureAwait(false);
            return "fresh";
        }

        // when
        var resultTask = coordinator.GetOrAddAsync(_store, key, Factory, options, AbortToken).AsTask();
        await factoryStarted.Task;
        await timeoutRegistered;
        _timeProvider.Advance(TimeSpan.FromSeconds(1));
        var act = async () => await resultTask;

        // then
        var thrown = await act.Should().ThrowAsync<CacheFactoryTimeoutException>();
        thrown.Which.Key.Should().Be(key);

        metrics
            .Count(
                "headless.cache.factory.executions",
                ("headless.cache.name", cacheName),
                ("headless.cache.outcome", "timeout")
            )
            .Should()
            .Be(1);

        var span = activities.Single("cache.get_or_add", cacheName);
        span.Status.Should().Be(ActivityStatusCode.Error);
    }
#pragma warning restore CA2025

    // AE4a: with no metric/trace listener subscribed, the instrumentation early-out reports disabled so span
    // creation and TagList building are skipped on the hot path (a cache operation still completes normally).
    [Fact]
    public async Task should_report_instrumentation_disabled_and_run_without_listeners()
    {
        // given — no ActivityListener / MeterListener attached
        CachingDiagnostics.IsEnabled.Should().BeFalse();

        var key = Faker.Random.AlphaNumeric(8);
        var coordinator = _CreateCoordinator();

        // when
        var result = await coordinator.GetOrAddAsync<string>(
            _store,
            key,
            _ => ValueTask.FromResult<string?>("fresh"),
            _CreateOptions(),
            AbortToken
        );

        // then — the operation is unaffected by the absence of listeners
        result.Value.Should().Be("fresh");

        // and subscribing flips the enable toggle (subscribing IS the enable, no separate flag)
        using var activities = new ActivityCollector();
        CachingDiagnostics.IsEnabled.Should().BeTrue();
    }

    // AE4b: with a trace listener attached and IncludeKeyInTraces off (the default), spans emit but carry no
    // headless.cache.key attribute; the low-cardinality headless.cache.name is always present.
    [Fact]
    public async Task should_not_put_key_on_span_when_include_key_in_traces_is_off()
    {
        // given
        var cacheName = _UniqueName();
        var key = Faker.Random.AlphaNumeric(8);
        using var activities = new ActivityCollector();
        var coordinator = _CreateCoordinator(cacheName, includeKeyInTraces: false);

        // when
        _ = await coordinator.GetOrAddAsync<string>(
            _store,
            key,
            _ => ValueTask.FromResult<string?>("fresh"),
            _CreateOptions(),
            AbortToken
        );

        // then
        var span = activities.Single("cache.get_or_add", cacheName);
        span.GetTagItem("headless.cache.name").Should().Be(cacheName);
        span.GetTagItem("headless.cache.key").Should().BeNull();
    }

    [Fact]
    public async Task should_put_key_on_span_only_when_include_key_in_traces_is_on()
    {
        // given
        var cacheName = _UniqueName();
        var key = Faker.Random.AlphaNumeric(8);
        using var activities = new ActivityCollector();
        var coordinator = _CreateCoordinator(cacheName, includeKeyInTraces: true);

        // when
        _ = await coordinator.GetOrAddAsync<string>(
            _store,
            key,
            _ => ValueTask.FromResult<string?>("fresh"),
            _CreateOptions(),
            AbortToken
        );

        // then
        var span = activities.Single("cache.get_or_add", cacheName);
        span.GetTagItem("headless.cache.key").Should().Be(key);
    }

    private FactoryCacheCoordinator _CreateCoordinator(string cacheName = "test-cache", bool includeKeyInTraces = false)
    {
        var coordinator = new FactoryCacheCoordinator(
            _timeProvider,
            NullLogger<FactoryCacheCoordinator>.Instance,
            factoryLockProvider: null,
            cacheName,
            "l1",
            includeKeyInTraces
        );

        _coordinators.Add(coordinator);

        return coordinator;
    }

    private static string _UniqueName()
    {
        return "test-" + Guid.NewGuid().ToString("N");
    }

    private Task _WaitForFactoryTimeoutRegistered(FactoryCacheCoordinator coordinator)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        coordinator.FactoryTimeoutTimerRegistered = () => tcs.TrySetResult();
        return tcs.Task;
    }

    private static CacheEntryOptions _CreateOptions(bool isFailSafeEnabled = false, TimeSpan? factoryHardTimeout = null)
    {
        return new CacheEntryOptions
        {
            Duration = TimeSpan.FromSeconds(5),
            IsFailSafeEnabled = isFailSafeEnabled,
            FailSafeMaxDuration = TimeSpan.FromMinutes(1),
            FailSafeThrottleDuration = TimeSpan.FromSeconds(10),
            FactorySoftTimeout = Timeout.InfiniteTimeSpan,
            FactoryHardTimeout = factoryHardTimeout ?? Timeout.InfiniteTimeSpan,
            BackgroundFactoryCeiling = Timeout.InfiniteTimeSpan,
            LockTimeout = Timeout.InfiniteTimeSpan,
        };
    }

    // Collects caching activities (ActivityStopped) for the Headless.Caching source.
    private sealed class ActivityCollector : IDisposable
    {
        private readonly ConcurrentBag<Activity> _activities = [];
        private readonly ActivityListener _listener;

        public ActivityCollector()
        {
            _listener = new ActivityListener
            {
                ShouldListenTo = source =>
                    string.Equals(source.Name, CachingDiagnostics.SourceName, StringComparison.Ordinal),
                Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
                ActivityStopped = _activities.Add,
            };

            ActivitySource.AddActivityListener(_listener);
        }

        public Activity Single(string operationName, string cacheName)
        {
            return _activities.Single(a =>
                string.Equals(a.OperationName, operationName, StringComparison.Ordinal)
                && string.Equals(a.GetTagItem("headless.cache.name") as string, cacheName, StringComparison.Ordinal)
            );
        }

        public void Dispose()
        {
            _listener.Dispose();
        }
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

        public IEnumerable<string> AllTagKeys()
        {
            return _measurements.SelectMany(m => m.Tags).Select(t => t.Key);
        }

        public IEnumerable<object?> AllTagValues()
        {
            return _measurements.SelectMany(m => m.Tags).Select(t => t.Value);
        }

        public void Dispose()
        {
            _listener.Dispose();
        }
    }
}
