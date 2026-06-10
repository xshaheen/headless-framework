// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

public sealed class FactoryCacheCoordinatorDistributedLockTests : TestBase
{
    private readonly FakeTimeProvider _timeProvider = new();
    private readonly FakeFactoryCacheStore _store = new();
    private readonly FakeCacheFactoryLockProvider _lockProvider = new();

    [Fact]
    public async Task should_throw_before_store_access_when_option_enabled_without_provider()
    {
        // given — a coordinator constructed WITHOUT a factory lock provider
        var coordinator = new FactoryCacheCoordinator(_timeProvider, NullLogger<FactoryCacheCoordinator>.Instance);
        var factoryCalls = 0;

        ValueTask<string?> Factory(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref factoryCalls);
            return ValueTask.FromResult<string?>("fresh");
        }

        // when
        var act = async () =>
            await coordinator.GetOrAddAsync(_store, "no-provider", Factory, _CreateOptions(), AbortToken);

        // then — fails in the validation phase, before any store access or factory run
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Headless.Caching.DistributedLocks*");
        _store.TryGetEntryCalls.Should().Be(0);
        factoryCalls.Should().Be(0);
    }

    [Fact]
    public async Task should_run_factory_under_distributed_lock_and_release_lease_on_success()
    {
        // given
        var key = Faker.Random.AlphaNumeric(8);
        var coordinator = _CreateCoordinator();
        var heldDuringFactory = false;

        ValueTask<string?> Factory(CancellationToken cancellationToken)
        {
            heldDuringFactory = _lockProvider.IsHeld(key);
            return ValueTask.FromResult<string?>("fresh");
        }

        // when
        var result = await coordinator.GetOrAddAsync(_store, key, Factory, _CreateOptions(), AbortToken);

        // then
        result.Value.Should().Be("fresh");
        heldDuringFactory.Should().BeTrue();
        _lockProvider.AcquireSuccesses.Should().Be(1);
        _lockProvider.Releases.Should().Be(1);
        _lockProvider.IsHeld(key).Should().BeFalse();
    }

    [Fact]
    public async Task should_serve_stale_without_running_factory_when_lock_held_elsewhere_with_fail_safe()
    {
        // given — a stale fail-safe reserve and the distributed lock held by "another node"
        var key = Faker.Random.AlphaNumeric(8);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        _store.SetEntry(key, "stale", now.AddSeconds(-1), now.AddMinutes(5));
        using var hold = _lockProvider.Hold(key);
        var coordinator = _CreateCoordinator();
        var factoryCalls = 0;
        var options = _CreateOptions(isFailSafeEnabled: true, factorySoftTimeout: TimeSpan.FromMilliseconds(50));

        ValueTask<string?> Factory(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref factoryCalls);
            return ValueTask.FromResult<string?>("fresh");
        }

        // when
        var result = await coordinator.GetOrAddAsync(_store, key, Factory, options, AbortToken);

        // then — the soft-timeout degradation serves the stale reserve and never runs the factory
        result.Value.Should().Be("stale");
        result.IsStale.Should().BeTrue();
        factoryCalls.Should().Be(0);
        _lockProvider.AcquireSuccesses.Should().Be(0);
        _store.GetEntry(key)!.Value.Should().Be("stale");
    }

    [Fact]
    public async Task should_degrade_to_miss_when_lock_held_elsewhere_on_cold_key_with_finite_lock_timeout()
    {
        // given — no entry at all (cold) and the distributed lock held by "another node"; the LOCAL
        // per-key lock is free, so only the distributed acquisition times out.
        var key = Faker.Random.AlphaNumeric(8);
        using var hold = _lockProvider.Hold(key);
        var coordinator = _CreateCoordinator();
        var factoryCalls = 0;
        var options = _CreateOptions(lockTimeout: TimeSpan.FromMilliseconds(50));

        ValueTask<string?> Factory(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref factoryCalls);
            return ValueTask.FromResult<string?>("fresh");
        }

        // when
        var result = await coordinator.GetOrAddAsync(_store, key, Factory, options, AbortToken);

        // then
        result.HasValue.Should().BeFalse();
        factoryCalls.Should().Be(0);
        _lockProvider.AcquireSuccesses.Should().Be(0);
    }

    [Fact]
    public async Task should_run_factory_once_across_two_coordinators_and_serve_winner_value_to_loser()
    {
        // given — two coordinators (two "nodes") sharing one store and one lock provider
        var key = Faker.Random.AlphaNumeric(8);
        var coordinatorA = _CreateCoordinator();
        var coordinatorB = _CreateCoordinator();
        var factoryCalls = 0;
        var winnerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async ValueTask<string?> Factory(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref factoryCalls);
            winnerStarted.TrySetResult();
            await gate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            return "fresh";
        }

        // when — the winner enters its factory holding the distributed lock, then the loser piles up on it
        var first = coordinatorA.GetOrAddAsync(_store, key, Factory, _CreateOptions(), AbortToken).AsTask();
        await winnerStarted.Task.WaitAsync(AbortToken);
        var second = coordinatorB.GetOrAddAsync(_store, key, Factory, _CreateOptions(), AbortToken).AsTask();

        while (_lockProvider.AcquireAttempts < 2)
        {
            await Task.Delay(10, AbortToken);
        }

        gate.SetResult();
        var results = await Task.WhenAll(first, second);

        // then — exactly ONE factory execution; the loser re-checked the shared store and returned
        // the winner's fresh value
        results.Should().OnlyContain(result => result.Value == "fresh" && !result.IsStale);
        factoryCalls.Should().Be(1);
        _lockProvider.AcquireSuccesses.Should().Be(2);
        _lockProvider.Releases.Should().Be(2);
    }

    [Fact]
    public async Task should_transfer_lease_to_background_completion_on_soft_timeout_and_release_when_it_finishes()
    {
        // given — a stale fail-safe reserve and a factory that outlives the soft timeout
        var key = Faker.Random.AlphaNumeric(8);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        _store.SetEntry(key, "stale", now.AddSeconds(-1), now.AddMinutes(5));
        var coordinator = _CreateCoordinator();
        var backgroundFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        coordinator.BackgroundCompletionFinished = () => backgroundFinished.TrySetResult();
        var timeoutRegistered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        coordinator.FactoryTimeoutTimerRegistered = () => timeoutRegistered.TrySetResult();
        var factoryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var factoryGate = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var options = _CreateOptions(isFailSafeEnabled: true, factorySoftTimeout: TimeSpan.FromSeconds(1));

        async ValueTask<string?> Factory(CancellationToken cancellationToken)
        {
            factoryStarted.SetResult();
            return await factoryGate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        // when — the caller gets the stale value while the lease rides with the detached factory
        var resultTask = coordinator.GetOrAddAsync(_store, key, Factory, options, AbortToken).AsTask();
        await factoryStarted.Task.WaitAsync(AbortToken);
        await timeoutRegistered.Task.WaitAsync(AbortToken);
        _timeProvider.Advance(TimeSpan.FromSeconds(1));
        var result = await resultTask;

        // then — the lease is still held by the background completion
        result.Value.Should().Be("stale");
        result.IsStale.Should().BeTrue();
        _lockProvider.AcquireSuccesses.Should().Be(1);
        _lockProvider.Releases.Should().Be(0);

        // when — the detached factory completes
        factoryGate.SetResult("fresh");
        await backgroundFinished.Task.WaitAsync(AbortToken);

        // then — the fresh value landed and the acquire/release counts balance
        _store.GetEntry(key)!.Value.Should().Be("fresh");
        _lockProvider.Releases.Should().Be(1);
        _lockProvider.IsHeld(key).Should().BeFalse();
    }

    [Fact]
    public async Task should_release_lease_when_factory_throws_and_keep_fail_safe_behavior()
    {
        // given — a stale fail-safe reserve and a factory that faults
        var key = Faker.Random.AlphaNumeric(8);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        _store.SetEntry(key, "stale", now.AddSeconds(-1), now.AddMinutes(5));
        var coordinator = _CreateCoordinator();
        var options = _CreateOptions(isFailSafeEnabled: true);

        static ValueTask<string?> Factory(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("boom");

        // when
        var result = await coordinator.GetOrAddAsync(_store, key, Factory, options, AbortToken);

        // then — fail-safe serves the stale reserve and the lease is released
        result.Value.Should().Be("stale");
        result.IsStale.Should().BeTrue();
        _lockProvider.AcquireSuccesses.Should().Be(1);
        _lockProvider.Releases.Should().Be(1);
        _lockProvider.IsHeld(key).Should().BeFalse();
    }

    [Fact]
    public async Task should_propagate_factory_exception_and_release_lease_when_no_stale_reserve()
    {
        // given — a cold key, so there is no fail-safe fallback
        var key = Faker.Random.AlphaNumeric(8);
        var coordinator = _CreateCoordinator();

        static ValueTask<string?> Factory(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("boom");

        // when
        var act = async () =>
            await coordinator.GetOrAddAsync(_store, key, Factory, _CreateOptions(isFailSafeEnabled: true), AbortToken);

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");
        _lockProvider.AcquireSuccesses.Should().Be(1);
        _lockProvider.Releases.Should().Be(1);
    }

    [Fact]
    public async Task should_skip_eager_refresh_silently_when_distributed_lock_held_elsewhere()
    {
        // given — a fresh entry past its eager point and the distributed lock held by "another node"
        var key = Faker.Random.AlphaNumeric(8);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var eagerRefreshAt = now.AddSeconds(-1);
        _store.SetEntry(key, "old", now.AddMinutes(5), now.AddMinutes(5), eagerRefreshAt: eagerRefreshAt);
        using var hold = _lockProvider.Hold(key);
        var coordinator = _CreateCoordinator();
        var backgroundFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        coordinator.BackgroundCompletionFinished = () => backgroundFinished.TrySetResult();
        var factoryCalls = 0;
        var options = _CreateOptions(duration: TimeSpan.FromMinutes(10), eagerRefreshThreshold: 0.5f);

        ValueTask<string?> Factory(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref factoryCalls);
            return ValueTask.FromResult<string?>("fresh");
        }

        // when
        var result = await coordinator.GetOrAddAsync(_store, key, Factory, options, AbortToken);
        await backgroundFinished.Task.WaitAsync(AbortToken);

        // then — the still-fresh value is served and the entry (including its eager stamp) is untouched
        result.Value.Should().Be("old");
        result.IsStale.Should().BeFalse();
        factoryCalls.Should().Be(0);
        var entry = _store.GetEntry(key)!;
        entry.Value.Should().Be("old");
        entry.EagerRefreshAt.Should().Be(eagerRefreshAt);
        _lockProvider.AcquireAttempts.Should().Be(1);
        _lockProvider.AcquireSuccesses.Should().Be(0);
    }

    [Fact]
    public async Task should_run_eager_refresh_and_release_lease_when_distributed_lock_is_free()
    {
        // given — a fresh entry past its eager point with the distributed lock free
        var key = Faker.Random.AlphaNumeric(8);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        _store.SetEntry(key, "old", now.AddMinutes(5), now.AddMinutes(5), eagerRefreshAt: now.AddSeconds(-1));
        var coordinator = _CreateCoordinator();
        var backgroundFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        coordinator.BackgroundCompletionFinished = () => backgroundFinished.TrySetResult();
        var factoryCalls = 0;
        var options = _CreateOptions(duration: TimeSpan.FromMinutes(10), eagerRefreshThreshold: 0.5f);

        ValueTask<string?> Factory(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref factoryCalls);
            return ValueTask.FromResult<string?>("fresh");
        }

        // when
        var result = await coordinator.GetOrAddAsync(_store, key, Factory, options, AbortToken);
        await backgroundFinished.Task.WaitAsync(AbortToken);

        // then — the caller got the still-fresh value while the refresh ran under the lease and released it
        result.Value.Should().Be("old");
        factoryCalls.Should().Be(1);
        _store.GetEntry(key)!.Value.Should().Be("fresh");
        _lockProvider.AcquireSuccesses.Should().Be(1);
        _lockProvider.Releases.Should().Be(1);
        _lockProvider.IsHeld(key).Should().BeFalse();
    }

    [Fact]
    public async Task should_not_touch_lock_provider_when_option_is_disabled()
    {
        // given
        var key = Faker.Random.AlphaNumeric(8);
        var coordinator = _CreateCoordinator();
        var options = _CreateOptions(useDistributedFactoryLock: false);

        // when
        var result = await coordinator.GetOrAddAsync<string>(
            _store,
            key,
            _ => ValueTask.FromResult<string?>("fresh"),
            options,
            AbortToken
        );

        // then — off by default means zero distributed-lock traffic
        result.Value.Should().Be("fresh");
        _lockProvider.AcquireAttempts.Should().Be(0);
    }

    private FactoryCacheCoordinator _CreateCoordinator() =>
        new(_timeProvider, NullLogger<FactoryCacheCoordinator>.Instance, _lockProvider);

    private static CacheEntryOptions _CreateOptions(
        TimeSpan? duration = null,
        bool isFailSafeEnabled = false,
        TimeSpan? factorySoftTimeout = null,
        TimeSpan? lockTimeout = null,
        float? eagerRefreshThreshold = null,
        bool useDistributedFactoryLock = true
    ) =>
        new()
        {
            Duration = duration ?? TimeSpan.FromSeconds(5),
            EagerRefreshThreshold = eagerRefreshThreshold,
            IsFailSafeEnabled = isFailSafeEnabled,
            FailSafeMaxDuration = TimeSpan.FromMinutes(1),
            FailSafeThrottleDuration = TimeSpan.FromSeconds(10),
            FactorySoftTimeout = factorySoftTimeout ?? Timeout.InfiniteTimeSpan,
            LockTimeout = lockTimeout ?? Timeout.InfiniteTimeSpan,
            UseDistributedFactoryLock = useDistributedFactoryLock,
        };
}
