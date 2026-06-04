// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

public sealed class FactoryCacheCoordinatorTests : TestBase
{
    private readonly FakeTimeProvider _timeProvider = new();
    private readonly FakeFactoryCacheStore _store = new();

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

    private FactoryCacheCoordinator _CreateCoordinator() =>
        new(_timeProvider, NullLogger<FactoryCacheCoordinator>.Instance);

    private static CacheEntryOptions _CreateOptions(
        TimeSpan? duration = null,
        bool isFailSafeEnabled = false,
        TimeSpan? maxDuration = null,
        TimeSpan? throttleDuration = null
    ) =>
        new()
        {
            Duration = duration ?? TimeSpan.FromSeconds(5),
            IsFailSafeEnabled = isFailSafeEnabled,
            FailSafeMaxDuration = maxDuration ?? TimeSpan.FromMinutes(1),
            FailSafeThrottleDuration = throttleDuration ?? TimeSpan.FromSeconds(10),
        };
}
