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
    public void should_throw_when_time_provider_is_null()
    {
        // when
        var act = () => new FactoryCacheCoordinator(null!, NullLogger<FactoryCacheCoordinator>.Instance);

        // then
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void should_default_factory_timeouts_and_background_ceiling_to_infinite()
    {
        // when
        var options = new CacheEntryOptions();

        // then
        options.FactorySoftTimeout.Should().Be(Timeout.InfiniteTimeSpan);
        options.FactoryHardTimeout.Should().Be(Timeout.InfiniteTimeSpan);
        options.BackgroundFactoryCeiling.Should().Be(Timeout.InfiniteTimeSpan);
        options.LockTimeout.Should().Be(Timeout.InfiniteTimeSpan);
    }

    [Fact]
    public void should_dispose_keyed_lock_without_throwing()
    {
        // given
        var coordinator = _CreateCoordinator();

        // when & then
        var act = coordinator.Dispose;
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    public async Task should_throw_when_factory_soft_timeout_is_non_positive_finite(int milliseconds)
    {
        // given
        var options = _CreateOptions(factorySoftTimeout: TimeSpan.FromMilliseconds(milliseconds));

        // when
        var act = async () =>
            await _CreateCoordinator()
                .GetOrAddAsync(_store, "soft-timeout-validation", _FactoryReturns("fresh"), options, AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    public async Task should_throw_when_factory_hard_timeout_is_non_positive_finite(int milliseconds)
    {
        // given
        var options = _CreateOptions(factoryHardTimeout: TimeSpan.FromMilliseconds(milliseconds));

        // when
        var act = async () =>
            await _CreateCoordinator()
                .GetOrAddAsync(_store, "hard-timeout-validation", _FactoryReturns("fresh"), options, AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    public async Task should_throw_when_background_factory_ceiling_is_non_positive_finite(int milliseconds)
    {
        // given
        var options = _CreateOptions(backgroundFactoryCeiling: TimeSpan.FromMilliseconds(milliseconds));

        // when
        var act = async () =>
            await _CreateCoordinator()
                .GetOrAddAsync(_store, "background-ceiling-validation", _FactoryReturns("fresh"), options, AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task should_throw_when_factory_hard_timeout_is_not_greater_than_soft_timeout()
    {
        // given
        var options = _CreateOptions(
            factorySoftTimeout: TimeSpan.FromSeconds(2),
            factoryHardTimeout: TimeSpan.FromSeconds(2)
        );

        // when
        var act = async () =>
            await _CreateCoordinator()
                .GetOrAddAsync(_store, "timeout-order-validation", _FactoryReturns("fresh"), options, AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task should_allow_one_factory_timeout_to_be_infinite(bool hardIsInfinite)
    {
        // given
        var options = hardIsInfinite
            ? _CreateOptions(factorySoftTimeout: TimeSpan.FromSeconds(1), factoryHardTimeout: Timeout.InfiniteTimeSpan)
            : _CreateOptions(factorySoftTimeout: Timeout.InfiniteTimeSpan, factoryHardTimeout: TimeSpan.FromSeconds(1));

        // when
        var result = await _CreateCoordinator()
            .GetOrAddAsync(_store, Faker.Random.AlphaNumeric(8), _FactoryReturns("fresh"), options, AbortToken);

        // then
        result.Value.Should().Be("fresh");
    }

    [Fact]
    public async Task should_allow_soft_timeout_without_failsafe()
    {
        // given
        var options = _CreateOptions(isFailSafeEnabled: false, factorySoftTimeout: TimeSpan.FromSeconds(1));

        // when
        var result = await _CreateCoordinator()
            .GetOrAddAsync(_store, Faker.Random.AlphaNumeric(8), _FactoryReturns("fresh"), options, AbortToken);

        // then
        result.Value.Should().Be("fresh");
    }

    [Fact]
    public void should_create_cache_factory_timeout_exception_as_timeout_exception()
    {
        // given
        var key = Faker.Random.AlphaNumeric(8);

        // when
        var exception = new CacheFactoryTimeoutException(key, TimeSpan.FromSeconds(2));

        // then
        exception.Should().BeAssignableTo<TimeoutException>();
        exception.Key.Should().Be(key);
        exception.Limit.Should().Be(TimeSpan.FromSeconds(2));
        exception.Message.Should().Contain(key);
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

    // #5a — PRE-LOCK: store signals ServeStaleImmediately on the first read; the coordinator must return the
    // stale value without acquiring the local lock and without invoking the factory.
    [Fact]
    public async Task should_serve_stale_immediately_without_factory_when_flagged_on_first_read()
    {
        // given — a logically-expired but physically-present entry with ServeStaleImmediately=true
        var key = Faker.Random.AlphaNumeric(8);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        _store.SetEntry(key, "stale", now.AddSeconds(-1), now.AddMinutes(5), serveStaleImmediately: true);
        var factoryCalls = 0;

        // when
        var result = await _CreateCoordinator()
            .GetOrAddAsync<string>(
                _store,
                key,
                _ =>
                {
                    factoryCalls++;
                    return ValueTask.FromResult<string?>("fresh");
                },
                _CreateOptions(isFailSafeEnabled: true),
                AbortToken
            );

        // then — stale value returned, factory never invoked, only 1 store read (the pre-lock path)
        result.Value.Should().Be("stale");
        result.IsStale.Should().BeTrue();
        factoryCalls.Should().Be(0);
        _store.TryGetEntryCalls.Should().Be(1);
    }

    // #5b — UNDER-LOCK: the first store read returns a plain stale entry (no flag); the coordinator acquires the
    // local lock and re-reads. The second read returns ServeStaleImmediately=true, so the factory must not run.
    [Fact]
    public async Task should_serve_stale_immediately_without_factory_when_flagged_on_under_lock_read()
    {
        // given — first read: stale without the flag; second read (under-lock): stale with the flag
        var key = Faker.Random.AlphaNumeric(8);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        _store.SetEntry(key, "stale", now.AddSeconds(-1), now.AddMinutes(5));
        _store.TryGetEntryOverride = (_, calls) =>
            calls == 2
                ? new FakeFactoryCacheStore.Entry(
                    Value: "stale",
                    IsNull: false,
                    LogicalExpiresAt: now.AddSeconds(-1),
                    PhysicalExpiresAt: now.AddMinutes(5),
                    SlidingExpiration: null,
                    ServeStaleImmediately: true
                )
                : null;
        var factoryCalls = 0;

        // when
        var result = await _CreateCoordinator()
            .GetOrAddAsync<string>(
                _store,
                key,
                _ =>
                {
                    factoryCalls++;
                    return ValueTask.FromResult<string?>("fresh");
                },
                _CreateOptions(isFailSafeEnabled: true),
                AbortToken
            );

        // then — factory skipped; exactly 2 store reads (pre-lock + under-lock)
        result.Value.Should().Be("stale");
        result.IsStale.Should().BeTrue();
        factoryCalls.Should().Be(0);
        _store.TryGetEntryCalls.Should().Be(2);
    }

    // #5c — POST-DISTRIBUTED-LOCK: same scenario but UseDistributedFactoryLock=true. After acquiring the
    // distributed lock the coordinator does a third store read; if that read sets ServeStaleImmediately the
    // factory must still not run.
    [Fact]
    public async Task should_serve_stale_immediately_without_factory_when_flagged_on_post_distributed_lock_read()
    {
        // given — reads 1 and 2 return plain stale; read 3 (post-distributed-lock) returns the flag
        var key = Faker.Random.AlphaNumeric(8);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        _store.SetEntry(key, "stale", now.AddSeconds(-1), now.AddMinutes(5));
        _store.TryGetEntryOverride = (_, calls) =>
            calls >= 2
                ? new FakeFactoryCacheStore.Entry(
                    Value: "stale",
                    IsNull: false,
                    LogicalExpiresAt: now.AddSeconds(-1),
                    PhysicalExpiresAt: now.AddMinutes(5),
                    SlidingExpiration: null,
                    ServeStaleImmediately: calls == 3
                )
                : null;
        var lockProvider = new FakeCacheFactoryLockProvider();

        using var coordinator = new FactoryCacheCoordinator(
            _timeProvider,
            NullLogger<FactoryCacheCoordinator>.Instance,
            lockProvider
        );

        var factoryCalls = 0;

        // when
        var result = await coordinator.GetOrAddAsync<string>(
            _store,
            key,
            _ =>
            {
                factoryCalls++;
                return ValueTask.FromResult<string?>("fresh");
            },
            _CreateOptions(isFailSafeEnabled: true, useDistributedFactoryLock: true),
            AbortToken
        );

        // then — factory skipped; 3 store reads (pre-lock, under-lock, post-distributed-lock)
        result.Value.Should().Be("stale");
        result.IsStale.Should().BeTrue();
        factoryCalls.Should().Be(0);
        _store.TryGetEntryCalls.Should().Be(3);
        lockProvider.AcquireSuccesses.Should().Be(1);
        lockProvider.Releases.Should().Be(1);
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
        using var coordinator = new FactoryCacheCoordinator(_timeProvider, logger);

        // when
        await coordinator.GetOrAddAsync<string>(
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

                return string.Equals(call.GetMethodInfo().Name, nameof(ILogger.Log), StringComparison.Ordinal)
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
    public async Task should_preserve_entry_metadata_on_failsafe_restamp_but_clear_eager_refresh()
    {
        // given — a stale reserve carrying the full metadata envelope
        var key = Faker.Random.AlphaNumeric(8);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var options = _CreateOptions(
            duration: TimeSpan.FromSeconds(5),
            isFailSafeEnabled: true,
            throttleDuration: TimeSpan.FromSeconds(10)
        );
        var tags = new[] { "tenant:1", "products" };
        _store.SetEntry(
            key,
            "stale",
            now.AddSeconds(-1),
            now.AddMinutes(1),
            eagerRefreshAt: now.AddSeconds(-2),
            etag: "W/\"v42\"",
            lastModifiedAt: now.AddMinutes(-30),
            tags: tags
        );

        using var coordinator = _CreateCoordinator();

        // when — the factory throws, activating fail-safe and the throttle restamp
        var stale = await coordinator.GetOrAddAsync<string>(
            _store,
            key,
            _ => throw new InvalidOperationException("downstream unavailable"),
            options,
            AbortToken
        );

        // then — the restamped entry preserves ETag/LastModifiedAt/Tags but must not eager-refresh
        stale.IsStale.Should().BeTrue();
        var restamped = _store.GetEntry(key)!;
        restamped.ETag.Should().Be("W/\"v42\"");
        restamped.LastModifiedAt.Should().Be(now.AddMinutes(-30));
        restamped.Tags.Should().BeEquivalentTo(tags);
        restamped.EagerRefreshAt.Should().BeNull("a restamped stale reserve must not trigger an eager refresh");
        restamped.SlidingExpiration.Should().BeNull();
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
        using var coordinator = _CreateCoordinator();

        var result = await coordinator.GetOrAddAsync<string>(
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

        async ValueTask<string?> factory(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref factoryCalls);
            await gate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            return "fresh";
        }

        // when
        var first = coordinator.GetOrAddAsync(_store, key, factory, _CreateOptions(), AbortToken).AsTask();
        var second = coordinator.GetOrAddAsync(_store, key, factory, _CreateOptions(), AbortToken).AsTask();
        gate.SetResult();
        var results = await Task.WhenAll(first, second);

        // then
        results.Should().OnlyContain(result => result.Value == "fresh" && !result.IsStale);
        factoryCalls.Should().Be(1);
    }

    [Fact]
    public async Task should_return_stale_on_soft_timeout_and_complete_factory_in_background()
    {
        // given
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
        var options = _CreateOptions(
            isFailSafeEnabled: true,
            factorySoftTimeout: TimeSpan.FromSeconds(1),
            factoryHardTimeout: TimeSpan.FromSeconds(10),
            // Required now: fail-safe + finite soft timeout needs a finite ceiling (lock-hold guard).
            backgroundFactoryCeiling: TimeSpan.FromSeconds(5)
        );

        async ValueTask<string?> factory(CancellationToken cancellationToken)
        {
            factoryStarted.SetResult();
            return await factoryGate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        // when
        var resultTask = coordinator.GetOrAddAsync(_store, key, factory, options, AbortToken).AsTask();
        await factoryStarted.Task;
        await timeoutRegistered.Task;
        _timeProvider.Advance(TimeSpan.FromSeconds(1));
        var result = await resultTask;
        factoryGate.SetResult("fresh");
        await backgroundFinished.Task;

        // then
        result.Value.Should().Be("stale");
        result.IsStale.Should().BeTrue();
        _store.GetEntry(key)!.Value.Should().Be("fresh");
    }

    [Fact]
    public async Task should_not_start_duplicate_factory_during_soft_timeout_background_completion()
    {
        // given
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
        var factoryCalls = 0;
        var options = _CreateOptions(
            isFailSafeEnabled: true,
            factorySoftTimeout: TimeSpan.FromSeconds(1),
            factoryHardTimeout: TimeSpan.FromSeconds(10),
            // Required now: fail-safe + finite soft timeout needs a finite ceiling (lock-hold guard).
            backgroundFactoryCeiling: TimeSpan.FromSeconds(5)
        );

        async ValueTask<string?> factory(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref factoryCalls);
            factoryStarted.SetResult();
            return await factoryGate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        // when
        var first = coordinator.GetOrAddAsync(_store, key, factory, options, AbortToken).AsTask();
        await factoryStarted.Task;
        await timeoutRegistered.Task;
        _timeProvider.Advance(TimeSpan.FromSeconds(1));
        (await first).IsStale.Should().BeTrue();

        var second = coordinator.GetOrAddAsync(_store, key, factory, options, AbortToken).AsTask();
        factoryGate.SetResult("fresh");
        await backgroundFinished.Task;
        var secondResult = await second;

        // then
        secondResult.Value.Should().Be("fresh");
        secondResult.IsStale.Should().BeFalse();
        factoryCalls.Should().Be(1);
    }

    [Fact]
    public async Task should_restamp_throttle_when_background_factory_fails_after_soft_timeout()
    {
        // given
        var key = Faker.Random.AlphaNumeric(8);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        _store.SetEntry(key, "stale", now.AddSeconds(-1), now.AddMinutes(5));
        var coordinator = _CreateCoordinator();
        var backgroundFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        coordinator.BackgroundOperationFinished = () => backgroundFinished.TrySetResult();
        var timeoutRegistered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        coordinator.FactoryTimeoutTimerRegistered = () => timeoutRegistered.TrySetResult();
        var factoryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var factoryGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var factoryCalls = 0;
        var options = _CreateOptions(
            isFailSafeEnabled: true,
            throttleDuration: TimeSpan.FromSeconds(20),
            factorySoftTimeout: TimeSpan.FromSeconds(1),
            factoryHardTimeout: TimeSpan.FromSeconds(10),
            // Required now: fail-safe + finite soft timeout needs a finite ceiling (lock-hold guard).
            backgroundFactoryCeiling: TimeSpan.FromSeconds(5)
        );

        async ValueTask<string?> factory(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref factoryCalls);
            factoryStarted.SetResult();
            await factoryGate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException("background failed");
        }

        // when
        var first = coordinator.GetOrAddAsync(_store, key, factory, options, AbortToken).AsTask();
        await factoryStarted.Task;
        await timeoutRegistered.Task;
        _timeProvider.Advance(TimeSpan.FromSeconds(1));
        (await first).IsStale.Should().BeTrue();
        factoryGate.SetResult();
        await backgroundFinished.Task;

        var throttled = await coordinator.GetOrAddAsync(_store, key, factory, options, AbortToken);

        // then
        throttled.Value.Should().Be("stale");
        throttled.IsStale.Should().BeFalse();
        factoryCalls.Should().Be(1);
    }

    [Fact]
    public async Task should_throw_cache_factory_timeout_exception_when_hard_timeout_fires_without_fallback()
    {
        // given
        var key = Faker.Random.AlphaNumeric(8);
        var factoryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var options = _CreateOptions(factoryHardTimeout: TimeSpan.FromSeconds(1));
        var coordinator = _CreateCoordinator();
        var timeoutRegistered = _WaitForFactoryTimeoutRegistered(coordinator);

        async ValueTask<string?> factory(CancellationToken cancellationToken)
        {
            factoryStarted.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, _timeProvider, cancellationToken).ConfigureAwait(false);
            return "fresh";
        }

        // when
        var resultTask = coordinator.GetOrAddAsync(_store, key, factory, options, AbortToken).AsTask();
        await factoryStarted.Task;
        await timeoutRegistered;
        _timeProvider.Advance(TimeSpan.FromSeconds(1));
        var act = async () => await resultTask;

        // then: the thrown exception carries the key and the configured hard-timeout limit
        var thrown = await act.Should().ThrowAsync<CacheFactoryTimeoutException>();
        thrown.Which.Key.Should().Be(key);
        thrown.Which.Limit.Should().Be(options.FactoryHardTimeout);
    }

    [Fact]
    public async Task should_serve_stale_when_hard_timeout_fires_with_fallback()
    {
        // given
        var key = Faker.Random.AlphaNumeric(8);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        _store.SetEntry(key, "stale", now.AddSeconds(-1), now.AddMinutes(5));
        var factoryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var options = _CreateOptions(isFailSafeEnabled: true, factoryHardTimeout: TimeSpan.FromSeconds(1));
        var coordinator = _CreateCoordinator();
        var timeoutRegistered = _WaitForFactoryTimeoutRegistered(coordinator);

        async ValueTask<string?> factory(CancellationToken cancellationToken)
        {
            factoryStarted.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, _timeProvider, cancellationToken).ConfigureAwait(false);
            return "fresh";
        }

        // when
        var resultTask = coordinator.GetOrAddAsync(_store, key, factory, options, AbortToken).AsTask();
        await factoryStarted.Task;
        await timeoutRegistered;
        _timeProvider.Advance(TimeSpan.FromSeconds(1));
        var result = await resultTask;

        // then
        result.Value.Should().Be("stale");
        result.IsStale.Should().BeTrue();
    }

    [Fact]
    public async Task should_ignore_soft_timeout_when_failsafe_is_disabled()
    {
        // given
        var key = Faker.Random.AlphaNumeric(8);
        var factoryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var factoryGate = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var options = _CreateOptions(factorySoftTimeout: TimeSpan.FromSeconds(1));

        async ValueTask<string?> factory(CancellationToken cancellationToken)
        {
            factoryStarted.SetResult();
            return await factoryGate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        // when
        var resultTask = _CreateCoordinator().GetOrAddAsync(_store, key, factory, options, AbortToken).AsTask();
        await factoryStarted.Task;
        _timeProvider.Advance(TimeSpan.FromSeconds(1));

        // then
        resultTask.IsCompleted.Should().BeFalse();

        factoryGate.SetResult("fresh");
        (await resultTask).Value.Should().Be("fresh");
    }

    [Fact]
    public async Task should_propagate_caller_cancellation_without_activating_background_completion()
    {
        // given
        var key = Faker.Random.AlphaNumeric(8);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        _store.SetEntry(key, "stale", now.AddSeconds(-1), now.AddMinutes(5));
        var coordinator = _CreateCoordinator();
        var backgroundStarted = false;
        coordinator.BackgroundCompletionCeilingTimerRegistered = () => backgroundStarted = true;
        var factoryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var options = _CreateOptions(
            isFailSafeEnabled: true,
            factorySoftTimeout: TimeSpan.FromSeconds(1),
            factoryHardTimeout: TimeSpan.FromSeconds(10),
            // Required now: fail-safe + finite soft timeout needs a finite ceiling (lock-hold guard).
            backgroundFactoryCeiling: TimeSpan.FromSeconds(5)
        );
        using var cts = new CancellationTokenSource();

        async ValueTask<string?> factory(CancellationToken cancellationToken)
        {
            factoryStarted.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, _timeProvider, cancellationToken).ConfigureAwait(false);
            return "fresh";
        }

        // when
        var resultTask = coordinator.GetOrAddAsync(_store, key, factory, options, cts.Token).AsTask();
        await factoryStarted.Task;
        await cts.CancelAsync();
        var act = async () => await resultTask;

        // then
        await act.Should().ThrowAsync<OperationCanceledException>();
        backgroundStarted.Should().BeFalse();
    }

    [Fact]
    public async Task should_keep_background_factory_detached_after_soft_timeout_return()
    {
        // given
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
        var options = _CreateOptions(
            isFailSafeEnabled: true,
            factorySoftTimeout: TimeSpan.FromSeconds(1),
            factoryHardTimeout: TimeSpan.FromSeconds(10),
            // Required now: fail-safe + finite soft timeout needs a finite ceiling (lock-hold guard).
            backgroundFactoryCeiling: TimeSpan.FromSeconds(5)
        );
        using var cts = new CancellationTokenSource();

        async ValueTask<string?> factory(CancellationToken cancellationToken)
        {
            factoryStarted.SetResult();
            return await factoryGate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        // when
        var resultTask = coordinator.GetOrAddAsync(_store, key, factory, options, cts.Token).AsTask();
        await factoryStarted.Task;
        await timeoutRegistered.Task;
        _timeProvider.Advance(TimeSpan.FromSeconds(1));
        (await resultTask).IsStale.Should().BeTrue();
        await cts.CancelAsync();
        factoryGate.SetResult("fresh");
        await backgroundFinished.Task;

        // then
        _store.GetEntry(key)!.Value.Should().Be("fresh");
    }

    [Fact]
    public async Task should_release_lock_at_background_ceiling_and_suppress_late_abandoned_write()
    {
        // given
        var key = Faker.Random.AlphaNumeric(8);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        _store.SetEntry(key, "stale", now.AddSeconds(-1), now.AddMinutes(5));
        var coordinator = _CreateCoordinator();
        var ceilingRegistered = _WaitForBackgroundCeilingRegistered(coordinator);
        var backgroundFinished = _WaitForBackgroundFinished(coordinator);
        var timeoutRegistered = _WaitForFactoryTimeoutRegistered(coordinator);
        var factoryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var abandonedFactoryGate = new TaskCompletionSource<string?>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var options = _CreateOptions(
            duration: TimeSpan.FromSeconds(5),
            isFailSafeEnabled: true,
            throttleDuration: TimeSpan.FromSeconds(2),
            factorySoftTimeout: TimeSpan.FromSeconds(1),
            factoryHardTimeout: TimeSpan.FromSeconds(20),
            backgroundFactoryCeiling: TimeSpan.FromSeconds(3)
        );

        async ValueTask<string?> factoryA(CancellationToken cancellationToken)
        {
            factoryStarted.SetResult();
            return await abandonedFactoryGate.Task.ConfigureAwait(false);
        }

        // when
        var first = coordinator.GetOrAddAsync(_store, key, factoryA, options, AbortToken).AsTask();
        await factoryStarted.Task;
        await timeoutRegistered;
        _timeProvider.Advance(TimeSpan.FromSeconds(1));
        (await first).IsStale.Should().BeTrue();
        await ceilingRegistered;
        _timeProvider.Advance(TimeSpan.FromSeconds(3));
        await backgroundFinished;

        _timeProvider.Advance(TimeSpan.FromSeconds(3));
        var second = await coordinator.GetOrAddAsync(_store, key, _FactoryReturns("B"), options, AbortToken);
        abandonedFactoryGate.SetResult("A");
        await Task.Yield();

        // then
        second.Value.Should().Be("B");
        _store.GetEntry(key)!.Value.Should().Be("B");
    }

    [Fact]
    public async Task should_throw_when_fail_safe_and_finite_soft_timeout_leave_background_ceiling_infinite()
    {
        // given — the dangerous combination: fail-safe + a finite soft timeout select the lock-holding
        // background-detach path, but an infinite BackgroundFactoryCeiling lets a hung factory hold the per-key
        // lock forever. Validation must reject it up front rather than admit the unbounded lock hold.
        var key = Faker.Random.AlphaNumeric(8);
        var options = _CreateOptions(
            isFailSafeEnabled: true,
            factorySoftTimeout: TimeSpan.FromSeconds(1),
            factoryHardTimeout: TimeSpan.FromSeconds(20),
            backgroundFactoryCeiling: Timeout.InfiniteTimeSpan
        );

        // when
        var act = async () =>
            await _CreateCoordinator().GetOrAddAsync(_store, key, _FactoryReturns("fresh"), options, AbortToken);

        // then
        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*BackgroundFactoryCeiling must be finite*");
    }

    [Fact]
    public async Task should_write_fresh_when_background_factory_completes_before_ceiling()
    {
        // given: a finite ceiling is armed, but the background factory finishes before it fires
        var key = Faker.Random.AlphaNumeric(8);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        _store.SetEntry(key, "stale", now.AddSeconds(-1), now.AddMinutes(5));
        var coordinator = _CreateCoordinator();
        var ceilingRegistered = _WaitForBackgroundCeilingRegistered(coordinator);
        var backgroundFinished = _WaitForBackgroundFinished(coordinator);
        var timeoutRegistered = _WaitForFactoryTimeoutRegistered(coordinator);
        var factoryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var factoryGate = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var options = _CreateOptions(
            isFailSafeEnabled: true,
            factorySoftTimeout: TimeSpan.FromSeconds(1),
            factoryHardTimeout: TimeSpan.FromSeconds(20),
            backgroundFactoryCeiling: TimeSpan.FromSeconds(3)
        );

        async ValueTask<string?> factory(CancellationToken cancellationToken)
        {
            factoryStarted.SetResult();
            return await factoryGate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        // when
        var first = coordinator.GetOrAddAsync(_store, key, factory, options, AbortToken).AsTask();
        await factoryStarted.Task;
        await timeoutRegistered;
        _timeProvider.Advance(TimeSpan.FromSeconds(1)); // soft timeout -> stale + detached background factory
        (await first).IsStale.Should().BeTrue();
        await ceilingRegistered;
        factoryGate.SetResult("fresh"); // factory wins the race against the 3s ceiling (only 1s elapsed)
        await backgroundFinished;

        // then: the fresh value is written and the ceiling never had to cancel the factory
        _store.GetEntry(key)!.Value.Should().Be("fresh");
    }

    [Fact]
    public async Task should_log_failure_when_abandoned_factory_faults_after_background_ceiling()
    {
        // given: the ceiling abandons a token-ignoring factory that later faults
        var key = Faker.Random.AlphaNumeric(8);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        _store.SetEntry(key, "stale", now.AddSeconds(-1), now.AddMinutes(5));
        var logger = Substitute.For<ILogger<FactoryCacheCoordinator>>();
        logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);
        var coordinator = _CreateCoordinator(logger);
        var ceilingRegistered = _WaitForBackgroundCeilingRegistered(coordinator);
        var backgroundFinished = _WaitForBackgroundFinished(coordinator);
        var timeoutRegistered = _WaitForFactoryTimeoutRegistered(coordinator);
        var factoryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var abandonedFactoryGate = new TaskCompletionSource<string?>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var options = _CreateOptions(
            isFailSafeEnabled: true,
            factorySoftTimeout: TimeSpan.FromSeconds(1),
            factoryHardTimeout: TimeSpan.FromSeconds(20),
            backgroundFactoryCeiling: TimeSpan.FromSeconds(3)
        );

        async ValueTask<string?> factory(CancellationToken cancellationToken)
        {
            factoryStarted.SetResult();
            // Ignore cancellation: the factory keeps running past the ceiling, then faults.
            return await abandonedFactoryGate.Task.ConfigureAwait(false);
        }

        // when
        var first = coordinator.GetOrAddAsync(_store, key, factory, options, AbortToken).AsTask();
        await factoryStarted.Task;
        await timeoutRegistered;
        _timeProvider.Advance(TimeSpan.FromSeconds(1)); // soft timeout -> stale + detached background factory
        (await first).IsStale.Should().BeTrue();
        await ceilingRegistered;
        _timeProvider.Advance(TimeSpan.FromSeconds(3)); // ceiling fires -> factory abandoned
        await backgroundFinished;
        abandonedFactoryGate.SetException(new InvalidOperationException("late factory failure"));

        // then: the abandoned task's fault is observed and logged, not lost (the #4 fix).
        var observed = false;
        for (var attempt = 0; attempt < 200 && !observed; attempt++)
        {
            observed = logger
                .ReceivedCalls()
                .Any(call =>
                    string.Equals(call.GetMethodInfo().Name, nameof(ILogger.Log), StringComparison.Ordinal)
                    && call.GetArguments()[1] is EventId { Id: 6, Name: "CacheBackgroundCompletionFailed" }
                );

            if (!observed)
            {
                await Task.Delay(25, AbortToken);
            }
        }

        observed.Should().BeTrue("the fault observer attached to the abandoned factory must log its failure");
    }

    [Fact]
    public async Task should_log_failure_when_abandoned_factory_faults_after_hard_timeout()
    {
        // given: cold cache, hard timeout, a token-ignoring factory that keeps running and later faults
        var key = Faker.Random.AlphaNumeric(8);
        var logger = Substitute.For<ILogger<FactoryCacheCoordinator>>();
        logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);
        var coordinator = _CreateCoordinator(logger);
        var timeoutRegistered = _WaitForFactoryTimeoutRegistered(coordinator);
        var factoryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var abandonedFactoryGate = new TaskCompletionSource<string?>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var options = _CreateOptions(isFailSafeEnabled: false, factoryHardTimeout: TimeSpan.FromSeconds(1));

        async ValueTask<string?> factory(CancellationToken cancellationToken)
        {
            factoryStarted.SetResult();
            // Ignore cancellation: the factory keeps running past the hard timeout, then faults.
            return await abandonedFactoryGate.Task.ConfigureAwait(false);
        }

        // when
        var resultTask = coordinator.GetOrAddAsync(_store, key, factory, options, AbortToken).AsTask();
        await factoryStarted.Task;
        await timeoutRegistered;
        _timeProvider.Advance(TimeSpan.FromSeconds(1)); // hard timeout -> factory abandoned, throws (cold cache)
        var act = async () => await resultTask;
        await act.Should().ThrowAsync<CacheFactoryTimeoutException>();
        abandonedFactoryGate.SetException(new InvalidOperationException("late factory failure"));

        // then: the abandoned task's fault is observed and logged, not lost.
        var observed = false;
        for (var attempt = 0; attempt < 200 && !observed; attempt++)
        {
            observed = logger
                .ReceivedCalls()
                .Any(call =>
                    string.Equals(call.GetMethodInfo().Name, nameof(ILogger.Log), StringComparison.Ordinal)
                    && call.GetArguments()[1] is EventId { Id: 6, Name: "CacheBackgroundCompletionFailed" }
                );

            if (!observed)
            {
                await Task.Delay(25, AbortToken);
            }
        }

        observed
            .Should()
            .BeTrue("the fault observer attached to the abandoned hard-timeout factory must log its failure");
    }

    [Fact]
    public async Task should_warn_once_per_key_when_soft_timeout_is_inert()
    {
        // given: a finite soft timeout with fail-safe disabled makes the soft timeout inert (EventId 7)
        var key = Faker.Random.AlphaNumeric(8);
        var logger = Substitute.For<ILogger<FactoryCacheCoordinator>>();
        logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);
        var coordinator = _CreateCoordinator(logger);
        var options = _CreateOptions(
            duration: TimeSpan.FromSeconds(1),
            isFailSafeEnabled: false,
            factorySoftTimeout: TimeSpan.FromSeconds(1)
        );

        // when: two factory misses on the same key (expire the entry between them so both reach the timeout
        // selection, which is where the inert warning is emitted and deduplicated)
        await coordinator.GetOrAddAsync(_store, key, _FactoryReturns("first"), options, AbortToken);
        _timeProvider.Advance(TimeSpan.FromSeconds(2));
        await coordinator.GetOrAddAsync(_store, key, _FactoryReturns("second"), options, AbortToken);

        // then: the inert warning is emitted exactly once for the key (deduplicated)
        var inertWarnings = logger
            .ReceivedCalls()
            .Count(call =>
                string.Equals(call.GetMethodInfo().Name, nameof(ILogger.Log), StringComparison.Ordinal)
                && call.GetArguments()[1] is EventId { Id: 7, Name: "CacheSoftTimeoutInert" }
            );

        inertWarnings.Should().Be(1);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    public async Task should_throw_when_lock_timeout_is_non_positive_finite(int milliseconds)
    {
        // given
        var options = _CreateOptions(lockTimeout: TimeSpan.FromMilliseconds(milliseconds));

        // when
        var act = async () =>
            await _CreateCoordinator()
                .GetOrAddAsync(_store, "lock-timeout-validation", _FactoryReturns("fresh"), options, AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task should_return_stale_when_waiter_times_out_acquiring_lock()
    {
        // given
        var key = Faker.Random.AlphaNumeric(8);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        _store.SetEntry(key, "stale", now.AddSeconds(-1), now.AddMinutes(5));
        var coordinator = _CreateCoordinator();
        var backgroundFinished = _WaitForBackgroundFinished(coordinator);
        var timeoutRegistered = _WaitForFactoryTimeoutRegistered(coordinator);
        var firstFactoryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstFactoryGate = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondFactoryCalls = 0;
        var options = _CreateOptions(
            isFailSafeEnabled: true,
            factorySoftTimeout: TimeSpan.FromSeconds(1),
            factoryHardTimeout: TimeSpan.FromSeconds(10),
            // Required now: fail-safe + finite soft timeout needs a finite ceiling (lock-hold guard).
            backgroundFactoryCeiling: TimeSpan.FromSeconds(5)
        );

        async ValueTask<string?> firstFactory(CancellationToken cancellationToken)
        {
            firstFactoryStarted.SetResult();
            return await firstFactoryGate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        ValueTask<string?> secondFactory(CancellationToken _)
        {
            secondFactoryCalls++;
            return ValueTask.FromResult<string?>("second");
        }

        // when
        var first = coordinator.GetOrAddAsync(_store, key, firstFactory, options, AbortToken).AsTask();
        await firstFactoryStarted.Task;
        await timeoutRegistered;
        var second = coordinator.GetOrAddAsync(_store, key, secondFactory, options, AbortToken).AsTask();
        _timeProvider.Advance(TimeSpan.FromSeconds(1));
        var secondResult = await second;
        firstFactoryGate.SetResult("first");
        await first;
        await backgroundFinished;

        // then
        secondResult.Value.Should().Be("stale");
        secondResult.IsStale.Should().BeTrue();
        secondFactoryCalls.Should().Be(0);
    }

    [Fact]
    public async Task should_return_miss_when_waiter_times_out_acquiring_lock_without_stale()
    {
        // given: no stale reserve exists and a hanging factory holds the per-key lock; a finite LockTimeout bounds
        // the waiter so it degrades to a miss instead of blocking on the in-flight factory.
        var key = Faker.Random.AlphaNumeric(8);
        var coordinator = _CreateCoordinator();
        var firstFactoryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstFactoryGate = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondFactoryCalls = 0;
        var options = _CreateOptions(lockTimeout: TimeSpan.FromSeconds(2));

        async ValueTask<string?> firstFactory(CancellationToken cancellationToken)
        {
            firstFactoryStarted.SetResult();
            return await firstFactoryGate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        ValueTask<string?> secondFactory(CancellationToken _)
        {
            secondFactoryCalls++;
            return ValueTask.FromResult<string?>("second");
        }

        // when
        var first = coordinator.GetOrAddAsync(_store, key, firstFactory, options, AbortToken).AsTask();
        await firstFactoryStarted.Task;
        var second = coordinator.GetOrAddAsync(_store, key, secondFactory, options, AbortToken).AsTask();
        _timeProvider.Advance(TimeSpan.FromSeconds(2));
        var secondResult = await second;
        firstFactoryGate.SetResult("first");
        await first;

        // then
        secondResult.HasValue.Should().BeFalse();
        secondFactoryCalls.Should().Be(0);
    }

    [Fact]
    public async Task should_escape_same_key_reentrancy_with_stale_value_when_lock_timeout_applies()
    {
        // given
        var key = Faker.Random.AlphaNumeric(8);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        _store.SetEntry(key, "stale", now.AddSeconds(-1), now.AddMinutes(5));
        var coordinator = _CreateCoordinator();
        var innerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var innerOptions = _CreateOptions(
            isFailSafeEnabled: true,
            factorySoftTimeout: TimeSpan.FromSeconds(1),
            factoryHardTimeout: TimeSpan.FromSeconds(10),
            // Required now: fail-safe + finite soft timeout needs a finite ceiling (lock-hold guard).
            backgroundFactoryCeiling: TimeSpan.FromSeconds(5)
        );
        var outerOptions = _CreateOptions(
            isFailSafeEnabled: true,
            factorySoftTimeout: TimeSpan.FromSeconds(10),
            factoryHardTimeout: TimeSpan.FromSeconds(20),
            // Required now: fail-safe + finite soft timeout needs a finite ceiling (lock-hold guard).
            backgroundFactoryCeiling: TimeSpan.FromSeconds(30)
        );

        async ValueTask<string?> outerFactory(CancellationToken cancellationToken)
        {
            var inner = coordinator
                .GetOrAddAsync(_store, key, _FactoryReturns("inner"), innerOptions, cancellationToken)
                .AsTask();
            innerStarted.SetResult();
            _timeProvider.Advance(TimeSpan.FromSeconds(1));
            return (await inner).Value;
        }

        // when
        var result = await coordinator.GetOrAddAsync(_store, key, outerFactory, outerOptions, AbortToken);

        // then
        await innerStarted.Task;
        result.Value.Should().Be("stale");
        _store.GetEntry(key)!.Value.Should().Be("stale");
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

        using var callerCts = new CancellationTokenSource(); // not cancelled
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

    [Theory]
    [InlineData(0f)]
    [InlineData(1f)]
    [InlineData(-0.5f)]
    [InlineData(1.5f)]
    public async Task should_reject_out_of_range_eager_refresh_threshold(float threshold)
    {
        var key = Faker.Random.AlphaNumeric(8);
        var options = _CreateOptions(eagerRefreshThreshold: threshold);

        var act = async () =>
            await _CreateCoordinator().GetOrAddAsync(_store, key, _FactoryReturns("value"), options, AbortToken);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task should_reject_eager_refresh_combined_with_sliding_expiration()
    {
        var key = Faker.Random.AlphaNumeric(8);
        var options = _CreateOptions(slidingExpiration: TimeSpan.FromSeconds(1), eagerRefreshThreshold: 0.5f);

        var act = async () =>
            await _CreateCoordinator().GetOrAddAsync(_store, key, _FactoryReturns("value"), options, AbortToken);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*eager refresh*");
    }

    [Fact]
    public async Task should_stamp_eager_refresh_at_on_fresh_write()
    {
        // given
        var key = Faker.Random.AlphaNumeric(8);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var options = _CreateOptions(duration: TimeSpan.FromSeconds(10), eagerRefreshThreshold: 0.5f);

        // when
        await _CreateCoordinator().GetOrAddAsync(_store, key, _FactoryReturns("value"), options, AbortToken);

        // then
        var entry = _store.GetEntry(key)!;
        entry.EagerRefreshAt.Should().Be(now.AddSeconds(5));
        entry.LogicalExpiresAt.Should().Be(now.AddSeconds(10));
    }

    [Fact]
    public async Task should_eager_refresh_in_background_when_fresh_hit_passes_threshold()
    {
        // given — a fresh entry whose eager point has already passed
        var key = Faker.Random.AlphaNumeric(8);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        _store.SetEntry(key, "old", now.AddMinutes(5), now.AddMinutes(5), eagerRefreshAt: now.AddSeconds(-1));
        using var coordinator = _CreateCoordinator();
#pragma warning disable CA2025 // Do not pass 'IDisposable' instances into unawaited tasks
        var backgroundFinished = _WaitForBackgroundFinished(coordinator);
#pragma warning restore CA2025
        var options = _CreateOptions(duration: TimeSpan.FromMinutes(10), eagerRefreshThreshold: 0.5f);

        // when
        var result = await coordinator.GetOrAddAsync(_store, key, _FactoryReturns("fresh"), options, AbortToken);
        await backgroundFinished;

        // then — the caller got the still-fresh value; the background refresh replaced it and re-stamped
        result.Value.Should().Be("old");
        result.IsStale.Should().BeFalse();
        var entry = _store.GetEntry(key)!;
        entry.Value.Should().Be("fresh");
        entry.EagerRefreshAt.Should().NotBeNull();
    }

    // #7 — eager-refresh gate-write CAS-lost branch (_RunEagerRefreshAsync: `if (!gateCommitted) return;`).
    // A concurrent writer wins the gate's compare-and-swap, so the gate write returns false (not an exception).
    // The refresh must abort BEFORE the factory runs, leave the entry fresh and re-triggerable (EagerRefreshAt
    // still stamped), and release the per-key lock so a later eager trigger can acquire it again.
    [Fact]
    public async Task should_abort_eager_refresh_without_factory_when_gate_write_loses_cas()
    {
        // given — a fresh entry past its eager point; the first gate write loses the CAS race
        var key = Faker.Random.AlphaNumeric(8);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var eagerRefreshAt = now.AddSeconds(-1);
        _store.SetEntry(key, "old", now.AddMinutes(5), now.AddMinutes(5), eagerRefreshAt: eagerRefreshAt);
        var coordinator = _CreateCoordinator();
        var backgroundFinished = _WaitForBackgroundFinished(coordinator);
        var factoryCalls = 0;
        var options = _CreateOptions(duration: TimeSpan.FromMinutes(10), eagerRefreshThreshold: 0.5f);

        // Fail only the gate write; never the factory result-write (which must not be reached here).
        _store.SetEntryCommitOverride = (_, calls) => calls != 1;

        ValueTask<string?> factory(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref factoryCalls);
            return ValueTask.FromResult<string?>("fresh");
        }

        // when — the triggering caller returns the still-fresh value; the detached refresh aborts at the gate
        var result = await coordinator.GetOrAddAsync(_store, key, factory, options, AbortToken);
        await backgroundFinished;

        // then — the factory never ran, the entry stays fresh with its eager stamp intact, and the gate write
        // was the only SetEntry attempt (the result-write was never reached).
        result.Value.Should().Be("old");
        result.IsStale.Should().BeFalse();
        factoryCalls.Should().Be(0);
        _store.SetEntryCalls.Should().Be(1);
        var entry = _store.GetEntry(key)!;
        entry.Value.Should().Be("old");
        entry.EagerRefreshAt.Should().Be(eagerRefreshAt, "the CAS-lost gate write must leave the entry re-triggerable");

        // and — the per-key lock was released: a second eager trigger acquires it and runs its gate write.
        var secondBackgroundFinished = _WaitForBackgroundFinished(coordinator);
        _store.SetEntryCommitOverride = null;
        var secondResult = await coordinator.GetOrAddAsync(_store, key, factory, options, AbortToken);
        await secondBackgroundFinished;

        secondResult.Value.Should().Be("old");
        factoryCalls.Should().Be(1, "the released lock let a later trigger run the refresh");
        _store.GetEntry(key)!.Value.Should().Be("fresh");
    }

    [Fact]
    public async Task should_clear_eager_stamp_before_factory_starts()
    {
        // given
        var key = Faker.Random.AlphaNumeric(8);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        _store.SetEntry(key, "old", now.AddMinutes(5), now.AddMinutes(5), eagerRefreshAt: now.AddSeconds(-1));
        var coordinator = _CreateCoordinator();
        var backgroundFinished = _WaitForBackgroundFinished(coordinator);
        var factoryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var factoryGate = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var options = _CreateOptions(duration: TimeSpan.FromMinutes(10), eagerRefreshThreshold: 0.5f);

        async ValueTask<string?> factory(CancellationToken cancellationToken)
        {
            factoryStarted.SetResult();
            return await factoryGate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        // when
        var result = await coordinator.GetOrAddAsync(_store, key, factory, options, AbortToken);
        await factoryStarted.Task;

        // then — the gate write cleared the stamp while the old value is still served
        result.Value.Should().Be("old");
        var gated = _store.GetEntry(key)!;
        gated.Value.Should().Be("old");
        gated.EagerRefreshAt.Should().BeNull();

        factoryGate.SetResult("fresh");
        await backgroundFinished;
        _store.GetEntry(key)!.Value.Should().Be("fresh");
    }

    [Fact]
    public async Task should_not_stampede_eager_refresh_under_concurrency()
    {
        // given
        var key = Faker.Random.AlphaNumeric(8);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        _store.SetEntry(key, "old", now.AddMinutes(5), now.AddMinutes(5), eagerRefreshAt: now.AddSeconds(-1));
        var coordinator = _CreateCoordinator();
        var backgroundFinished = _WaitForBackgroundFinished(coordinator);
        var factoryCalls = 0;
        var factoryGate = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var options = _CreateOptions(duration: TimeSpan.FromMinutes(10), eagerRefreshThreshold: 0.5f);

        async ValueTask<string?> factory(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref factoryCalls);
            return await factoryGate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        // when — many concurrent fresh hits past the eager point
        var results = await Task.WhenAll(
            Enumerable
                .Range(0, 10)
                .Select(_ => coordinator.GetOrAddAsync(_store, key, factory, options, AbortToken).AsTask())
        );

        factoryGate.SetResult("fresh");
        await backgroundFinished;

        // then — everyone was served the fresh-enough value and exactly one refresh ran
        results.Should().AllSatisfy(result => result.Value.Should().Be("old"));
        factoryCalls.Should().Be(1);
        _store.GetEntry(key)!.Value.Should().Be("fresh");
    }

    [Fact]
    public async Task should_keep_entry_untouched_when_eager_factory_fails()
    {
        // given
        var key = Faker.Random.AlphaNumeric(8);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var logicalExpiresAt = now.AddMinutes(5);
        _store.SetEntry(key, "old", logicalExpiresAt, logicalExpiresAt, eagerRefreshAt: now.AddSeconds(-1));
        var coordinator = _CreateCoordinator();
        var backgroundFinished = _WaitForBackgroundFinished(coordinator);
        var options = _CreateOptions(duration: TimeSpan.FromMinutes(10), eagerRefreshThreshold: 0.5f);

        static ValueTask<string?> factory(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("refresh failed");

        // when
        var result = await coordinator.GetOrAddAsync(_store, key, factory, options, AbortToken);
        await backgroundFinished;

        // then — the value and expirations are untouched; only the eager stamp was consumed by the gate
        result.Value.Should().Be("old");
        result.IsStale.Should().BeFalse();
        var entry = _store.GetEntry(key)!;
        entry.Value.Should().Be("old");
        entry.LogicalExpiresAt.Should().Be(logicalExpiresAt);
        entry.EagerRefreshAt.Should().BeNull();
    }

    [Fact]
    public async Task should_abandon_eager_refresh_when_background_ceiling_fires()
    {
        // given
        var key = Faker.Random.AlphaNumeric(8);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        _store.SetEntry(key, "old", now.AddMinutes(5), now.AddMinutes(5), eagerRefreshAt: now.AddSeconds(-1));
        using var coordinator = _CreateCoordinator();
#pragma warning disable CA2025 // Do not pass 'IDisposable' instances into unawaited tasks
        var backgroundFinished = _WaitForBackgroundFinished(coordinator);
        var ceilingRegistered = _WaitForBackgroundCeilingRegistered(coordinator);
#pragma warning restore CA2025 // Do not pass 'IDisposable' instances into unawaited tasks
        var factoryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var factoryGate = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var options = _CreateOptions(
            duration: TimeSpan.FromMinutes(10),
            backgroundFactoryCeiling: TimeSpan.FromSeconds(2),
            eagerRefreshThreshold: 0.5f
        );

        async ValueTask<string?> factory(CancellationToken cancellationToken)
        {
            factoryStarted.SetResult();
            // Ignores cancellation: simulates a non-cooperative factory.
            return await factoryGate.Task.ConfigureAwait(false);
        }

        // when
        var result = await coordinator.GetOrAddAsync(_store, key, factory, options, AbortToken);
        await factoryStarted.Task;
        await ceilingRegistered;
        _timeProvider.Advance(TimeSpan.FromSeconds(2));
        await backgroundFinished;

        // then — the refresh was abandoned without touching the entry; a late success is dropped
        result.Value.Should().Be("old");
        _store.GetEntry(key)!.Value.Should().Be("old");

        factoryGate.SetResult("late");
        await Task.Yield();
        _store.GetEntry(key)!.Value.Should().Be("old");
    }

    [Fact]
    public async Task should_not_trigger_eager_refresh_before_threshold()
    {
        // given — a fresh entry whose eager point is still in the future
        var key = Faker.Random.AlphaNumeric(8);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        _store.SetEntry(key, "old", now.AddMinutes(5), now.AddMinutes(5), eagerRefreshAt: now.AddMinutes(1));
        var coordinator = _CreateCoordinator();
        var factoryCalls = 0;
        var options = _CreateOptions(duration: TimeSpan.FromMinutes(10), eagerRefreshThreshold: 0.5f);

        ValueTask<string?> factory(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref factoryCalls);
            return ValueTask.FromResult<string?>("fresh");
        }

        // when
        var result = await coordinator.GetOrAddAsync(_store, key, factory, options, AbortToken);

        // then
        result.Value.Should().Be("old");
        factoryCalls.Should().Be(0);
        _store.GetEntry(key)!.EagerRefreshAt.Should().Be(now.AddMinutes(1));
    }

    [Fact]
    public async Task should_pass_cold_context_to_conditional_factory_on_miss()
    {
        // given
        var key = Faker.Random.AlphaNumeric(8);
        CacheFactoryContext<string>? observed = null;

        // when
        var result = await _CreateCoordinator()
            .GetOrAddAsync<string>(
                _store,
                key,
                (context, _) =>
                {
                    observed = context;
                    return ValueTask.FromResult(context.Modified("fresh"));
                },
                _CreateOptions(),
                AbortToken
            );

        // then
        result.Value.Should().Be("fresh");
        observed.Should().NotBeNull();
        observed!.Key.Should().Be(key);
        observed.HasStaleValue.Should().BeFalse();
        observed.StaleValue.HasValue.Should().BeFalse();
        observed.ETag.Should().BeNull();
        observed.LastModifiedAt.Should().BeNull();
        observed.Tags.Should().BeNull();
    }

    [Fact]
    public async Task should_pass_stale_value_and_validators_to_conditional_factory()
    {
        // given — a logically expired but physically present entry carrying the full metadata envelope
        var key = Faker.Random.AlphaNumeric(8);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var lastModified = now.AddMinutes(-30);
        var tags = new[] { "tenant:1", "products" };
        _store.SetEntry(
            key,
            "stale",
            now.AddSeconds(-1),
            now.AddMinutes(5),
            etag: "W/\"v1\"",
            lastModifiedAt: lastModified,
            tags: tags
        );
        CacheFactoryContext<string>? observed = null;

        // when
        await _CreateCoordinator()
            .GetOrAddAsync<string>(
                _store,
                key,
                (context, _) =>
                {
                    observed = context;
                    return ValueTask.FromResult(context.Modified("fresh"));
                },
                _CreateOptions(),
                AbortToken
            );

        // then
        observed.Should().NotBeNull();
        observed!.HasStaleValue.Should().BeTrue();
        observed.StaleValue.HasValue.Should().BeTrue();
        observed.StaleValue.Value.Should().Be("stale");
        observed.ETag.Should().Be("W/\"v1\"");
        observed.LastModifiedAt.Should().Be(lastModified);
        observed.Tags.Should().BeEquivalentTo(tags);
    }

    [Fact]
    public async Task should_extend_entry_when_conditional_factory_reports_not_modified()
    {
        // given — a stale entry whose origin value is still current
        var key = Faker.Random.AlphaNumeric(8);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var duration = TimeSpan.FromSeconds(5);
        var maxDuration = TimeSpan.FromMinutes(1);
        var lastModified = now.AddMinutes(-30);
        _store.SetEntry(
            key,
            "cached",
            now.AddSeconds(-1),
            now.AddMinutes(5),
            etag: "W/\"v1\"",
            lastModifiedAt: lastModified
        );
        var options = _CreateOptions(
            duration: duration,
            isFailSafeEnabled: true,
            maxDuration: maxDuration,
            eagerRefreshThreshold: 0.5f
        );

        // when
        var result = await _CreateCoordinator()
            .GetOrAddAsync<string>(
                _store,
                key,
                (context, _) => ValueTask.FromResult(context.NotModified()),
                options,
                AbortToken
            );

        // then — the existing value is returned as fresh and the entry is re-stamped end to end
        result.HasValue.Should().BeTrue();
        result.Value.Should().Be("cached");
        result.IsStale.Should().BeFalse();
        var entry = _store.GetEntry(key)!;
        entry.Value.Should().Be("cached");
        entry.LogicalExpiresAt.Should().Be(now.Add(duration));
        entry.PhysicalExpiresAt.Should().Be(now.Add(maxDuration));
        entry.EagerRefreshAt.Should().Be(now.AddSeconds(2.5));
        entry.ETag.Should().Be("W/\"v1\"");
        entry.LastModifiedAt.Should().Be(lastModified);
    }

    [Fact]
    public async Task should_throw_when_conditional_factory_reports_not_modified_on_cold_miss()
    {
        // given
        var key = Faker.Random.AlphaNumeric(8);

        // when — there is no cached value to extend, so NotModified() is a programming error
        var act = async () =>
            await _CreateCoordinator()
                .GetOrAddAsync<string>(
                    _store,
                    key,
                    (context, _) => ValueTask.FromResult(context.NotModified()),
                    _CreateOptions(),
                    AbortToken
                );

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*no cached value*");
        _store.GetEntry(key).Should().BeNull();
    }

    [Fact]
    public async Task should_write_value_and_validators_when_conditional_factory_reports_modified()
    {
        // given
        var key = Faker.Random.AlphaNumeric(8);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var lastModified = now.AddMinutes(-5);

        // when
        var result = await _CreateCoordinator()
            .GetOrAddAsync<string>(
                _store,
                key,
                (context, _) => ValueTask.FromResult(context.Modified("fresh", "W/\"v2\"", lastModified)),
                _CreateOptions(),
                AbortToken
            );

        // then
        result.Value.Should().Be("fresh");
        result.IsStale.Should().BeFalse();
        var entry = _store.GetEntry(key)!;
        entry.Value.Should().Be("fresh");
        entry.ETag.Should().Be("W/\"v2\"");
        entry.LastModifiedAt.Should().Be(lastModified);
    }

    [Fact]
    public async Task should_honor_adaptive_duration_replaced_by_conditional_factory()
    {
        // given
        var key = Faker.Random.AlphaNumeric(8);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var options = _CreateOptions(duration: TimeSpan.FromMinutes(10));

        // when — the factory shortens the duration before returning (adaptive caching)
        var result = await _CreateCoordinator()
            .GetOrAddAsync<string>(
                _store,
                key,
                (context, _) =>
                {
                    context.Options = context.Options with { Duration = TimeSpan.FromSeconds(30) };
                    return ValueTask.FromResult(context.Modified("fresh"));
                },
                options,
                AbortToken
            );

        // then — the stored expirations honor the adaptive duration, not the call's
        result.Value.Should().Be("fresh");
        var entry = _store.GetEntry(key)!;
        entry.LogicalExpiresAt.Should().Be(now.AddSeconds(30));
        entry.PhysicalExpiresAt.Should().Be(now.AddSeconds(30));
    }

    [Fact]
    public async Task should_throw_after_factory_ran_when_adaptive_options_are_invalid()
    {
        // given
        var key = Faker.Random.AlphaNumeric(8);
        var factoryRan = false;

        // when — the factory sets an invalid option; validation happens at write time, after the factory ran
        var act = async () =>
            await _CreateCoordinator()
                .GetOrAddAsync<string>(
                    _store,
                    key,
                    (context, _) =>
                    {
                        factoryRan = true;
                        context.Options = context.Options with { JitterMaxDuration = TimeSpan.FromMilliseconds(-1) };
                        return ValueTask.FromResult(context.Modified("fresh"));
                    },
                    _CreateOptions(),
                    AbortToken
                );

        // then — the invalid adaptive mutation throws and nothing is written
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
        factoryRan.Should().BeTrue();
        _store.GetEntry(key).Should().BeNull();
    }

    [Fact]
    public async Task should_serve_stale_when_conditional_factory_throws_with_failsafe()
    {
        // given
        var key = Faker.Random.AlphaNumeric(8);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        _store.SetEntry(key, "stale", now.AddSeconds(-1), now.AddMinutes(5));

        // when — the conditional factory fails exactly like a simple factory would
        var result = await _CreateCoordinator()
            .GetOrAddAsync(
                _store,
                key,
                (CacheFactoryContext<string> _, CancellationToken _) =>
                    throw new InvalidOperationException("downstream unavailable"),
                _CreateOptions(isFailSafeEnabled: true),
                AbortToken
            );

        // then — fail-safe behavior is unchanged for the conditional overload
        result.Value.Should().Be("stale");
        result.IsStale.Should().BeTrue();
    }

    [Fact]
    public async Task should_restamp_entry_in_background_when_conditional_factory_reports_not_modified_after_soft_timeout()
    {
        // given
        var key = Faker.Random.AlphaNumeric(8);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var duration = TimeSpan.FromSeconds(5);
        _store.SetEntry(key, "stale", now.AddSeconds(-1), now.AddMinutes(5), etag: "W/\"v1\"");
        var coordinator = _CreateCoordinator();
        var backgroundFinished = _WaitForBackgroundFinished(coordinator);
        var timeoutRegistered = _WaitForFactoryTimeoutRegistered(coordinator);
        var factoryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var factoryGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var options = _CreateOptions(
            duration: duration,
            isFailSafeEnabled: true,
            factorySoftTimeout: TimeSpan.FromSeconds(1),
            factoryHardTimeout: TimeSpan.FromSeconds(10),
            // Required now: fail-safe + finite soft timeout needs a finite ceiling (lock-hold guard).
            backgroundFactoryCeiling: TimeSpan.FromSeconds(5)
        );

        async ValueTask<CacheFactoryResult<string>> factory(
            CacheFactoryContext<string> context,
            CancellationToken cancellationToken
        )
        {
            factoryStarted.SetResult();
            await factoryGate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            return context.NotModified();
        }

        // when — the soft timeout serves stale, then the detached factory completes with NotModified
        var resultTask = coordinator.GetOrAddAsync<string>(_store, key, factory, options, AbortToken).AsTask();
        await factoryStarted.Task;
        await timeoutRegistered;
        _timeProvider.Advance(TimeSpan.FromSeconds(1));
        var result = await resultTask;
        factoryGate.SetResult();
        await backgroundFinished;

        // then — the background completion re-stamped the existing value as fresh, preserving its validators
        result.Value.Should().Be("stale");
        result.IsStale.Should().BeTrue();
        var entry = _store.GetEntry(key)!;
        entry.Value.Should().Be("stale");
        entry.ETag.Should().Be("W/\"v1\"");
        var writeNow = now.AddSeconds(1);
        entry.LogicalExpiresAt.Should().Be(writeNow.Add(duration));
        entry.PhysicalExpiresAt.Should().Be(writeNow.AddMinutes(1));
    }

    [Fact]
    public async Task should_extend_entry_when_eager_refresh_factory_reports_not_modified()
    {
        // given — a fresh entry whose eager point has already passed
        var key = Faker.Random.AlphaNumeric(8);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        _store.SetEntry(
            key,
            "old",
            now.AddMinutes(5),
            now.AddMinutes(5),
            eagerRefreshAt: now.AddSeconds(-1),
            etag: "W/\"v7\""
        );
        var coordinator = _CreateCoordinator();
        var backgroundFinished = _WaitForBackgroundFinished(coordinator);
        var options = _CreateOptions(duration: TimeSpan.FromMinutes(10), eagerRefreshThreshold: 0.5f);

        // when — the eager refresh runs a conditional factory that reports the value unchanged
        var result = await coordinator.GetOrAddAsync<string>(
            _store,
            key,
            (context, _) => ValueTask.FromResult(context.NotModified()),
            options,
            AbortToken
        );

        await backgroundFinished;

        // then — the value is untouched while the lifetime and eager stamp are extended
        result.Value.Should().Be("old");
        result.IsStale.Should().BeFalse();
        var entry = _store.GetEntry(key)!;
        entry.Value.Should().Be("old");
        entry.ETag.Should().Be("W/\"v7\"");
        entry.LogicalExpiresAt.Should().Be(now.AddMinutes(10));
        entry.EagerRefreshAt.Should().Be(now.AddMinutes(5));
    }

    [Fact]
    public async Task should_persist_context_tags_on_modified_and_not_modified_writes()
    {
        // given
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var modifiedKey = Faker.Random.AlphaNumeric(8);
        var notModifiedKey = Faker.Random.AlphaNumeric(8);
        _store.SetEntry(notModifiedKey, "cached", now.AddSeconds(-1), now.AddMinutes(5), tags: ["old-tag"]);
        var coordinator = _CreateCoordinator();

        // when — both factories replace the context tags before returning
        await coordinator.GetOrAddAsync<string>(
            _store,
            modifiedKey,
            (context, _) =>
            {
                context.Tags = ["tenant:1", "products"];
                return ValueTask.FromResult(context.Modified("fresh"));
            },
            _CreateOptions(),
            AbortToken
        );

        await coordinator.GetOrAddAsync<string>(
            _store,
            notModifiedKey,
            (context, _) =>
            {
                context.Tags = ["tenant:2"];
                return ValueTask.FromResult(context.NotModified());
            },
            _CreateOptions(isFailSafeEnabled: true),
            AbortToken
        );

        // then
        _store.GetEntry(modifiedKey)!.Tags.Should().BeEquivalentTo("tenant:1", "products");
        _store.GetEntry(notModifiedKey)!.Tags.Should().BeEquivalentTo("tenant:2");
    }

    [Fact]
    public async Task should_not_resurrect_removed_key_when_factory_write_lands_after_mid_flight_removal()
    {
        // given — a stale entry whose refresh factory is in flight
        var key = Faker.Random.AlphaNumeric(8);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        _store.SetEntry(key, "stale", now.AddSeconds(-1), now.AddMinutes(5));
        var coordinator = _CreateCoordinator();
        var factoryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var factoryGate = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);

        async ValueTask<string?> factory(CancellationToken cancellationToken)
        {
            factoryStarted.SetResult();
            return await factoryGate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        // when — another actor removes the key while the factory is running, then the factory completes
        var resultTask = coordinator.GetOrAddAsync(_store, key, factory, _CreateOptions(), AbortToken).AsTask();
        await factoryStarted.Task.WaitAsync(AbortToken);
        _RemoveEntryDirectly(_store, key);
        _store.GetEntry(key).Should().BeNull("the concurrent removal must be visible before the factory lands");
        factoryGate.SetResult("fresh");
        var result = await resultTask;

        // then — the late factory result is still returned to the caller, but the store write is conditional on
        // the stale entry snapshot and must not resurrect a key another actor removed while the factory ran.
        result.Value.Should().Be("fresh");
        _store.GetEntry(key).Should().BeNull();
    }

    [Fact]
    public async Task should_not_overwrite_concurrent_writer_value_when_in_flight_factory_completes_last()
    {
        // given — a stale entry whose refresh factory is in flight
        var key = Faker.Random.AlphaNumeric(8);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        _store.SetEntry(key, "stale", now.AddSeconds(-1), now.AddMinutes(5));
        var coordinator = _CreateCoordinator();
        var factoryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var factoryGate = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);

        async ValueTask<string?> factory(CancellationToken cancellationToken)
        {
            factoryStarted.SetResult();
            return await factoryGate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        // when — a concurrent writer replaces the entry mid-factory, then the factory completes
        var resultTask = coordinator.GetOrAddAsync(_store, key, factory, _CreateOptions(), AbortToken).AsTask();
        await factoryStarted.Task.WaitAsync(AbortToken);
        _store.SetEntry(key, "concurrent", now.AddMinutes(5), now.AddMinutes(5));
        factoryGate.SetResult("fresh");
        var result = await resultTask;

        // then — the late factory result is returned to its caller, but the conditional store write observes the
        // changed live entry and leaves the concurrent writer's newer value intact.
        result.Value.Should().Be("fresh");
        _store.GetEntry(key)!.Value.Should().Be("concurrent");
    }

    [Fact]
    public async Task should_not_resurrect_removed_key_when_eager_factory_write_lands_after_mid_flight_removal()
    {
        // given — a fresh entry past its eager point; the detached eager factory is started and held open
        var key = Faker.Random.AlphaNumeric(8);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        _store.SetEntry(key, "old", now.AddMinutes(5), now.AddMinutes(5), eagerRefreshAt: now.AddSeconds(-1));
        var coordinator = _CreateCoordinator();
        var backgroundFinished = _WaitForBackgroundFinished(coordinator);
        var factoryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var factoryGate = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var options = _CreateOptions(duration: TimeSpan.FromMinutes(10), eagerRefreshThreshold: 0.5f);

        async ValueTask<string?> factory(CancellationToken cancellationToken)
        {
            factoryStarted.SetResult();
            return await factoryGate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        // when — another actor removes the key while the eager factory is running, then the factory completes
        var result = await coordinator.GetOrAddAsync(_store, key, factory, options, AbortToken);
        await factoryStarted.Task.WaitAsync(AbortToken);
        _RemoveEntryDirectly(_store, key);
        _store.GetEntry(key).Should().BeNull("the concurrent removal must be visible before the eager write lands");
        factoryGate.SetResult("fresh");
        await backgroundFinished;

        // then — the triggering caller was served the fresh-enough value, and the late eager write is CAS-guarded
        // against the post-gate entry snapshot, so it must not resurrect a key another actor removed mid-flight.
        result.Value.Should().Be("old");
        _store.GetEntry(key).Should().BeNull();
    }

    // #3 — eager-refresh fail-closed: when the post-gate re-read returns NotFound (a Remove landed in the
    // gate-write -> re-read window, or the re-read itself degraded), the eager write is ABANDONED before the factory
    // runs, rather than degrading to an unconditional (null-stamp) write that would resurrect the removed key. This
    // is distinct from the sibling test above, where the removal lands AFTER the post-gate read and the final write's
    // CAS is what fails.
    [Fact]
    public async Task should_abandon_eager_refresh_without_factory_when_post_gate_reread_returns_not_found()
    {
        // given — a fresh entry past its eager point
        var key = Faker.Random.AlphaNumeric(8);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        _store.SetEntry(key, "old", now.AddMinutes(5), now.AddMinutes(5), eagerRefreshAt: now.AddSeconds(-1));
        var coordinator = _CreateCoordinator();
        var backgroundFinished = _WaitForBackgroundFinished(coordinator);
        var factoryCalls = 0;
        var options = _CreateOptions(duration: TimeSpan.FromMinutes(10), eagerRefreshThreshold: 0.5f);

        // The eager gate write clears EagerRefreshAt; the first read AFTER that (the post-gate re-read) is where a
        // concurrent Remove is modelled to have landed. Removing the key on that read forces the re-read to return
        // NotFound, which drives the fail-closed guard. Earlier reads (pre-lock, under-lock double-check) still see the
        // eager stamp set, so they are left untouched.
        _store.TryGetEntryOverride = (k, _) =>
        {
            if (_store.GetEntry(k) is { EagerRefreshAt: null })
            {
                _store.RemoveEntry(k);
            }

            return null; // always fall through to the real (now-absent) store read
        };

        ValueTask<string?> factory(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref factoryCalls);
            return ValueTask.FromResult<string?>("fresh");
        }

        // when — the triggering caller returns the still-fresh value; the detached refresh re-reads NotFound post-gate
        var result = await coordinator.GetOrAddAsync(_store, key, factory, options, AbortToken);
        await backgroundFinished;

        // then — the eager write was abandoned: the factory never ran, only the gate write happened (no result write),
        // and the removed key was NOT resurrected. A regression to the unconditional null-stamp write would re-add it.
        result.Value.Should().Be("old");
        result.IsStale.Should().BeFalse();
        factoryCalls.Should().Be(0, "a post-gate NotFound must abandon the refresh before the factory runs");
        _store.SetEntryCalls.Should().Be(1, "only the gate write happened; the eager result write must not run");
        _store.GetEntry(key).Should().BeNull("a lost/removed gate entry must not be resurrected by the eager write");
    }

    [Fact]
    public async Task should_not_clobber_concurrent_upsert_when_eager_factory_write_lands_mid_flight()
    {
        // given — a fresh entry past its eager point; the detached eager factory is started and held open
        var key = Faker.Random.AlphaNumeric(8);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        _store.SetEntry(key, "old", now.AddMinutes(5), now.AddMinutes(5), eagerRefreshAt: now.AddSeconds(-1));
        var coordinator = _CreateCoordinator();
        var backgroundFinished = _WaitForBackgroundFinished(coordinator);
        var factoryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var factoryGate = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var options = _CreateOptions(duration: TimeSpan.FromMinutes(10), eagerRefreshThreshold: 0.5f);

        async ValueTask<string?> factory(CancellationToken cancellationToken)
        {
            factoryStarted.SetResult();
            return await factoryGate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        // when — a concurrent writer replaces the entry while the eager factory is running, then it completes
        var result = await coordinator.GetOrAddAsync(_store, key, factory, options, AbortToken);
        await factoryStarted.Task.WaitAsync(AbortToken);
        _store.SetEntry(key, "concurrent", now.AddMinutes(5), now.AddMinutes(5));
        factoryGate.SetResult("fresh");
        await backgroundFinished;

        // then — the stale eager write observes the changed concurrency stamp and fails without retry, leaving
        // the concurrent writer's newer value intact.
        result.Value.Should().Be("old");
        _store.GetEntry(key)!.Value.Should().Be("concurrent");
    }

    [Fact]
    public async Task should_join_in_flight_eager_refresh_instead_of_second_factory_when_entry_expires_mid_refresh()
    {
        // given — a fresh entry past its eager point; the eager refresh factory is started and held open
        var key = Faker.Random.AlphaNumeric(8);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        _store.SetEntry(key, "old", now.AddMinutes(2), now.AddMinutes(10), eagerRefreshAt: now.AddSeconds(-1));
        var coordinator = _CreateCoordinator();
        var backgroundFinished = _WaitForBackgroundFinished(coordinator);
        var factoryCalls = 0;
        var factoryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var factoryGate = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var options = _CreateOptions(duration: TimeSpan.FromMinutes(10), eagerRefreshThreshold: 0.5f);

        async ValueTask<string?> factory(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref factoryCalls);
            factoryStarted.TrySetResult();
            return await factoryGate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        // when — the triggering caller returns the still-fresh value and the eager factory holds the keyed lock
        var first = await coordinator.GetOrAddAsync(_store, key, factory, options, AbortToken);
        await factoryStarted.Task.WaitAsync(AbortToken);
        first.Value.Should().Be("old");

        // the entry crosses logical expiration while the eager refresh is still running
        _timeProvider.Advance(TimeSpan.FromMinutes(3));

        // a normal GetOrAdd caller arrives: it must block on the keyed lock held by the eager refresh
        var second = coordinator.GetOrAddAsync(_store, key, factory, options, AbortToken).AsTask();
        await Task.Delay(50, AbortToken); // give the joiner real time to park on the lock
        second.IsCompleted.Should().BeFalse("the joiner must wait for the in-flight eager refresh");

        factoryGate.SetResult("fresh");
        await backgroundFinished;
        var secondResult = await second;

        // then — NO double factory: the joiner re-checked under the lock and served the eager refresh's value
        secondResult.Value.Should().Be("fresh");
        secondResult.IsStale.Should().BeFalse();
        factoryCalls.Should().Be(1);
        _store.GetEntry(key)!.Value.Should().Be("fresh");
    }

    [Fact]
    public async Task should_clamp_physical_expiration_to_duration_when_fail_safe_max_duration_is_shorter()
    {
        // given — FailSafeMaxDuration SHORTER than Duration: physical = max(Duration, FailSafeMaxDuration)
        var key = Faker.Random.AlphaNumeric(8);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var duration = TimeSpan.FromMinutes(10);
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

        // then — the written physical expiration equals Duration (never shorter than the logical lifetime)
        result.Value.Should().Be("fresh");
        var entry = _store.GetEntry(key)!;
        entry.LogicalExpiresAt.Should().Be(now.Add(duration));
        entry.PhysicalExpiresAt.Should().Be(now.Add(duration));
    }

    [Fact]
    public async Task should_invoke_factory_once_for_high_concurrency_cold_stampede()
    {
        // given — a REAL time provider (no fake-time orchestration) and a briefly-gated factory
        var key = Faker.Random.AlphaNumeric(8);
        var store = new FakeFactoryCacheStore();
        using var coordinator = new FactoryCacheCoordinator(
            TimeProvider.System,
            NullLogger<FactoryCacheCoordinator>.Instance
        );
        var factoryCalls = 0;
        var gate = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var options = _CreateOptions(duration: TimeSpan.FromMinutes(5));

        async ValueTask<string?> factory(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref factoryCalls);
            return await gate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        // when — ~500 concurrent cold callers stampede one key
        var tasks = Enumerable
            .Range(0, 500)
            .Select(_ => Task.Run(() => coordinator.GetOrAddAsync(store, key, factory, options, AbortToken).AsTask()))
            .ToArray();

        await Task.Delay(100, AbortToken); // let the pack pile up on the keyed lock before the factory completes
        gate.SetResult("fresh");
        var results = await Task.WhenAll(tasks);

        // then — single flight: exactly one factory call; every caller got the fresh value
        factoryCalls.Should().Be(1);
        results.Should().HaveCount(500);
        results.Should().OnlyContain(result => result.HasValue && result.Value == "fresh");
    }

    // ---------------------------------------------------------------------------------------------------------
    // FusionCache L1 parity ports. Each pins a behavior FusionCache covers against the headless coordinator API.
    // Names map to the FusionCache originals in L1Tests_Async.cs; intent is preserved, mechanics use FakeTimeProvider
    // and the FactoryCacheCoordinator store-primitive instead of FusionCache's real-clock Task.Delay.
    // ---------------------------------------------------------------------------------------------------------

    // FusionCache: DoesNotReturnStaleDataIfFactorySucceedsAsync.
    // A logically-expired entry whose refresh factory SUCCEEDS must return the NEW value, not the stale reserve —
    // the success path bypasses fail-safe entirely. This is the success-side complement to the stale-on-FAILURE
    // tests above (should_serve_stale_when_factory_throws_within_physical_window).
    [Fact]
    public async Task should_return_fresh_value_when_factory_succeeds_after_logical_expiry()
    {
        // given — a physically-retained but logically-expired fail-safe reserve
        var key = Faker.Random.AlphaNumeric(8);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        _store.SetEntry(key, "stale", now.AddSeconds(-1), now.AddMinutes(5));
        var options = _CreateOptions(duration: TimeSpan.FromSeconds(1), isFailSafeEnabled: true);

        // when — the refresh factory succeeds
        var result = await _CreateCoordinator()
            .GetOrAddAsync(_store, key, _FactoryReturns("fresh"), options, AbortToken);

        // then — the new value wins; nothing stale is served despite the usable reserve
        result.Value.Should().Be("fresh");
        result.IsStale.Should().BeFalse();
        _store.GetEntry(key)!.Value.Should().Be("fresh");
    }

    // FusionCache: AdaptiveCachingDoesNotChangeOptionsAsync.
    // A factory that mutates ctx.Options must NOT mutate the caller's original CacheEntryOptions. Headless makes this
    // structurally guaranteed: CacheEntryOptions is a readonly record struct, so the context holds a by-value copy and
    // a `with` replacement on ctx.Options cannot alias the caller's instance. This pins that guarantee.
    [Fact]
    public async Task should_not_mutate_caller_options_when_adaptive_factory_replaces_context_options()
    {
        // given
        var key = Faker.Random.AlphaNumeric(8);
        var callerOptions = _CreateOptions(duration: TimeSpan.FromSeconds(10));

        // when — the factory adaptively shortens the duration on its context copy
        var result = await _CreateCoordinator()
            .GetOrAddAsync<string>(
                _store,
                key,
                (context, _) =>
                {
                    context.Options = context.Options with { Duration = TimeSpan.FromSeconds(20) };
                    return ValueTask.FromResult(context.Modified("fresh"));
                },
                callerOptions,
                AbortToken
            );

        // then — the adaptive mutation was honored on the write, but the caller's options object is untouched
        result.Value.Should().Be("fresh");
        callerOptions.Duration.Should().Be(TimeSpan.FromSeconds(10));
    }

    // FusionCache: AdaptiveCachingWithBackgroundFactoryCompletionAsync.
    // A factory that soft-times out and completes in the BACKGROUND, replacing ctx.Options (adaptive caching), must
    // apply the adaptive options to the eventually-written entry. Mirrors should_return_stale_on_soft_timeout_and_
    // complete_factory_in_background plus the adaptive-duration write path.
    [Fact]
    public async Task should_apply_adaptive_options_when_background_completion_writes_after_soft_timeout()
    {
        // given — a stale reserve and a soft timeout that detaches the factory
        var key = Faker.Random.AlphaNumeric(8);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        _store.SetEntry(key, "stale", now.AddSeconds(-1), now.AddMinutes(5));
        var coordinator = _CreateCoordinator();
        var backgroundFinished = _WaitForBackgroundFinished(coordinator);
        var timeoutRegistered = _WaitForFactoryTimeoutRegistered(coordinator);
        var factoryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var factoryGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var options = _CreateOptions(
            duration: TimeSpan.FromSeconds(5),
            isFailSafeEnabled: true,
            factorySoftTimeout: TimeSpan.FromSeconds(1),
            factoryHardTimeout: TimeSpan.FromSeconds(10),
            // Required: fail-safe + finite soft timeout needs a finite ceiling (lock-hold guard).
            backgroundFactoryCeiling: TimeSpan.FromSeconds(5)
        );

        async ValueTask<CacheFactoryResult<string>> factory(
            CacheFactoryContext<string> context,
            CancellationToken cancellationToken
        )
        {
            factoryStarted.SetResult();
            await factoryGate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            // Adaptive: replace the duration on the background-completion write.
            context.Options = context.Options with
            {
                Duration = TimeSpan.FromSeconds(30),
            };
            return context.Modified("fresh");
        }

        // when — soft timeout serves stale, then the detached factory completes with adaptive options
        var resultTask = coordinator.GetOrAddAsync<string>(_store, key, factory, options, AbortToken).AsTask();
        await factoryStarted.Task;
        await timeoutRegistered;
        _timeProvider.Advance(TimeSpan.FromSeconds(1));
        var result = await resultTask;
        var writeNow = _timeProvider.GetUtcNow().UtcDateTime;
        factoryGate.SetResult();
        await backgroundFinished;

        // then — the background write honors the adaptive 30s duration, not the call's 5s duration
        result.Value.Should().Be("stale");
        result.IsStale.Should().BeTrue();
        var entry = _store.GetEntry(key)!;
        entry.Value.Should().Be("fresh");
        entry.LogicalExpiresAt.Should().Be(writeNow.AddSeconds(30));
    }

    // FusionCache: AdaptiveCachingCanWorkOnExceptionAsync.
    // PINNED DIVERGENCE FROM FusionCache: in FusionCache a factory that disables fail-safe via ctx.Options in a
    // finally and then THROWS lets the exception propagate (the catch reads the live, mutated options). Headless
    // reads fail-safe activation from the ORIGINAL caller options captured before the factory ran — ctx.Options
    // only governs the WRITE, which never happens on a throw (FactoryCacheCoordinator.cs catch at line ~260 uses
    // `options`, not `context.Options`). So fail-safe still activates and the stale reserve is served. This pins the
    // headless semantics: adaptive fail-safe-disable does NOT influence exception handling on the throwing call.
    [Fact]
    public async Task should_still_serve_stale_when_adaptive_factory_disables_failsafe_then_throws()
    {
        // given — a usable stale reserve and caller-enabled fail-safe
        var key = Faker.Random.AlphaNumeric(8);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        _store.SetEntry(key, "stale", now.AddSeconds(-1), now.AddMinutes(5));

        // when — the factory disables fail-safe on its context copy (in a finally) and throws
        var result = await _CreateCoordinator()
            .GetOrAddAsync(
                _store,
                key,
                (CacheFactoryContext<string> context, CancellationToken _) =>
                {
                    try
                    {
                        throw new InvalidOperationException("downstream unavailable");
                    }
                    finally
                    {
                        context.Options = context.Options with { IsFailSafeEnabled = false };
                    }
                },
                _CreateOptions(isFailSafeEnabled: true),
                AbortToken
            );

        // then — headless serves stale (the adaptive disable did not reach the exception-handling decision)
        result.Value.Should().Be("stale");
        result.IsStale.Should().BeTrue();
    }

    // FusionCache: CanEagerRefreshNoCancellationAsync.
    // An eager refresh fires in the background detached from the caller's token. Cancelling the ORIGINAL caller's
    // token after the refresh starts must NOT abort the detached factory — it runs under a fresh internal CTS
    // (FactoryCacheCoordinator.EagerRefresh.cs: _StartEagerFactoryAsync news up its own CancellationTokenSource).
    [Fact]
    public async Task should_complete_eager_refresh_even_when_caller_token_is_cancelled()
    {
        // given — a fresh entry whose eager point has already passed
        var key = Faker.Random.AlphaNumeric(8);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        _store.SetEntry(key, "old", now.AddMinutes(5), now.AddMinutes(5), eagerRefreshAt: now.AddSeconds(-1));
        var coordinator = _CreateCoordinator();
        var backgroundFinished = _WaitForBackgroundFinished(coordinator);
        var factoryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var factoryGate = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var options = _CreateOptions(duration: TimeSpan.FromMinutes(10), eagerRefreshThreshold: 0.5f);
        var cts = new CancellationTokenSource();

        async ValueTask<string?> factory(CancellationToken cancellationToken)
        {
            factoryStarted.SetResult();
            return await factoryGate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            // when — the eager refresh starts, then the caller cancels its own token
            var result = await coordinator.GetOrAddAsync(_store, key, factory, options, cts.Token);
            await factoryStarted.Task;
            await cts.CancelAsync();
            factoryGate.SetResult("fresh");
            await backgroundFinished;

            // then — the caller got the still-fresh value, and the detached refresh completed despite the cancellation
            result.Value.Should().Be("old");
            _store.GetEntry(key)!.Value.Should().Be("fresh");
        }
        finally
        {
            // Dispose only after the detached refresh has fully settled (backgroundFinished awaited), so the
            // token source is never freed while a task might still observe its token (CA2025).
            cts.Dispose();
        }
    }

    // FusionCache: CanEagerRefreshWithInfiniteDurationAsync.
    // Eager refresh configured on an entry with a very large duration works (the eager threshold still triggers).
    // FusionCache uses TimeSpan.MaxValue; headless computes logical expiry as now.Add(Duration), so a literal
    // TimeSpan.MaxValue would overflow DateTime — a ~1000-year duration exercises the same "huge duration + eager
    // threshold" path without overflow.
    [Fact]
    public async Task should_eager_refresh_with_very_large_duration()
    {
        // given — a fresh entry past its eager point, carrying a near-infinite duration
        var key = Faker.Random.AlphaNumeric(8);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var hugeDuration = TimeSpan.FromDays(365_000);
        _store.SetEntry(key, "old", now.AddDays(180_000), now.AddDays(180_000), eagerRefreshAt: now.AddSeconds(-1));
        var coordinator = _CreateCoordinator();
        var backgroundFinished = _WaitForBackgroundFinished(coordinator);
        var options = _CreateOptions(duration: hugeDuration, eagerRefreshThreshold: 0.5f);

        // when
        var result = await coordinator.GetOrAddAsync(_store, key, _FactoryReturns("fresh"), options, AbortToken);
        await backgroundFinished;

        // then — the eager refresh ran and re-stamped the entry; the huge duration's eager point is honored
        result.Value.Should().Be("old");
        result.IsStale.Should().BeFalse();
        var entry = _store.GetEntry(key)!;
        entry.Value.Should().Be("fresh");
        entry.LogicalExpiresAt.Should().Be(now.Add(hugeDuration));
        entry.EagerRefreshAt.Should().Be(now.AddTicks(hugeDuration.Ticks / 2));
    }

    // FusionCache: CanHandleEagerRefreshWithTagsAsync.
    // Tags supplied by the eager-refresh factory persist on the rewritten entry. (FusionCache then removes via
    // RemoveByTagAsync; that operation lives on ICache, not the FactoryCacheCoordinator store primitive, so this
    // port asserts tag persistence only — the removal itself is covered at the ICache/hybrid level, not here.)
    [Fact]
    public async Task should_persist_eager_refresh_factory_tags_on_rewritten_entry()
    {
        // given — a fresh entry past its eager point with an existing tag
        var key = Faker.Random.AlphaNumeric(8);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        _store.SetEntry(
            key,
            "old",
            now.AddMinutes(5),
            now.AddMinutes(5),
            eagerRefreshAt: now.AddSeconds(-1),
            tags: ["a", "b"]
        );
        var coordinator = _CreateCoordinator();
        var backgroundFinished = _WaitForBackgroundFinished(coordinator);
        var options = _CreateOptions(duration: TimeSpan.FromMinutes(10), eagerRefreshThreshold: 0.5f);

        // when — the eager refresh factory replaces the tags
        var result = await coordinator.GetOrAddAsync<string>(
            _store,
            key,
            (context, _) =>
            {
                context.Tags = ["c", "d"];
                return ValueTask.FromResult(context.Modified("fresh"));
            },
            options,
            AbortToken
        );
        await backgroundFinished;

        // then — the rewritten entry carries the new tags
        result.Value.Should().Be("old");
        var entry = _store.GetEntry(key)!;
        entry.Value.Should().Be("fresh");
        entry.Tags.Should().BeEquivalentTo("c", "d");
    }

    // FusionCache: CanAccessCacheKeyInsideFactoryAsync.
    // The factory's ctx.Key carries the cache key the store sees. The coordinator does not apply a key prefix (that
    // is an ICache-layer concern), so ctx.Key equals the key passed to GetOrAddAsync verbatim.
    [Fact]
    public async Task should_expose_cache_key_to_factory_via_context()
    {
        // given
        var key = Faker.Random.AlphaNumeric(8);
        string? observedKey = null;

        // when
        await _CreateCoordinator()
            .GetOrAddAsync<string>(
                _store,
                key,
                (context, _) =>
                {
                    observedKey = context.Key;
                    return ValueTask.FromResult(context.Modified("fresh"));
                },
                _CreateOptions(),
                AbortToken
            );

        // then
        observedKey.Should().Be(key);
    }

    // #21 — _ObserveDiscardedSuccess: a factory that ignores its hard-timeout CancellationToken and completes
    // successfully AFTER the hard-timeout window fires. The coordinator must return the stale/miss result at
    // hard-timeout and must NOT write the discarded value to the store. EventId 16 must fire on the logger.
    [Fact]
    public async Task should_discard_late_factory_success_after_hard_timeout_and_log_event_id_16()
    {
        // given — stale entry as fail-safe reserve so the coordinator returns stale (not throws)
        var key = Faker.Random.AlphaNumeric(8);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        _store.SetEntry(key, "stale", now.AddSeconds(-1), now.AddMinutes(5));
        var logger = Substitute.For<ILogger<FactoryCacheCoordinator>>();
        logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);
        var coordinator = _CreateCoordinator(logger);
        var timeoutRegistered = _WaitForFactoryTimeoutRegistered(coordinator);
        var factoryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // Gate the factory: it ignores cancellation and only completes when we signal it
        var factoryGate = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var options = _CreateOptions(isFailSafeEnabled: true, factoryHardTimeout: TimeSpan.FromSeconds(1));

        async ValueTask<string?> ignoresCancellationFactory(CancellationToken cancellationToken)
        {
            factoryStarted.SetResult();
            // Deliberately ignore the cancellation token — factory keeps running past hard timeout
            return await factoryGate.Task.ConfigureAwait(false);
        }

        // when — advance past hard timeout, coordinator returns stale
        var resultTask = coordinator
            .GetOrAddAsync(_store, key, ignoresCancellationFactory, options, AbortToken)
            .AsTask();
        await factoryStarted.Task;
        await timeoutRegistered;
        _timeProvider.Advance(TimeSpan.FromSeconds(1)); // hard timeout fires
        var result = await resultTask;

        // then — stale is returned
        result.Value.Should().Be("stale");
        result.IsStale.Should().BeTrue();
        // The coordinator may restamp the throttle window on hard-timeout with fail-safe (1 write allowed);
        // the important invariant is that the discarded factory's value is never written.
        var writesAtTimeout = _store.SetEntryCalls;

        // now let the factory complete successfully (it ignored cancellation)
        factoryGate.SetResult("discarded-fresh");
        // poll briefly for the ContinueWith continuation to run (OnlyOnRanToCompletion on TaskScheduler.Default)
        var observed = false;
        for (var attempt = 0; attempt < 200 && !observed; attempt++)
        {
            observed = logger
                .ReceivedCalls()
                .Any(call =>
                    string.Equals(call.GetMethodInfo().Name, nameof(ILogger.Log), StringComparison.Ordinal)
                    && call.GetArguments()[1] is EventId { Id: 16, Name: "CacheFactoryDiscardedSuccess" }
                );

            if (!observed)
            {
                await Task.Delay(25, AbortToken);
            }
        }

        // EventId 16 must fire (DiscardedSuccess observer)
        observed
            .Should()
            .BeTrue("LogCacheFactoryDiscardedSuccess (EventId 16) must fire when the abandoned factory later resolves");
        // No additional write must have occurred after the factory's discarded success — the discard path
        // must not call SetEntryAsync. Any writes before this point are the coordinator's own restamp.
        _store
            .SetEntryCalls.Should()
            .Be(writesAtTimeout, "the discarded factory result must never be written to the store");
        // The store's value for this key must NOT be "discarded-fresh"
        _store
            .GetEntry(key)!
            .Value.Should()
            .NotBe("discarded-fresh", "discarded factory result must never be written to the store");
    }

    // #30 — _TryRestampStaleWithCeilingAsync ceiling-fires-before-restamp-write-completes branch.
    // The restamp store write is gated behind a TaskCompletionSource; BackgroundFactoryCeiling is set shorter
    // than the gate, so the ceiling fires first. Assert: coordinator returns control, the restamp is cancelled,
    // and the stale entry's physical TTL is unchanged in the underlying store.
    //
    // NOTE: This test uses TimeProvider.System (real time) with a short ceiling (100ms) so the ceiling task
    // is registered and fires by real time, avoiding FakeTimeProvider ordering races. The gated restamp write
    // blocks indefinitely until the ceiling cancels it.
    [Fact]
    public async Task should_cancel_restamp_when_ceiling_fires_before_restamp_write_completes()
    {
        // given — stale entry; use a separate real-time store so FakeTimeProvider ordering is not an issue
        var key = Faker.Random.AlphaNumeric(8);
        var realStore = new FakeFactoryCacheStore();
        var now = TimeProvider.System.GetUtcNow().UtcDateTime;
        var stalePhysicalExpiry = now.AddMinutes(5);

        // restampGate blocks the store write indefinitely so the ceiling can win the race
        var restampGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gatedStore = new GatedRestampStore(realStore, restampGate.Task);
        realStore.SetEntry(key, "stale", now.AddSeconds(-1), stalePhysicalExpiry);

        // Use TimeProvider.System so Task.Delay fires by wall time (avoids FakeTimeProvider advance races).
        // Disposed manually after backgroundFinished to avoid CA2025 (task completes while Dispose is in flight).
        var coordinator = new FactoryCacheCoordinator(
            TimeProvider.System,
            NullLogger<FactoryCacheCoordinator>.Instance
        );
        var backgroundFinished = _WaitForBackgroundFinished(coordinator);
        var factoryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var factoryGate = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);

        // ceiling = 150ms (real time); factory soft-times out at 50ms and then fails in background
        var options = new CacheEntryOptions
        {
            Duration = TimeSpan.FromMinutes(5),
            IsFailSafeEnabled = true,
            FailSafeMaxDuration = TimeSpan.FromMinutes(10),
            FailSafeThrottleDuration = TimeSpan.FromSeconds(30),
            FactorySoftTimeout = TimeSpan.FromMilliseconds(50),
            FactoryHardTimeout = TimeSpan.FromSeconds(10),
            BackgroundFactoryCeiling = TimeSpan.FromMilliseconds(150),
            LockTimeout = Timeout.InfiniteTimeSpan,
        };

        // Factory waits for its gate, then fails — background completion follows the failure path
        // (not the ceiling-abandoned path) which calls _TryRestampStaleWithCeilingAsync
        async ValueTask<string?> failingFactory(CancellationToken cancellationToken)
        {
            factoryStarted.SetResult();
            await factoryGate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException("background factory failed");
        }

        // when — soft timeout → stale returned, background completion starts
        var first = coordinator.GetOrAddAsync(gatedStore, key, failingFactory, options, AbortToken).AsTask();
        await factoryStarted.Task;
        // Wait for soft timeout to fire by real time (50ms ceiling)
        var staleResult = await first;
        staleResult.IsStale.Should().BeTrue();

        // Release the factory gate so it fails → background catches the exception, calls _TryRestampStaleWithCeilingAsync.
        // The restamp write blocks on restampGate indefinitely.
        // The 150ms ceiling inside _TryRestampStaleWithCeilingAsync will fire by real time and cancel the restamp.
        factoryGate.SetException(new InvalidOperationException("background factory failed"));

        // Wait for the background operation to fully finish (ceiling cancels restamp → background unwinds)
        await backgroundFinished.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
        coordinator.Dispose(); // dispose after background task completes to avoid CA2025

        // then — restamp was attempted but the ceiling cancelled it before the gate opened
        gatedStore.RestampAttempts.Should().Be(1, "restamp was attempted once");
        var storeEntry = realStore.GetEntry(key);
        storeEntry.Should().NotBeNull();
        // The restamp write was cancelled before mutating the store; physical TTL must be unchanged
        storeEntry!
            .PhysicalExpiresAt.Should()
            .Be(stalePhysicalExpiry, "ceiling must cancel the restamp before it can mutate the store's physical TTL");
    }

    // Wraps FakeFactoryCacheStore and delays SetEntryAsync (restamp writes) behind a Task gate so the
    // ceiling-fires-before-restamp branch in _TryRestampStaleWithCeilingAsync can be exercised.
    // RestampStarted fires when the restamp write begins (before the gate blocks), so the test can advance
    // the fake clock AFTER the restamp task is in-flight but BEFORE the ceiling Task.Delay is created.
    // Note: Task.Delay for the second ceiling is created by _TryRestampStaleWithCeilingAsync AFTER
    // restampTask starts, so we yield after RestampStarted to let that registration happen.
    private sealed class GatedRestampStore(FakeFactoryCacheStore inner, Task gate) : IFactoryCacheStore
    {
        private readonly TaskCompletionSource _restampStartedTcs = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        public int RestampAttempts { get; private set; }

        /// <summary>Completes when a restamp write has started (entered the gate-wait).</summary>
        public Task RestampStarted => _restampStartedTcs.Task;

        public ValueTask<CacheStoreEntry<T>> TryGetEntryAsync<T>(string key, CancellationToken cancellationToken) =>
            inner.TryGetEntryAsync<T>(key, cancellationToken);

        public ValueTask<bool> SetEntryAsync<T>(
            string key,
            in CacheStoreEntryWrite<T> entry,
            CancellationToken cancellationToken
        )
        {
            // Copy the 'in' struct before entering the async state machine (async methods cannot have 'in' params).
            var entryCopy = entry;
            return _SetEntryAsyncCore(key, entryCopy, cancellationToken);
        }

        private async ValueTask<bool> _SetEntryAsyncCore<T>(
            string key,
            CacheStoreEntryWrite<T> entry,
            CancellationToken cancellationToken
        )
        {
            if (entry.IsRestamp)
            {
                RestampAttempts++;
                _restampStartedTcs.TrySetResult(); // signal that the restamp is in-flight
                // Block until the ceiling cancels us via cancellationToken (or gate is released)
                await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            return await inner.SetEntryAsync(key, in entry, cancellationToken).ConfigureAwait(false);
        }

        public ValueTask TryRearmSlidingAsync(
            string key,
            TimeSpan slidingExpiration,
            DateTime physicalExpiresAt,
            DateTime now,
            CancellationToken cancellationToken
        ) => inner.TryRearmSlidingAsync(key, slidingExpiration, physicalExpiresAt, now, cancellationToken);
    }

    // Models an actor that bypasses the coordinator (e.g. a direct RemoveAsync on the provider) while a factory
    // is in flight. Kept here (not on the fake) because only these interleaving tests need it.
    private static void _RemoveEntryDirectly(FakeFactoryCacheStore store, string key) => store.RemoveEntry(key);

    private FactoryCacheCoordinator _CreateCoordinator() =>
        _CreateCoordinator(NullLogger<FactoryCacheCoordinator>.Instance);

    // Tracks every coordinator so it is disposed in teardown, regardless of how the test consumes it
    // (inline fluent calls cannot take a `using`). Coordinator.Dispose is idempotent, so tests that
    // dispose mid-run via `using var` are unaffected.
    private FactoryCacheCoordinator _CreateCoordinator(ILogger<FactoryCacheCoordinator> logger)
    {
        var coordinator = new FactoryCacheCoordinator(_timeProvider, logger);
        _coordinators.Add(coordinator);

        return coordinator;
    }

    private static Task _WaitForBackgroundFinished(FactoryCacheCoordinator coordinator)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        coordinator.BackgroundOperationFinished = () => tcs.TrySetResult();
        return tcs.Task;
    }

    private static Task _WaitForBackgroundCeilingRegistered(FactoryCacheCoordinator coordinator)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        coordinator.BackgroundCompletionCeilingTimerRegistered = () => tcs.TrySetResult();
        return tcs.Task;
    }

    private static Task _WaitForFactoryTimeoutRegistered(FactoryCacheCoordinator coordinator)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        coordinator.FactoryTimeoutTimerRegistered = () => tcs.TrySetResult();
        return tcs.Task;
    }

    private static CacheEntryOptions _CreateOptions(
        TimeSpan? duration = null,
        bool isFailSafeEnabled = false,
        TimeSpan? maxDuration = null,
        TimeSpan? throttleDuration = null,
        TimeSpan? factorySoftTimeout = null,
        TimeSpan? factoryHardTimeout = null,
        TimeSpan? backgroundFactoryCeiling = null,
        TimeSpan? lockTimeout = null,
        TimeSpan? slidingExpiration = null,
        float? eagerRefreshThreshold = null,
        bool useDistributedFactoryLock = false
    ) =>
        new()
        {
            Duration = duration ?? TimeSpan.FromSeconds(5),
            SlidingExpiration = slidingExpiration,
            EagerRefreshThreshold = eagerRefreshThreshold,
            IsFailSafeEnabled = isFailSafeEnabled,
            FailSafeMaxDuration = maxDuration ?? TimeSpan.FromMinutes(1),
            FailSafeThrottleDuration = throttleDuration ?? TimeSpan.FromSeconds(10),
            FactorySoftTimeout = factorySoftTimeout ?? Timeout.InfiniteTimeSpan,
            FactoryHardTimeout = factoryHardTimeout ?? Timeout.InfiniteTimeSpan,
            BackgroundFactoryCeiling = backgroundFactoryCeiling ?? Timeout.InfiniteTimeSpan,
            LockTimeout = lockTimeout ?? Timeout.InfiniteTimeSpan,
            UseDistributedFactoryLock = useDistributedFactoryLock,
        };

    private static Func<CancellationToken, ValueTask<string?>> _FactoryReturns(string value) =>
        _ => ValueTask.FromResult<string?>(value);
}
