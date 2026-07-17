// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Diagnostics;
using Headless.Caching;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

/// <summary>
/// Regression coverage for the caller-cancellation span-leak fix in <see cref="FactoryCacheCoordinator"/>
/// a bare <see langword="catch"/> after the KTD-7 caller-cancellation filter must still dispose
/// the in-flight <c>cache.factory</c> span so it is exported and <see cref="Activity.Current"/> is never
/// left pointing at a stopped span past the cancelled call.
/// </summary>
public sealed class CachingDiagnosticsCancellationTests : TestBase
{
    private readonly FakeTimeProvider _timeProvider = new();
    private readonly FakeFactoryCacheStore _store = new();

    // Regression (e4dda16f9): cancelling the caller mid-factory must propagate OperationCanceledException
    // AND stop every span this call started — cache.get_or_add (the parent) and cache.factory (the child)
    // — instead of leaking either past the cancelled frame.
    [Fact]
    public async Task should_stop_get_or_add_and_factory_spans_when_caller_cancels_mid_factory()
    {
        // given — the factory never observes its own cancellation token (a non-cooperative factory);
        // the coordinator's own caller-cancellation race must be what unwinds the call and disposes the
        // child span, not cooperative cancellation inside the factory body.
        var cacheName = _UniqueName();
        var key = Faker.Random.AlphaNumeric(8);
        var factoryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var factoryGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Activity? factoryActivity = null;

        using var coordinator = new FactoryCacheCoordinator(
            _timeProvider,
            NullLogger<FactoryCacheCoordinator>.Instance,
            factoryLockProvider: null,
            cacheName,
            "l1"
        );

        using var stopped = new StoppedActivityCollector();
        using var cts = new CancellationTokenSource();

        async ValueTask<string?> Factory(CancellationToken cancellationToken)
        {
            // cache.factory has just been started by the coordinator, so it is Activity.Current here —
            // capture the exact instance rather than relying on a span tag (cache.factory carries no
            // headless.cache.name tag, unlike the parent cache.get_or_add span).
            factoryActivity = Activity.Current;
            factoryStarted.SetResult();
            await factoryGate.Task.ConfigureAwait(false);
            return "unused";
        }

        // when
        var resultTask = coordinator.GetOrAddAsync(_store, key, Factory, _CreateOptions(), cts.Token).AsTask();
        await factoryStarted.Task;
        await cts.CancelAsync();
        var act = async () => await resultTask;

        // then
        await act.Should().ThrowAsync<OperationCanceledException>();

        // Let the abandoned (non-cooperative) factory finish so its detached fault/discard observers settle.
        factoryGate.SetResult();

        factoryActivity.Should().NotBeNull();
        factoryActivity!.OperationName.Should().Be("cache.factory");
        // Dispose() (the fix) synchronously fires ActivityStopped, so the child span is already in the
        // collector by the time the cancelled call above has finished unwinding.
        stopped.All.Should().Contain(factoryActivity);
        factoryActivity.Status.Should().NotBe(ActivityStatusCode.Error);

        var parentSpan = stopped.Single("cache.get_or_add", cacheName);
        parentSpan.Status.Should().NotBe(ActivityStatusCode.Error);
    }

    private static string _UniqueName()
    {
        return "test-" + Guid.NewGuid().ToString("N");
    }

    private static CacheEntryOptions _CreateOptions()
    {
        return new CacheEntryOptions
        {
            Duration = TimeSpan.FromSeconds(5),
            FactorySoftTimeout = Timeout.InfiniteTimeSpan,
            FactoryHardTimeout = Timeout.InfiniteTimeSpan,
            BackgroundFactoryCeiling = Timeout.InfiniteTimeSpan,
            LockTimeout = Timeout.InfiniteTimeSpan,
        };
    }

    // Collects ActivityStopped events for the Headless.Caching source. A small intentional duplicate of
    // CachingDiagnosticsTests.ActivityCollector (that class is private to its own file).
    private sealed class StoppedActivityCollector : IDisposable
    {
        private readonly ConcurrentBag<Activity> _stopped = [];
        private readonly ActivityListener _listener;

        public StoppedActivityCollector()
        {
            _listener = new ActivityListener
            {
                ShouldListenTo = source =>
                    string.Equals(source.Name, CachingDiagnostics.SourceName, StringComparison.Ordinal),
                Sample = static (ref _) => ActivitySamplingResult.AllData,
                ActivityStopped = _stopped.Add,
            };

            ActivitySource.AddActivityListener(_listener);
        }

        public IReadOnlyCollection<Activity> All => _stopped;

        public Activity Single(string operationName, string cacheName)
        {
            return _stopped.Single(a =>
                string.Equals(a.OperationName, operationName, StringComparison.Ordinal)
                && string.Equals(a.GetTagItem(CachingMetrics.TagName) as string, cacheName, StringComparison.Ordinal)
            );
        }

        public void Dispose()
        {
            _listener.Dispose();
        }
    }
}
