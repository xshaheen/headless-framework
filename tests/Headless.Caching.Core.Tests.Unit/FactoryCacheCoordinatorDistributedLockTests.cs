// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;
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

        ValueTask<string?> factory(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref factoryCalls);
            return ValueTask.FromResult<string?>("fresh");
        }

        // when
        var act = async () =>
            await coordinator.GetOrAddAsync(_store, "no-provider", factory, _CreateOptions(), AbortToken);

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

        ValueTask<string?> factory(CancellationToken cancellationToken)
        {
            heldDuringFactory = _lockProvider.IsHeld(key);
            return ValueTask.FromResult<string?>("fresh");
        }

        // when
        var result = await coordinator.GetOrAddAsync(_store, key, factory, _CreateOptions(), AbortToken);

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
        // Required now: fail-safe + finite soft timeout needs a finite ceiling (lock-hold guard).
        var options = _CreateOptions(
            isFailSafeEnabled: true,
            factorySoftTimeout: TimeSpan.FromMilliseconds(50),
            backgroundFactoryCeiling: TimeSpan.FromSeconds(5)
        );

        ValueTask<string?> factory(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref factoryCalls);
            return ValueTask.FromResult<string?>("fresh");
        }

        // when
        var result = await coordinator.GetOrAddAsync(_store, key, factory, options, AbortToken);

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

        ValueTask<string?> factory(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref factoryCalls);
            return ValueTask.FromResult<string?>("fresh");
        }

        // when
        var result = await coordinator.GetOrAddAsync(_store, key, factory, options, AbortToken);

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

        async ValueTask<string?> factory(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref factoryCalls);
            winnerStarted.TrySetResult();
            await gate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            return "fresh";
        }

        // when — the winner enters its factory holding the distributed lock, then the loser piles up on it
        var first = coordinatorA.GetOrAddAsync(_store, key, factory, _CreateOptions(), AbortToken).AsTask();
        await winnerStarted.Task.WaitAsync(AbortToken);
        var second = coordinatorB.GetOrAddAsync(_store, key, factory, _CreateOptions(), AbortToken).AsTask();

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
        coordinator.BackgroundOperationFinished = () => backgroundFinished.TrySetResult();
        var timeoutRegistered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        coordinator.FactoryTimeoutTimerRegistered = () => timeoutRegistered.TrySetResult();
        var factoryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var factoryGate = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        // Required now: fail-safe + finite soft timeout needs a finite ceiling (lock-hold guard).
        var options = _CreateOptions(
            isFailSafeEnabled: true,
            factorySoftTimeout: TimeSpan.FromSeconds(1),
            backgroundFactoryCeiling: TimeSpan.FromSeconds(5)
        );

        async ValueTask<string?> factory(CancellationToken cancellationToken)
        {
            factoryStarted.SetResult();
            return await factoryGate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        // when — the caller gets the stale value while the lease rides with the detached factory
        var resultTask = coordinator.GetOrAddAsync(_store, key, factory, options, AbortToken).AsTask();
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

        static ValueTask<string?> factory(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("boom");

        // when
        var result = await coordinator.GetOrAddAsync(_store, key, factory, options, AbortToken);

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

        static ValueTask<string?> factory(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("boom");

        // when
        var act = async () =>
            await coordinator.GetOrAddAsync(_store, key, factory, _CreateOptions(isFailSafeEnabled: true), AbortToken);

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
        coordinator.BackgroundOperationFinished = () => backgroundFinished.TrySetResult();
        var factoryCalls = 0;
        var options = _CreateOptions(duration: TimeSpan.FromMinutes(10), eagerRefreshThreshold: 0.5f);

        ValueTask<string?> factory(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref factoryCalls);
            return ValueTask.FromResult<string?>("fresh");
        }

        // when
        var result = await coordinator.GetOrAddAsync(_store, key, factory, options, AbortToken);
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
        coordinator.BackgroundOperationFinished = () => backgroundFinished.TrySetResult();
        var factoryCalls = 0;
        var options = _CreateOptions(duration: TimeSpan.FromMinutes(10), eagerRefreshThreshold: 0.5f);

        ValueTask<string?> factory(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref factoryCalls);
            return ValueTask.FromResult<string?>("fresh");
        }

        // when
        var result = await coordinator.GetOrAddAsync(_store, key, factory, options, AbortToken);
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

    // #11 — SkipCacheRead bypasses all store reads including the post-distributed-lock re-check; the factory
    // must run even though a fresh entry exists, and the distributed lock must be acquired and released.
    [Fact]
    public async Task should_run_factory_under_distributed_lock_when_skip_cache_read_is_set_despite_fresh_entry()
    {
        // given — a fresh cached value; SkipCacheRead forces the factory to run regardless
        var key = Faker.Random.AlphaNumeric(8);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        _store.SetEntry(key, "cached", now.AddMinutes(5), now.AddMinutes(5));
        var coordinator = _CreateCoordinator();
        var factoryCalls = 0;

        ValueTask<string?> factory(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref factoryCalls);
            return ValueTask.FromResult<string?>("fresh");
        }

        // when
        var result = await coordinator.GetOrAddAsync(
            _store,
            key,
            factory,
            _CreateOptions(skipCacheRead: true),
            AbortToken
        );

        // then — factory ran exactly once; distributed lock acquired and released; new value persisted
        result.Value.Should().Be("fresh");
        result.IsStale.Should().BeFalse();
        factoryCalls.Should().Be(1);
        _lockProvider.AcquireSuccesses.Should().Be(1);
        _lockProvider.Releases.Should().Be(1);
        _lockProvider.IsHeld(key).Should().BeFalse();
        _store.GetEntry(key)!.Value.Should().Be("fresh");
        // no store reads: SkipCacheRead bypasses all three read checkpoints
        _store.TryGetEntryCalls.Should().Be(0);
    }

    [Fact]
    public async Task should_serve_stale_without_running_factory_and_log_warning_when_lock_acquire_throws_with_fail_safe()
    {
        // given — a stale fail-safe reserve and a lock backend that is DOWN (acquire throws, not "held elsewhere")
        var key = Faker.Random.AlphaNumeric(8);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        _store.SetEntry(key, "stale", now.AddSeconds(-1), now.AddMinutes(5));
        _lockProvider.AcquireFault = () => new InvalidOperationException("lock backend unavailable");
        var logger = Substitute.For<ILogger<FactoryCacheCoordinator>>();
        logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);
        var coordinator = new FactoryCacheCoordinator(_timeProvider, logger, _lockProvider);
        var factoryCalls = 0;
        var options = _CreateOptions(isFailSafeEnabled: true);

        ValueTask<string?> factory(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref factoryCalls);
            return ValueTask.FromResult<string?>("fresh");
        }

        // when
        var result = await coordinator.GetOrAddAsync(_store, key, factory, options, AbortToken);

        // then — stale beats failure: the reserve is served, the factory never runs, and the throttle restamp
        // shields the down backend from per-call retries
        result.Value.Should().Be("stale");
        result.IsStale.Should().BeTrue();
        factoryCalls.Should().Be(0);
        _lockProvider.AcquireSuccesses.Should().Be(0);
        _store.GetEntry(key)!.Value.Should().Be("stale");
        _store.SetEntryCalls.Should().Be(1, "the throttle restamp must be written");

        var logged = logger
            .ReceivedCalls()
            .Any(call =>
            {
                var arguments = call.GetArguments();

                return string.Equals(call.GetMethodInfo().Name, nameof(ILogger.Log), StringComparison.Ordinal)
                    && arguments[0] is LogLevel.Warning
                    && arguments[1] is EventId { Id: 13, Name: "CacheFactoryLockAcquireFailed" };
            });

        logged.Should().BeTrue();
    }

    [Fact]
    public async Task should_propagate_lock_acquire_exception_when_cache_is_cold()
    {
        // given — no stale reserve exists, so there is nothing to degrade to
        var key = Faker.Random.AlphaNumeric(8);
        _lockProvider.AcquireFault = () => new InvalidOperationException("lock backend unavailable");
        var coordinator = _CreateCoordinator();
        var factoryCalls = 0;

        ValueTask<string?> factory(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref factoryCalls);
            return ValueTask.FromResult<string?>("fresh");
        }

        // when
        var act = async () =>
            await coordinator.GetOrAddAsync(_store, key, factory, _CreateOptions(isFailSafeEnabled: true), AbortToken);

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("lock backend unavailable");
        factoryCalls.Should().Be(0);
    }

    [Fact]
    public async Task should_propagate_lock_acquire_exception_when_fail_safe_is_disabled()
    {
        // given — a stale entry exists, but with fail-safe off it is not a usable reserve
        var key = Faker.Random.AlphaNumeric(8);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        _store.SetEntry(key, "stale", now.AddSeconds(-1), now.AddMinutes(5));
        _lockProvider.AcquireFault = () => new InvalidOperationException("lock backend unavailable");
        var coordinator = _CreateCoordinator();
        var factoryCalls = 0;

        ValueTask<string?> factory(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref factoryCalls);
            return ValueTask.FromResult<string?>("fresh");
        }

        // when
        var act = async () =>
            await coordinator.GetOrAddAsync(_store, key, factory, _CreateOptions(isFailSafeEnabled: false), AbortToken);

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("lock backend unavailable");
        factoryCalls.Should().Be(0);
    }

    [Fact]
    public async Task should_propagate_caller_token_oce_from_lock_acquire_instead_of_serving_stale()
    {
        // given — a stale fail-safe reserve and an acquire that throws an OCE bound to the CALLER's token;
        // caller cancellation must propagate, never be converted to a stale serve (mirrors the factory path)
        var key = Faker.Random.AlphaNumeric(8);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        _store.SetEntry(key, "stale", now.AddSeconds(-1), now.AddMinutes(5));
        using var callerCts = new CancellationTokenSource(); // not cancelled — identity match must suffice
        _lockProvider.AcquireFault = () => new OperationCanceledException(callerCts.Token);
        var coordinator = _CreateCoordinator();

        // when
        var act = async () =>
            await coordinator.GetOrAddAsync<string>(
                _store,
                key,
                _ => ValueTask.FromResult<string?>("fresh"),
                _CreateOptions(isFailSafeEnabled: true),
                callerCts.Token
            );

        // then
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task should_serve_stale_when_lock_acquire_throws_oce_from_unrelated_token()
    {
        // given — the acquire OCE carries an internal/downstream token, NOT the caller's: that is a backend
        // failure, so fail-safe activates exactly like any other acquire exception
        var key = Faker.Random.AlphaNumeric(8);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        _store.SetEntry(key, "stale", now.AddSeconds(-1), now.AddMinutes(5));
        using var callerCts = new CancellationTokenSource(); // not cancelled
        using var internalCts = new CancellationTokenSource();
        _lockProvider.AcquireFault = () => new OperationCanceledException(internalCts.Token);
        var coordinator = _CreateCoordinator();

        // when
        var result = await coordinator.GetOrAddAsync<string>(
            _store,
            key,
            _ => ValueTask.FromResult<string?>("fresh"),
            _CreateOptions(isFailSafeEnabled: true),
            callerCts.Token
        );

        // then
        result.Value.Should().Be("stale");
        result.IsStale.Should().BeTrue();
    }

    [Fact]
    public async Task should_swallow_release_failure_and_still_return_factory_result_with_warning_logged()
    {
        // given — a healthy acquire but a lease release that throws (backend hiccup on the way out)
        var key = Faker.Random.AlphaNumeric(8);
        _lockProvider.ReleaseFault = () => new InvalidOperationException("release failed");
        var logger = Substitute.For<ILogger<FactoryCacheCoordinator>>();
        logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);
        var coordinator = new FactoryCacheCoordinator(_timeProvider, logger, _lockProvider);

        // when — the release failure must never mask the operation's outcome
        var result = await coordinator.GetOrAddAsync<string>(
            _store,
            key,
            _ => ValueTask.FromResult<string?>("fresh"),
            _CreateOptions(),
            AbortToken
        );

        // then — the fresh value is returned and persisted; the failure is only logged (EventId 12)
        result.Value.Should().Be("fresh");
        result.IsStale.Should().BeFalse();
        _store.GetEntry(key)!.Value.Should().Be("fresh");
        _lockProvider.AcquireSuccesses.Should().Be(1);
        _lockProvider.Releases.Should().Be(0, "the release faulted before completing");

        var logged = logger
            .ReceivedCalls()
            .Any(call =>
            {
                var arguments = call.GetArguments();

                return string.Equals(call.GetMethodInfo().Name, nameof(ILogger.Log), StringComparison.Ordinal)
                    && arguments[0] is LogLevel.Warning
                    && arguments[1] is EventId { Id: 12, Name: "CacheFactoryLockReleaseFailed" };
            });

        logged.Should().BeTrue();
    }

    private FactoryCacheCoordinator _CreateCoordinator() =>
        new(_timeProvider, NullLogger<FactoryCacheCoordinator>.Instance, _lockProvider);

    private static CacheEntryOptions _CreateOptions(
        TimeSpan? duration = null,
        bool isFailSafeEnabled = false,
        TimeSpan? factorySoftTimeout = null,
        TimeSpan? backgroundFactoryCeiling = null,
        TimeSpan? lockTimeout = null,
        float? eagerRefreshThreshold = null,
        bool useDistributedFactoryLock = true,
        bool skipCacheRead = false
    ) =>
        new()
        {
            Duration = duration ?? TimeSpan.FromSeconds(5),
            EagerRefreshThreshold = eagerRefreshThreshold,
            IsFailSafeEnabled = isFailSafeEnabled,
            FailSafeMaxDuration = TimeSpan.FromMinutes(1),
            FailSafeThrottleDuration = TimeSpan.FromSeconds(10),
            FactorySoftTimeout = factorySoftTimeout ?? Timeout.InfiniteTimeSpan,
            BackgroundFactoryCeiling = backgroundFactoryCeiling ?? Timeout.InfiniteTimeSpan,
            LockTimeout = lockTimeout ?? Timeout.InfiniteTimeSpan,
            UseDistributedFactoryLock = useDistributedFactoryLock,
            SkipCacheRead = skipCacheRead,
        };
}
