// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Caching;
using Headless.Testing.Tests;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

public sealed class FactoryCacheCoordinatorEventTests : TestBase
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

    [Fact]
    public async Task should_raise_fresh_hit_event_on_fresh_store_hit()
    {
        // given — a fresh entry in the store, events subscribed synchronously
        var key = Faker.Random.AlphaNumeric(8);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        _store.SetEntry(key, "fresh", now.AddMinutes(5), now.AddMinutes(5));
        var coordinator = _CreateCoordinator();
        CacheHitEventArgs? hit = null;
        using var _ = coordinator.EventsHub.Hit.AddHandler((_, e) => hit = e);

        // when
        var result = await coordinator.GetOrAddAsync<string>(_store, key, _ => throw new(), _Options(), AbortToken);

        // then
        result.Value.Should().Be("fresh");
        hit.Should().NotBeNull();
        hit!.IsStale.Should().BeFalse();
        hit.Key.Should().Be(key);
        hit.Tier.Should().Be(CacheTier.L1);
    }

    [Fact]
    public async Task should_raise_stale_hit_event_when_failsafe_serves_stale_and_never_a_miss()
    {
        // given — a stale reserve and a throwing factory with fail-safe enabled
        var key = Faker.Random.AlphaNumeric(8);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        _store.SetEntry(key, "stale", now.AddSeconds(-1), now.AddMinutes(5));
        var coordinator = _CreateCoordinator();
        var hits = new ConcurrentBag<CacheHitEventArgs>();
        var missed = false;
        using var _1 = coordinator.EventsHub.Hit.AddHandler((_, e) => hits.Add(e));
        using var _2 = coordinator.EventsHub.Miss.AddHandler((_, _) => missed = true);

        // when
        var result = await coordinator.GetOrAddAsync<string>(
            _store,
            key,
            _ => throw new InvalidOperationException("down"),
            _Options(isFailSafeEnabled: true),
            AbortToken
        );

        // then — exactly one aggregate outcome: a stale hit, never a miss (Codex single-outcome fix)
        result.IsStale.Should().BeTrue();
        hits.Should().ContainSingle(h => h.IsStale);
        missed.Should().BeFalse();
    }

    [Fact]
    public async Task should_raise_miss_and_set_on_cold_read()
    {
        // given — an empty store, background events collected
        var key = Faker.Random.AlphaNumeric(8);
        var coordinator = _CreateCoordinator();
        var missed = false;
        var setFired = new TaskCompletionSource();
        using var _1 = coordinator.EventsHub.Miss.AddHandler((_, _) => missed = true);
        using var _2 = coordinator.EventsHub.Set.AddHandler((_, _) => setFired.TrySetResult());

        // when
        var result = await coordinator.GetOrAddAsync<string>(_store, key, _ => new("value"), _Options(), AbortToken);

        // then — Miss (aggregate, synchronous) and Set (background write signal) both fire
        result.Value.Should().Be("value");
        missed.Should().BeTrue();
        await setFired.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
    }

    [Fact]
    public async Task should_raise_factory_success_event_on_cold_read()
    {
        // given
        var key = Faker.Random.AlphaNumeric(8);
        var coordinator = _CreateCoordinator();
        var success = new TaskCompletionSource<CacheFactoryEventArgs>();
        using var _ = coordinator.EventsHub.FactorySuccess.AddHandler((_, e) => success.TrySetResult(e));

        // when
        await coordinator.GetOrAddAsync<string>(_store, key, _ => new("v"), _Options(), AbortToken);

        // then — background factory-success event
        var args = await success.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
        args.Outcome.Should().Be(CacheFactoryOutcome.Success);
        args.Key.Should().Be(key);
    }

    [Fact]
    public async Task should_raise_failsafe_activation_event_with_factory_error_trigger()
    {
        // given — a stale reserve + throwing factory + fail-safe
        var key = Faker.Random.AlphaNumeric(8);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        _store.SetEntry(key, "stale", now.AddSeconds(-1), now.AddMinutes(5));
        var coordinator = _CreateCoordinator();
        var failSafe = new TaskCompletionSource<CacheFailSafeEventArgs>();
        using var _ = coordinator.EventsHub.FailSafeActivation.AddHandler((_, e) => failSafe.TrySetResult(e));

        // when
        await coordinator.GetOrAddAsync<string>(
            _store,
            key,
            _ => throw new InvalidOperationException("down"),
            _Options(isFailSafeEnabled: true),
            AbortToken
        );

        // then
        var args = await failSafe.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
        args.Trigger.Should().Be(CacheFailSafeTrigger.FactoryError);
    }

    [Fact]
    public async Task should_not_deadlock_when_sync_handler_reenters_same_key()
    {
        // given — a synchronous factory-success handler that re-enters GetOrAddAsync for the SAME key. The
        // coordinator dispatches its factory events on a background task (never while the per-key lock is held), so
        // the re-entrant call acquires the lock after release rather than deadlocking.
        var key = Faker.Random.AlphaNumeric(8);
        var coordinator = _CreateCoordinator(syncHandlers: true);
        var reentered = new TaskCompletionSource();
        using var _1 = coordinator.EventsHub.FactorySuccess.AddHandler(
            (_, _) =>
            {
                _ = Task.Run(
                    async () =>
                    {
                        await coordinator.GetOrAddAsync<string>(_store, key, _ => new("again"), _Options(), AbortToken);
                        reentered.TrySetResult();
                    },
                    AbortToken
                );
            }
        );

        // when
        await coordinator.GetOrAddAsync<string>(_store, key, _ => new("v"), _Options(), AbortToken);

        // then — the re-entrant same-key call completes
        await reentered.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
    }

    [Fact]
    public async Task should_take_fast_path_without_events_when_unsubscribed()
    {
        // given — no subscriber at all
        var key = Faker.Random.AlphaNumeric(8);
        var coordinator = _CreateCoordinator();

        // when / then — the operation still works with no listeners
        var result = await coordinator.GetOrAddAsync<string>(_store, key, _ => new("v"), _Options(), AbortToken);
        result.Value.Should().Be("v");
        coordinator.EventsHub.HasSubscribers.Should().BeFalse();
    }

    private FactoryCacheCoordinator _CreateCoordinator(bool syncHandlers = true)
    {
        var coordinator = new FactoryCacheCoordinator(
            _timeProvider,
            logger: null,
            factoryLockProvider: null,
            cacheName: "test",
            cacheTier: CachingMetrics.TierL1,
            includeKeyInTraces: false,
            eventsConfig: new CacheEventsConfig { SyncHandlers = syncHandlers }
        );
        _coordinators.Add(coordinator);

        return coordinator;
    }

    private static CacheEntryOptions _Options(bool isFailSafeEnabled = false) =>
        new()
        {
            Duration = TimeSpan.FromSeconds(5),
            IsFailSafeEnabled = isFailSafeEnabled,
            FailSafeMaxDuration = TimeSpan.FromMinutes(1),
            FailSafeThrottleDuration = TimeSpan.FromSeconds(10),
            FactorySoftTimeout = Timeout.InfiniteTimeSpan,
            FactoryHardTimeout = Timeout.InfiniteTimeSpan,
            BackgroundFactoryCeiling = Timeout.InfiniteTimeSpan,
            LockTimeout = Timeout.InfiniteTimeSpan,
        };
}
