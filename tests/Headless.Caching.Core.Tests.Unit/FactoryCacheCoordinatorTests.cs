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
    public void should_default_factory_timeouts_to_infinite_and_background_ceiling_to_two_minutes()
    {
        // when
        var options = new CacheEntryOptions();

        // then
        options.FactorySoftTimeout.Should().Be(Timeout.InfiniteTimeSpan);
        options.FactoryHardTimeout.Should().Be(Timeout.InfiniteTimeSpan);
        options.BackgroundFactoryCeiling.Should().Be(TimeSpan.FromMinutes(2));
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
                .GetOrAddAsync<string>(_store, "soft-timeout-validation", _FactoryReturns("fresh"), options, AbortToken);

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
                .GetOrAddAsync<string>(_store, "hard-timeout-validation", _FactoryReturns("fresh"), options, AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-2)]
    public async Task should_throw_when_background_factory_ceiling_is_non_positive_or_infinite(int milliseconds)
    {
        // given
        var ceiling = milliseconds == -1 ? Timeout.InfiniteTimeSpan : TimeSpan.FromMilliseconds(milliseconds);
        var options = _CreateOptions(backgroundFactoryCeiling: ceiling);

        // when
        var act = async () =>
            await _CreateCoordinator()
                .GetOrAddAsync<string>(
                    _store,
                    "background-ceiling-validation",
                    _FactoryReturns("fresh"),
                    options,
                    AbortToken
                );

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
                .GetOrAddAsync<string>(_store, "timeout-order-validation", _FactoryReturns("fresh"), options, AbortToken);

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
            .GetOrAddAsync<string>(_store, Faker.Random.AlphaNumeric(8), _FactoryReturns("fresh"), options, AbortToken);

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
            .GetOrAddAsync<string>(_store, Faker.Random.AlphaNumeric(8), _FactoryReturns("fresh"), options, AbortToken);

        // then
        result.Value.Should().Be("fresh");
    }

    [Fact]
    public void should_create_cache_factory_timeout_exception_as_timeout_exception()
    {
        // given
        var key = Faker.Random.AlphaNumeric(8);

        // when
        var exception = new CacheFactoryTimeoutException(key, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(2));

        // then
        exception.Should().BeAssignableTo<TimeoutException>();
        exception.Key.Should().Be(key);
        exception.Elapsed.Should().Be(TimeSpan.FromSeconds(3));
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

    [Fact]
    public async Task should_return_stale_on_soft_timeout_and_complete_factory_in_background()
    {
        // given
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
        var options = _CreateOptions(
            isFailSafeEnabled: true,
            factorySoftTimeout: TimeSpan.FromSeconds(1),
            factoryHardTimeout: TimeSpan.FromSeconds(10)
        );

        async ValueTask<string?> Factory(CancellationToken cancellationToken)
        {
            factoryStarted.SetResult();
            return await factoryGate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        // when
        var resultTask = coordinator.GetOrAddAsync(_store, key, Factory, options, AbortToken).AsTask();
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
        coordinator.BackgroundCompletionFinished = () => backgroundFinished.TrySetResult();
        var timeoutRegistered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        coordinator.FactoryTimeoutTimerRegistered = () => timeoutRegistered.TrySetResult();
        var factoryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var factoryGate = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var factoryCalls = 0;
        var options = _CreateOptions(
            isFailSafeEnabled: true,
            factorySoftTimeout: TimeSpan.FromSeconds(1),
            factoryHardTimeout: TimeSpan.FromSeconds(10)
        );

        async ValueTask<string?> Factory(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref factoryCalls);
            factoryStarted.SetResult();
            return await factoryGate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        // when
        var first = coordinator.GetOrAddAsync(_store, key, Factory, options, AbortToken).AsTask();
        await factoryStarted.Task;
        await timeoutRegistered.Task;
        _timeProvider.Advance(TimeSpan.FromSeconds(1));
        (await first).IsStale.Should().BeTrue();

        var second = coordinator.GetOrAddAsync(_store, key, Factory, options, AbortToken).AsTask();
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
        coordinator.BackgroundCompletionFinished = () => backgroundFinished.TrySetResult();
        var timeoutRegistered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        coordinator.FactoryTimeoutTimerRegistered = () => timeoutRegistered.TrySetResult();
        var factoryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var factoryGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var factoryCalls = 0;
        var options = _CreateOptions(
            isFailSafeEnabled: true,
            throttleDuration: TimeSpan.FromSeconds(20),
            factorySoftTimeout: TimeSpan.FromSeconds(1),
            factoryHardTimeout: TimeSpan.FromSeconds(10)
        );

        async ValueTask<string?> Factory(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref factoryCalls);
            factoryStarted.SetResult();
            await factoryGate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException("background failed");
        }

        // when
        var first = coordinator.GetOrAddAsync(_store, key, Factory, options, AbortToken).AsTask();
        await factoryStarted.Task;
        await timeoutRegistered.Task;
        _timeProvider.Advance(TimeSpan.FromSeconds(1));
        (await first).IsStale.Should().BeTrue();
        factoryGate.SetResult();
        await backgroundFinished.Task;

        var throttled = await coordinator.GetOrAddAsync(_store, key, Factory, options, AbortToken);

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
        await act.Should().ThrowAsync<CacheFactoryTimeoutException>();
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

        async ValueTask<string?> Factory(CancellationToken cancellationToken)
        {
            factoryStarted.SetResult();
            return await factoryGate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        // when
        var resultTask = _CreateCoordinator().GetOrAddAsync(_store, key, Factory, options, AbortToken).AsTask();
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
            factoryHardTimeout: TimeSpan.FromSeconds(10)
        );
        using var cts = new CancellationTokenSource();

        async ValueTask<string?> Factory(CancellationToken cancellationToken)
        {
            factoryStarted.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, _timeProvider, cancellationToken).ConfigureAwait(false);
            return "fresh";
        }

        // when
        var resultTask = coordinator.GetOrAddAsync(_store, key, Factory, options, cts.Token).AsTask();
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
        coordinator.BackgroundCompletionFinished = () => backgroundFinished.TrySetResult();
        var timeoutRegistered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        coordinator.FactoryTimeoutTimerRegistered = () => timeoutRegistered.TrySetResult();
        var factoryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var factoryGate = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var options = _CreateOptions(
            isFailSafeEnabled: true,
            factorySoftTimeout: TimeSpan.FromSeconds(1),
            factoryHardTimeout: TimeSpan.FromSeconds(10)
        );
        using var cts = new CancellationTokenSource();

        async ValueTask<string?> Factory(CancellationToken cancellationToken)
        {
            factoryStarted.SetResult();
            return await factoryGate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        // when
        var resultTask = coordinator.GetOrAddAsync(_store, key, Factory, options, cts.Token).AsTask();
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
        var abandonedFactoryGate = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var options = _CreateOptions(
            duration: TimeSpan.FromSeconds(5),
            isFailSafeEnabled: true,
            throttleDuration: TimeSpan.FromSeconds(2),
            factorySoftTimeout: TimeSpan.FromSeconds(1),
            factoryHardTimeout: TimeSpan.FromSeconds(20),
            backgroundFactoryCeiling: TimeSpan.FromSeconds(3)
        );

        async ValueTask<string?> FactoryA(CancellationToken cancellationToken)
        {
            factoryStarted.SetResult();
            return await abandonedFactoryGate.Task.ConfigureAwait(false);
        }

        // when
        var first = coordinator.GetOrAddAsync(_store, key, FactoryA, options, AbortToken).AsTask();
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
            factoryHardTimeout: TimeSpan.FromSeconds(10)
        );

        async ValueTask<string?> FirstFactory(CancellationToken cancellationToken)
        {
            firstFactoryStarted.SetResult();
            return await firstFactoryGate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        ValueTask<string?> SecondFactory(CancellationToken _)
        {
            secondFactoryCalls++;
            return ValueTask.FromResult<string?>("second");
        }

        // when
        var first = coordinator.GetOrAddAsync(_store, key, FirstFactory, options, AbortToken).AsTask();
        await firstFactoryStarted.Task;
        await timeoutRegistered;
        var second = coordinator.GetOrAddAsync(_store, key, SecondFactory, options, AbortToken).AsTask();
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
            factoryHardTimeout: TimeSpan.FromSeconds(10)
        );
        var outerOptions = _CreateOptions(
            isFailSafeEnabled: true,
            factorySoftTimeout: TimeSpan.FromSeconds(10),
            factoryHardTimeout: TimeSpan.FromSeconds(20)
        );

        async ValueTask<string?> OuterFactory(CancellationToken cancellationToken)
        {
            var inner = coordinator.GetOrAddAsync(_store, key, _FactoryReturns("inner"), innerOptions, cancellationToken).AsTask();
            innerStarted.SetResult();
            _timeProvider.Advance(TimeSpan.FromSeconds(1));
            return (await inner).Value;
        }

        // when
        var result = await coordinator.GetOrAddAsync(_store, key, OuterFactory, outerOptions, AbortToken);

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

    private static Task _WaitForBackgroundFinished(FactoryCacheCoordinator coordinator)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        coordinator.BackgroundCompletionFinished = () => tcs.TrySetResult();
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
        TimeSpan? backgroundFactoryCeiling = null
    ) =>
        new()
        {
            Duration = duration ?? TimeSpan.FromSeconds(5),
            IsFailSafeEnabled = isFailSafeEnabled,
            FailSafeMaxDuration = maxDuration ?? TimeSpan.FromMinutes(1),
            FailSafeThrottleDuration = throttleDuration ?? TimeSpan.FromSeconds(10),
            FactorySoftTimeout = factorySoftTimeout ?? Timeout.InfiniteTimeSpan,
            FactoryHardTimeout = factoryHardTimeout ?? Timeout.InfiniteTimeSpan,
            BackgroundFactoryCeiling = backgroundFactoryCeiling ?? TimeSpan.FromMinutes(2),
        };

    private static Func<CancellationToken, ValueTask<string?>> _FactoryReturns(string value) =>
        _ => ValueTask.FromResult<string?>(value);
}
