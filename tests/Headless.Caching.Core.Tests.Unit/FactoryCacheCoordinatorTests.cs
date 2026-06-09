// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

public sealed class FactoryCacheCoordinatorTests : TestBase
{
    private readonly FakeTimeProvider _timeProvider = new();
    private readonly FakeFactoryCacheStore _store = new();

    [Fact]
    public void should_throw_when_time_provider_is_null()
    {
        // when
        var act = () => new FactoryCacheCoordinator(null!, NullLogger<FactoryCacheCoordinator>.Instance);

        // then
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task should_return_fresh_hit_without_invoking_factory()
    {
        // given
        var key = Faker.Random.AlphaNumeric(8);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        _store.SetEntry(key, "cached", now.AddMinutes(5), now.AddMinutes(5));
        var factoryCalls = 0;

        // when
        var result = await _CreateCoordinator()
            .GetOrAddAsync<string>(
                _store,
                key,
                _ =>
                {
                    factoryCalls++;
                    return ValueTask.FromResult<string?>("new");
                },
                _CreateOptions(),
                AbortToken
            );

        // then
        result.Value.Should().Be("cached");
        result.IsStale.Should().BeFalse();
        factoryCalls.Should().Be(0);
    }

    [Fact]
    public async Task should_rearm_fresh_sliding_hit_without_invoking_factory()
    {
        // given
        var key = Faker.Random.AlphaNumeric(8);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var slidingExpiration = TimeSpan.FromSeconds(1);
        var physicalExpiresAt = now.AddSeconds(5);
        _store.SetEntry(key, "cached", now.AddMilliseconds(100), physicalExpiresAt, slidingExpiration);
        var factoryCalls = 0;

        // when
        var result = await _CreateCoordinator()
            .GetOrAddAsync<string>(
                _store,
                key,
                _ =>
                {
                    factoryCalls++;
                    return ValueTask.FromResult<string?>("new");
                },
                _CreateOptions(slidingExpiration: slidingExpiration),
                AbortToken
            );

        // then
        var entry = _store.GetEntry(key);
        result.Value.Should().Be("cached");
        result.IsStale.Should().BeFalse();
        factoryCalls.Should().Be(0);
        // Re-arm is now a metadata-only TTL bump via TryRearmSlidingAsync, not a full SetEntry rewrite.
        _store.RearmCalls.Should().Be(1);
        _store.SetEntryCalls.Should().Be(0);
        entry.Should().NotBeNull();
        entry!.LogicalExpiresAt.Should().Be(now.Add(slidingExpiration));
        entry.PhysicalExpiresAt.Should().Be(physicalExpiresAt);
        entry.SlidingExpiration.Should().Be(slidingExpiration);
    }

    [Fact]
    public async Task should_serve_stale_when_factory_throws_within_physical_window()
    {
        // given
        var key = Faker.Random.AlphaNumeric(8);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        _store.SetEntry(key, "stale", now.AddSeconds(-1), now.AddMinutes(5));

        // when
        var result = await _CreateCoordinator()
            .GetOrAddAsync<string>(
                _store,
                key,
                _ => throw new InvalidOperationException("downstream unavailable"),
                _CreateOptions(isFailSafeEnabled: true),
                AbortToken
            );

        // then
        result.Value.Should().Be("stale");
        result.IsStale.Should().BeTrue();
    }

    [Fact]
    public async Task should_log_warning_when_failsafe_activates()
    {
        // given
        var key = Faker.Random.AlphaNumeric(8);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        _store.SetEntry(key, "stale", now.AddSeconds(-1), now.AddMinutes(5));
        var logger = Substitute.For<ILogger<FactoryCacheCoordinator>>();
        logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);

        // when
        await new FactoryCacheCoordinator(_timeProvider, logger)
            .GetOrAddAsync<string>(
                _store,
                key,
                _ => throw new InvalidOperationException("downstream unavailable"),
                _CreateOptions(isFailSafeEnabled: true),
                AbortToken
            );

        // then
        var logged = logger
            .ReceivedCalls()
            .Any(call =>
            {
                var arguments = call.GetArguments();

                return call.GetMethodInfo().Name == nameof(ILogger.Log)
                    && arguments[0] is LogLevel.Warning
                    && arguments[1] is EventId { Id: 1, Name: "CacheFailSafeActivated" };
            });

        logged.Should().BeTrue();
    }

    [Fact]
    public async Task should_propagate_factory_exception_when_cache_is_cold()
    {
        // given
        var key = Faker.Random.AlphaNumeric(8);
        var exception = new InvalidOperationException("downstream unavailable");

        // when
        var act = async () =>
            await _CreateCoordinator()
                .GetOrAddAsync<string>(
                    _store,
                    key,
                    _ => throw exception,
                    _CreateOptions(isFailSafeEnabled: true),
                    AbortToken
                );

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage(exception.Message);
    }

    [Fact]
    public async Task should_propagate_factory_exception_when_stale_entry_is_past_physical_window()
    {
        // given
        var key = Faker.Random.AlphaNumeric(8);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var exception = new InvalidOperationException("downstream unavailable");
        _store.SetEntry(key, "stale", now.AddMinutes(-2), now.AddMinutes(-1));

        // when
        var act = async () =>
            await _CreateCoordinator()
                .GetOrAddAsync<string>(
                    _store,
                    key,
                    _ => throw exception,
                    _CreateOptions(isFailSafeEnabled: true),
                    AbortToken
                );

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage(exception.Message);
    }

    [Fact]
    public async Task should_throttle_factory_retries_after_failsafe_activation()
    {
        // given
        var key = Faker.Random.AlphaNumeric(8);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var factoryCalls = 0;
        var options = _CreateOptions(
            duration: TimeSpan.FromSeconds(5),
            isFailSafeEnabled: true,
            throttleDuration: TimeSpan.FromSeconds(10)
        );
        _store.SetEntry(key, "stale", now.AddSeconds(-1), now.AddMinutes(1));
        var coordinator = _CreateCoordinator();

        // when
        var stale = await coordinator.GetOrAddAsync<string>(
            _store,
            key,
            _ =>
            {
                factoryCalls++;
                throw new InvalidOperationException("downstream unavailable");
            },
            options,
            AbortToken
        );

        var throttled = await coordinator.GetOrAddAsync<string>(
            _store,
            key,
            _ =>
            {
                factoryCalls++;
                return ValueTask.FromResult<string?>("new");
            },
            options,
            AbortToken
        );

        _timeProvider.Advance(TimeSpan.FromSeconds(11));
        var refreshed = await coordinator.GetOrAddAsync<string>(
            _store,
            key,
            _ =>
            {
                factoryCalls++;
                return ValueTask.FromResult<string?>("new");
            },
            options,
            AbortToken
        );

        // then
        stale.IsStale.Should().BeTrue();
        throttled.Value.Should().Be("stale");
        throttled.IsStale.Should().BeFalse();
        refreshed.Value.Should().Be("new");
        factoryCalls.Should().Be(2);
    }

    [Fact]
    public async Task should_not_serve_stale_when_caller_token_is_cancelled()
    {
        // given
        var key = Faker.Random.AlphaNumeric(8);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        _store.SetEntry(key, "stale", now.AddSeconds(-1), now.AddMinutes(5));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // when
        var act = async () =>
            await _CreateCoordinator()
                .GetOrAddAsync<string>(
                    _store,
                    key,
                    _ => throw new OperationCanceledException(cts.Token),
                    _CreateOptions(isFailSafeEnabled: true),
                    cts.Token
                );

        // then
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task should_not_serve_stale_when_failsafe_is_disabled()
    {
        // given
        var key = Faker.Random.AlphaNumeric(8);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        _store.SetEntry(key, "stale", now.AddSeconds(-1), now.AddMinutes(5));

        // when
        var act = async () =>
            await _CreateCoordinator()
                .GetOrAddAsync<string>(
                    _store,
                    key,
                    _ => throw new InvalidOperationException("downstream unavailable"),
                    _CreateOptions(isFailSafeEnabled: false),
                    AbortToken
                );

        // then
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task should_set_logical_and_physical_expiration_on_factory_success()
    {
        // given
        var key = Faker.Random.AlphaNumeric(8);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var duration = TimeSpan.FromSeconds(5);
        var failSafeMaxDuration = TimeSpan.FromMinutes(1);

        // when
        var result = await _CreateCoordinator()
            .GetOrAddAsync<string>(
                _store,
                key,
                _ => ValueTask.FromResult<string?>("fresh"),
                _CreateOptions(duration: duration, isFailSafeEnabled: true, maxDuration: failSafeMaxDuration),
                AbortToken
            );

        // then
        var entry = _store.GetEntry(key);
        result.Value.Should().Be("fresh");
        result.IsStale.Should().BeFalse();
        entry.Should().NotBeNull();
        entry!.LogicalExpiresAt.Should().Be(now.Add(duration));
        entry.PhysicalExpiresAt.Should().Be(now.Add(failSafeMaxDuration));
        entry.SlidingExpiration.Should().BeNull();
    }

    [Fact]
    public async Task should_set_sliding_logical_expiration_on_factory_success()
    {
        // given
        var key = Faker.Random.AlphaNumeric(8);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var duration = TimeSpan.FromSeconds(5);
        var slidingExpiration = TimeSpan.FromSeconds(1);

        // when
        var result = await _CreateCoordinator()
            .GetOrAddAsync<string>(
                _store,
                key,
                _ => ValueTask.FromResult<string?>("fresh"),
                _CreateOptions(duration: duration, slidingExpiration: slidingExpiration),
                AbortToken
            );

        // then
        var entry = _store.GetEntry(key);
        result.Value.Should().Be("fresh");
        result.IsStale.Should().BeFalse();
        entry.Should().NotBeNull();
        entry!.LogicalExpiresAt.Should().Be(now.Add(slidingExpiration));
        entry.PhysicalExpiresAt.Should().Be(now.Add(duration));
        entry.SlidingExpiration.Should().Be(slidingExpiration);
    }

    [Fact]
    public async Task should_clamp_sliding_logical_expiration_to_physical_cap_on_factory_success()
    {
        // given
        var key = Faker.Random.AlphaNumeric(8);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var duration = TimeSpan.FromSeconds(2);
        var slidingExpiration = TimeSpan.FromSeconds(5);

        // when
        await _CreateCoordinator()
            .GetOrAddAsync<string>(
                _store,
                key,
                _ => ValueTask.FromResult<string?>("fresh"),
                _CreateOptions(duration: duration, slidingExpiration: slidingExpiration),
                AbortToken
            );

        // then
        var entry = _store.GetEntry(key);
        entry.Should().NotBeNull();
        entry!.LogicalExpiresAt.Should().Be(now.Add(duration));
        entry.PhysicalExpiresAt.Should().Be(now.Add(duration));
        entry.SlidingExpiration.Should().Be(slidingExpiration);
    }

    [Fact]
    public async Task should_rearm_under_lock_sliding_fresh_hit()
    {
        // given
        var key = Faker.Random.AlphaNumeric(8);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var slidingExpiration = TimeSpan.FromSeconds(1);
        var physicalExpiresAt = now.AddSeconds(5);
        var factoryCalls = 0;
        _store.TryGetEntryOverride = (_, calls) =>
            calls == 2
                ? new FakeFactoryCacheStore.Entry(
                    Value: "concurrent",
                    IsNull: false,
                    LogicalExpiresAt: now.AddMilliseconds(100),
                    PhysicalExpiresAt: physicalExpiresAt,
                    SlidingExpiration: slidingExpiration
                )
                : null;

        // when
        var result = await _CreateCoordinator()
            .GetOrAddAsync<string>(
                _store,
                key,
                _ =>
                {
                    factoryCalls++;
                    return ValueTask.FromResult<string?>("new");
                },
                _CreateOptions(slidingExpiration: slidingExpiration),
                AbortToken
            );

        // then
        var entry = _store.GetEntry(key);
        result.Value.Should().Be("concurrent");
        factoryCalls.Should().Be(0);
        _store.RearmCalls.Should().Be(1);
        _store.SetEntryCalls.Should().Be(0);
        entry.Should().NotBeNull();
        entry!.LogicalExpiresAt.Should().Be(now.Add(slidingExpiration));
        entry.PhysicalExpiresAt.Should().Be(physicalExpiresAt);
    }

    [Fact]
    public async Task should_return_fresh_sliding_hit_when_rearm_write_fails()
    {
        // given
        var key = Faker.Random.AlphaNumeric(8);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var slidingExpiration = TimeSpan.FromSeconds(1);
        _store.SetEntry(key, "cached", now.AddMilliseconds(100), now.AddSeconds(5), slidingExpiration);
        _store.RearmFault = () => new InvalidOperationException("store re-arm failed");

        // when
        var result = await _CreateCoordinator()
            .GetOrAddAsync<string>(
                _store,
                key,
                _ => ValueTask.FromResult<string?>("new"),
                _CreateOptions(slidingExpiration: slidingExpiration),
                AbortToken
            );

        // then
        result.Value.Should().Be("cached");
        result.IsStale.Should().BeFalse();
    }

    [Fact]
    public async Task should_store_null_and_return_null_cache_value_when_factory_returns_null()
    {
        // given
        var key = Faker.Random.AlphaNumeric(8);

        // when
        var result = await _CreateCoordinator()
            .GetOrAddAsync<string>(
                _store,
                key,
                _ => ValueTask.FromResult<string?>(null),
                _CreateOptions(),
                AbortToken
            );

        // then
        result.HasValue.Should().BeTrue();
        result.IsNull.Should().BeTrue();
        result.IsStale.Should().BeFalse();
        var entry = _store.GetEntry(key);
        entry.Should().NotBeNull();
        entry!.IsNull.Should().BeTrue();
        entry.Value.Should().BeNull();
    }

    [Fact]
    public async Task should_treat_store_read_failure_as_miss()
    {
        // given
        var key = Faker.Random.AlphaNumeric(8);
        _store.TryGetEntryFault = () => new InvalidOperationException("store read failed");

        // when
        var result = await _CreateCoordinator()
            .GetOrAddAsync<string>(
                _store,
                key,
                _ => ValueTask.FromResult<string?>("fresh"),
                _CreateOptions(isFailSafeEnabled: true),
                AbortToken
            );

        // then
        result.Value.Should().Be("fresh");
        result.IsStale.Should().BeFalse();
    }

    [Fact]
    public async Task should_propagate_factory_exception_after_store_read_failure_on_cold_cache()
    {
        // given
        var key = Faker.Random.AlphaNumeric(8);
        _store.TryGetEntryFault = () => new InvalidOperationException("store read failed");

        // when
        var act = async () =>
            await _CreateCoordinator()
                .GetOrAddAsync<string>(
                    _store,
                    key,
                    _ => throw new InvalidOperationException("factory failed"),
                    _CreateOptions(isFailSafeEnabled: true),
                    AbortToken
                );

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("factory failed");
    }

    [Fact]
    public async Task should_reject_sliding_expiration_with_failsafe_enabled()
    {
        // given
        var key = Faker.Random.AlphaNumeric(8);
        var factoryCalls = 0;

        // when
        var act = async () =>
            await _CreateCoordinator()
                .GetOrAddAsync<string>(
                    _store,
                    key,
                    _ =>
                    {
                        factoryCalls++;
                        return ValueTask.FromResult<string?>("fresh");
                    },
                    _CreateOptions(isFailSafeEnabled: true, slidingExpiration: TimeSpan.FromSeconds(1)),
                    AbortToken
                );

        // then
        // Sliding + fail-safe is an unsupported combination, guarded via Headless.Checks' Ensure.False
        // (InvalidOperationException) rather than a hand-rolled ArgumentException.
        await act.Should().ThrowAsync<InvalidOperationException>();
        factoryCalls.Should().Be(0);
        _store.SetEntryCalls.Should().Be(0);
    }

    [Fact]
    public async Task should_return_stale_when_restamp_write_fails()
    {
        // given
        var key = Faker.Random.AlphaNumeric(8);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        _store.SetEntry(key, "stale", now.AddSeconds(-1), now.AddMinutes(5));
        _store.SetEntryFault = () => new InvalidOperationException("store write failed");

        // when
        var result = await _CreateCoordinator()
            .GetOrAddAsync<string>(
                _store,
                key,
                _ => throw new InvalidOperationException("factory failed"),
                _CreateOptions(isFailSafeEnabled: true),
                AbortToken
            );

        // then
        result.Value.Should().Be("stale");
        result.IsStale.Should().BeTrue();
    }

    [Fact]
    public async Task should_invoke_factory_once_for_concurrent_cold_callers()
    {
        // given
        var key = Faker.Random.AlphaNumeric(8);
        var factoryCalls = 0;
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var coordinator = _CreateCoordinator();

        async ValueTask<string?> Factory(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref factoryCalls);
            await gate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            return "fresh";
        }

        // when
        var first = coordinator.GetOrAddAsync(_store, key, Factory, _CreateOptions(), AbortToken).AsTask();
        var second = coordinator.GetOrAddAsync(_store, key, Factory, _CreateOptions(), AbortToken).AsTask();
        gate.SetResult();
        var results = await Task.WhenAll(first, second);

        // then
        results.Should().OnlyContain(result => result.Value == "fresh" && !result.IsStale);
        factoryCalls.Should().Be(1);
    }

    // #1 regression guard — token-less OCE from factory with None caller token must activate fail-safe, not propagate.
    // This pins the bug: CancellationToken.None.CanBeCanceled == false, so the identity check is skipped and
    // the token-less OCE is correctly treated as a non-caller exception, enabling fail-safe activation.
    [Fact]
    public async Task should_activate_failsafe_when_factory_throws_tokenless_oce_and_caller_token_is_none()
    {
        // given
        var key = Faker.Random.AlphaNumeric(8);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        _store.SetEntry(key, "stale", now.AddSeconds(-1), now.AddMinutes(5));

        // when — caller passes no token (default = CancellationToken.None)
        var result = await _CreateCoordinator()
            .GetOrAddAsync<string>(
                _store,
                key,
                _ => throw new OperationCanceledException(), // token-less OCE
                _CreateOptions(isFailSafeEnabled: true),
                CancellationToken.None
            );

        // then — fail-safe must activate; stale value is returned, NOT an exception
        result.Value.Should().Be("stale");
        result.IsStale.Should().BeTrue();
    }

    // #1b — OCE carrying an unrelated (non-caller) token must activate fail-safe.
    [Fact]
    public async Task should_activate_failsafe_when_factory_throws_oce_from_unrelated_token()
    {
        // given
        var key = Faker.Random.AlphaNumeric(8);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        _store.SetEntry(key, "stale", now.AddSeconds(-1), now.AddMinutes(5));

        using var callerCts = new CancellationTokenSource();   // not cancelled
        using var internalCts = new CancellationTokenSource(); // simulates a downstream / internal timeout

        // when — factory throws an OCE bound to an *internal* token, not the caller's
        var result = await _CreateCoordinator()
            .GetOrAddAsync<string>(
                _store,
                key,
                _ => throw new OperationCanceledException(internalCts.Token),
                _CreateOptions(isFailSafeEnabled: true),
                callerCts.Token
            );

        // then — fail-safe must activate because the OCE token differs from the caller's token
        result.Value.Should().Be("stale");
        result.IsStale.Should().BeTrue();
    }

    // #5 — store write failure after successful factory MUST propagate; fail-safe must NOT activate.
    [Fact]
    public async Task should_propagate_when_fresh_store_write_fails_after_factory_success()
    {
        // given
        var key = Faker.Random.AlphaNumeric(8);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        _store.SetEntry(key, "stale", now.AddSeconds(-1), now.AddMinutes(5));

        // Fault the FIRST SetEntryAsync call (the fresh write after factory success), then auto-clear so the
        // restamp path (which swallows exceptions) does not interfere.
        var storeWriteException = new InvalidOperationException("store write failed");
        _store.SetEntryFault = () =>
        {
            _store.SetEntryFault = null; // one-shot: only the first call throws
            return storeWriteException;
        };

        // when — factory succeeds, but the fresh write to the store throws
        var act = async () =>
            await _CreateCoordinator()
                .GetOrAddAsync<string>(
                    _store,
                    key,
                    _ => ValueTask.FromResult<string?>("fresh"),
                    _CreateOptions(isFailSafeEnabled: true),
                    TestContext.Current.CancellationToken
                );

        // then — the store-write exception propagates; fail-safe does NOT swallow it
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage(storeWriteException.Message);
    }

    private FactoryCacheCoordinator _CreateCoordinator() =>
        new(_timeProvider, NullLogger<FactoryCacheCoordinator>.Instance);

    private static CacheEntryOptions _CreateOptions(
        TimeSpan? duration = null,
        bool isFailSafeEnabled = false,
        TimeSpan? maxDuration = null,
        TimeSpan? throttleDuration = null,
        TimeSpan? slidingExpiration = null
    ) =>
        new()
        {
            Duration = duration ?? TimeSpan.FromSeconds(5),
            SlidingExpiration = slidingExpiration,
            IsFailSafeEnabled = isFailSafeEnabled,
            FailSafeMaxDuration = maxDuration ?? TimeSpan.FromMinutes(1),
            FailSafeThrottleDuration = throttleDuration ?? TimeSpan.FromSeconds(10),
        };
}
