// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Testing.Tests;

namespace Tests;

public abstract class CacheConformanceTestsBase : TestBase
{
    protected abstract ICache CreateCache(string keyPrefix);

    protected virtual ValueTask ResetAsync() => ValueTask.CompletedTask;

    protected virtual ValueTask AdvancePastExpirationAsync(TimeSpan expiration) => ValueTask.CompletedTask;

    protected virtual ValueTask AdvanceAsync(TimeSpan duration) => AdvancePastExpirationAsync(duration);

    public virtual async Task should_round_trip_object_and_string_values()
    {
        await ResetAsync();
        var cache = CreateCache(Faker.Random.AlphaNumeric(8));
        var objectKey = Faker.Random.AlphaNumeric(10);
        var stringKey = Faker.Random.AlphaNumeric(10);
        var value = new CacheConformanceObject(Faker.Random.Guid(), Faker.Name.FullName());
        var stringValue = Faker.Lorem.Sentence();

        await cache.UpsertAsync(objectKey, value, TimeSpan.FromMinutes(5), AbortToken);
        await cache.UpsertAsync(stringKey, stringValue, TimeSpan.FromMinutes(5), AbortToken);

        var cachedObject = await cache.GetAsync<CacheConformanceObject>(objectKey, AbortToken);
        var cachedString = await cache.GetAsync<string>(stringKey, AbortToken);

        cachedObject.HasValue.Should().BeTrue();
        cachedObject.Value.Should().Be(value);
        cachedString.HasValue.Should().BeTrue();
        cachedString.Value.Should().Be(stringValue);
    }

    public virtual async Task should_round_trip_null_and_null_sentinel_string()
    {
        await ResetAsync();
        var cache = CreateCache(Faker.Random.AlphaNumeric(8));
        var nullKey = Faker.Random.AlphaNumeric(10);
        var sentinelKey = Faker.Random.AlphaNumeric(10);

        await cache.UpsertAsync<string?>(nullKey, null, TimeSpan.FromMinutes(5), AbortToken);
        await cache.UpsertAsync(sentinelKey, "@@NULL", TimeSpan.FromMinutes(5), AbortToken);

        var nullValue = await cache.GetAsync<string>(nullKey, AbortToken);
        var sentinelValue = await cache.GetAsync<string>(sentinelKey, AbortToken);

        nullValue.HasValue.Should().BeTrue();
        nullValue.IsNull.Should().BeTrue();
        sentinelValue.HasValue.Should().BeTrue();
        sentinelValue.Value.Should().Be("@@NULL");
    }

    public virtual async Task should_round_trip_empty_string_value()
    {
        await ResetAsync();
        var cache = CreateCache(Faker.Random.AlphaNumeric(8));
        var key = Faker.Random.AlphaNumeric(10);

        await cache.UpsertAsync(key, "", TimeSpan.FromMinutes(5), AbortToken);

        var cached = await cache.GetAsync<string>(key, AbortToken);

        cached.HasValue.Should().BeTrue();
        cached.IsNull.Should().BeFalse();
        cached.Value.Should().Be("");
    }

    public virtual async Task should_expire_values_after_duration()
    {
        await ResetAsync();
        var cache = CreateCache(Faker.Random.AlphaNumeric(8));
        var key = Faker.Random.AlphaNumeric(10);
        var expiration = TimeSpan.FromMilliseconds(250);

        await cache.UpsertAsync(key, "value", expiration, AbortToken);

        var beforeExpiry = await cache.GetAsync<string>(key, AbortToken);
        await AdvancePastExpirationAsync(expiration);
        var afterExpiry = await cache.GetAsync<string>(key, AbortToken);

        beforeExpiry.HasValue.Should().BeTrue();
        afterExpiry.HasValue.Should().BeFalse();
    }

    public virtual async Task should_get_all_values_including_null_members()
    {
        await ResetAsync();
        var cache = CreateCache(Faker.Random.AlphaNumeric(8));
        var firstKey = Faker.Random.AlphaNumeric(10);
        var nullKey = Faker.Random.AlphaNumeric(10);
        var values = new Dictionary<string, string?>(StringComparer.Ordinal) { [firstKey] = "value", [nullKey] = null };

        var written = await cache.UpsertAllAsync(values, TimeSpan.FromMinutes(5), AbortToken);
        var cached = await cache.GetAllAsync<string>(values.Keys, AbortToken);

        written.Should().Be(values.Count);
        cached[firstKey].Value.Should().Be("value");
        cached[nullKey].HasValue.Should().BeTrue();
        cached[nullKey].IsNull.Should().BeTrue();
    }

    public virtual async Task should_increment_and_read_back_number()
    {
        await ResetAsync();
        var cache = CreateCache(Faker.Random.AlphaNumeric(8));
        var key = Faker.Random.AlphaNumeric(10);

        await cache.IncrementAsync(key, 2L, TimeSpan.FromMinutes(5), AbortToken);
        var result = await cache.IncrementAsync(key, 3L, TimeSpan.FromMinutes(5), AbortToken);
        var cached = await cache.GetAsync<long>(key, AbortToken);

        result.Should().Be(5);
        cached.HasValue.Should().BeTrue();
        cached.Value.Should().Be(5);
    }

    public virtual async Task should_compare_and_swap_on_matching_values_only()
    {
        await ResetAsync();
        var cache = CreateCache(Faker.Random.AlphaNumeric(8));
        var replaceKey = Faker.Random.AlphaNumeric(10);
        var removeKey = Faker.Random.AlphaNumeric(10);

        await cache.UpsertAsync(replaceKey, "first", TimeSpan.FromMinutes(5), AbortToken);
        await cache.UpsertAsync(removeKey, "remove", TimeSpan.FromMinutes(5), AbortToken);

        var replaceMiss = await cache.TryReplaceIfEqualAsync(
            replaceKey,
            "wrong",
            "second",
            TimeSpan.FromMinutes(5),
            AbortToken
        );
        var replaceHit = await cache.TryReplaceIfEqualAsync(
            replaceKey,
            "first",
            "second",
            TimeSpan.FromMinutes(5),
            AbortToken
        );
        var removeMiss = await cache.RemoveIfEqualAsync(removeKey, "wrong", AbortToken);
        var removeHit = await cache.RemoveIfEqualAsync(removeKey, "remove", AbortToken);

        var replaced = await cache.GetAsync<string>(replaceKey, AbortToken);
        var removed = await cache.GetAsync<string>(removeKey, AbortToken);

        replaceMiss.Should().BeFalse();
        replaceHit.Should().BeTrue();
        replaced.Value.Should().Be("second");
        removeMiss.Should().BeFalse();
        removeHit.Should().BeTrue();
        removed.HasValue.Should().BeFalse();
    }

    public virtual async Task should_insert_only_when_missing_and_replace_only_when_present()
    {
        await ResetAsync();
        var cache = CreateCache(Faker.Random.AlphaNumeric(8));
        var key = Faker.Random.AlphaNumeric(10);
        var missingKey = Faker.Random.AlphaNumeric(10);

        var inserted = await cache.TryInsertAsync(key, "first", TimeSpan.FromMinutes(5), AbortToken);
        var duplicateInsert = await cache.TryInsertAsync(key, "second", TimeSpan.FromMinutes(5), AbortToken);
        var replaceMissing = await cache.TryReplaceAsync(missingKey, "missing", TimeSpan.FromMinutes(5), AbortToken);
        var replaceExisting = await cache.TryReplaceAsync(key, "second", TimeSpan.FromMinutes(5), AbortToken);
        var cached = await cache.GetAsync<string>(key, AbortToken);

        inserted.Should().BeTrue();
        duplicateInsert.Should().BeFalse();
        replaceMissing.Should().BeFalse();
        replaceExisting.Should().BeTrue();
        cached.Value.Should().Be("second");
    }

    public virtual async Task should_serve_stale_when_failsafe_factory_throws_within_window()
    {
        await ResetAsync();
        var cache = CreateCache(Faker.Random.AlphaNumeric(8));
        var key = Faker.Random.AlphaNumeric(10);
        var options = _CreateFailSafeOptions();

        await cache.GetOrAddAsync(key, _ => ValueTask.FromResult<string?>("value"), options, AbortToken);
        await AdvanceAsync(options.Duration + TimeSpan.FromMilliseconds(50));

        var result = await cache.GetOrAddAsync<string>(
            key,
            _ => throw new InvalidOperationException("downstream unavailable"),
            options,
            AbortToken
        );

        result.HasValue.Should().BeTrue();
        result.Value.Should().Be("value");
        result.IsStale.Should().BeTrue();
    }

    public virtual async Task should_propagate_factory_exception_after_failsafe_physical_window()
    {
        await ResetAsync();
        var cache = CreateCache(Faker.Random.AlphaNumeric(8));
        var key = Faker.Random.AlphaNumeric(10);
        var options = _CreateFailSafeOptions();

        await cache.GetOrAddAsync(key, _ => ValueTask.FromResult<string?>("value"), options, AbortToken);
        await AdvanceAsync(options.FailSafeMaxDuration + TimeSpan.FromMilliseconds(50));

        var act = async () =>
            await cache.GetOrAddAsync<string>(
                key,
                _ => throw new InvalidOperationException("downstream unavailable"),
                options,
                AbortToken
            );

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    public virtual async Task should_propagate_factory_exception_when_failsafe_cache_is_cold()
    {
        await ResetAsync();
        var cache = CreateCache(Faker.Random.AlphaNumeric(8));
        var key = Faker.Random.AlphaNumeric(10);

        var act = async () =>
            await cache.GetOrAddAsync<string>(
                key,
                _ => throw new InvalidOperationException("downstream unavailable"),
                _CreateFailSafeOptions(),
                AbortToken
            );

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    public virtual async Task should_throttle_failsafe_factory_retries()
    {
        await ResetAsync();
        var cache = CreateCache(Faker.Random.AlphaNumeric(8));
        var key = Faker.Random.AlphaNumeric(10);
        var options = _CreateFailSafeOptions(throttleDuration: TimeSpan.FromMilliseconds(250));
        var factoryCalls = 0;

        await cache.GetOrAddAsync(key, _ => ValueTask.FromResult<string?>("value"), options, AbortToken);
        await AdvanceAsync(options.Duration + TimeSpan.FromMilliseconds(50));

        var activated = await cache.GetOrAddAsync<string>(
            key,
            _ =>
            {
                factoryCalls++;
                throw new InvalidOperationException("downstream unavailable");
            },
            options,
            AbortToken
        );

        var throttled = await cache.GetOrAddAsync<string>(
            key,
            _ =>
            {
                factoryCalls++;
                return ValueTask.FromResult<string?>("new");
            },
            options,
            AbortToken
        );

        await AdvanceAsync(options.FailSafeThrottleDuration + TimeSpan.FromMilliseconds(50));
        var refreshed = await cache.GetOrAddAsync<string>(
            key,
            _ =>
            {
                factoryCalls++;
                return ValueTask.FromResult<string?>("new");
            },
            options,
            AbortToken
        );

        activated.IsStale.Should().BeTrue();
        throttled.Value.Should().Be("value");
        throttled.IsStale.Should().BeFalse();
        refreshed.Value.Should().Be("new");
        factoryCalls.Should().Be(2);
    }

    public virtual async Task should_not_serve_stale_when_failsafe_disabled_by_default()
    {
        await ResetAsync();
        var cache = CreateCache(Faker.Random.AlphaNumeric(8));
        var key = Faker.Random.AlphaNumeric(10);
        var duration = TimeSpan.FromMilliseconds(100);

        await cache.GetOrAddAsync(key, _ => ValueTask.FromResult<string?>("value"), duration, AbortToken);
        await AdvanceAsync(duration + TimeSpan.FromMilliseconds(50));

        var act = async () =>
            await cache.GetOrAddAsync<string>(
                key,
                _ => throw new InvalidOperationException("downstream unavailable"),
                duration,
                AbortToken
            );

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    public virtual async Task should_not_serve_stale_when_caller_cancels()
    {
        await ResetAsync();
        var cache = CreateCache(Faker.Random.AlphaNumeric(8));
        var key = Faker.Random.AlphaNumeric(10);
        var options = _CreateFailSafeOptions();
        using var cts = new CancellationTokenSource();

        await cache.GetOrAddAsync(key, _ => ValueTask.FromResult<string?>("value"), options, AbortToken);
        await AdvanceAsync(options.Duration + TimeSpan.FromMilliseconds(50));
        await cts.CancelAsync();

        var act = async () =>
            await cache.GetOrAddAsync<string>(
                key,
                _ => throw new OperationCanceledException(cts.Token),
                options,
                cts.Token
            );

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    public virtual async Task should_return_stale_on_soft_timeout_and_refresh_in_background()
    {
        await ResetAsync();
        var cache = CreateCache(Faker.Random.AlphaNumeric(8));
        var key = Faker.Random.AlphaNumeric(10);
        var options = _CreateTimeoutOptions();
        var factoryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var factoryGate = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);

        await cache.GetOrAddAsync(key, _ => ValueTask.FromResult<string?>("stale"), options, AbortToken);
        await AdvanceAsync(options.Duration + TimeSpan.FromMilliseconds(50));

        async ValueTask<string?> Factory(CancellationToken cancellationToken)
        {
            factoryStarted.SetResult();
            return await factoryGate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        var timeoutTask = cache.GetOrAddAsync(key, Factory, options, AbortToken).AsTask();
        await factoryStarted.Task;
        await TriggerTimeoutAsync(options.FactorySoftTimeout, timeoutTask);

        var timedOut = await timeoutTask;
        factoryGate.SetResult("fresh");

        await WaitUntilAsync(async () =>
        {
            var cached = await cache.GetAsync<string>(key, AbortToken);
            return cached.HasValue && cached.Value == "fresh";
        });

        timedOut.Value.Should().Be("stale");
        timedOut.IsStale.Should().BeTrue();
    }

    public virtual async Task should_throw_cache_factory_timeout_when_hard_timeout_fires_without_fallback()
    {
        await ResetAsync();
        var cache = CreateCache(Faker.Random.AlphaNumeric(8));
        var key = Faker.Random.AlphaNumeric(10);
        var options = _CreateTimeoutOptions(isFailSafeEnabled: false);
        var factoryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async ValueTask<string?> Factory(CancellationToken cancellationToken)
        {
            factoryStarted.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            return "fresh";
        }

        var timeoutTask = cache.GetOrAddAsync(key, Factory, options, AbortToken).AsTask();
        await factoryStarted.Task;
        await TriggerTimeoutAsync(options.FactoryHardTimeout, timeoutTask);
        var act = async () => await timeoutTask;

        await act.Should().ThrowAsync<CacheFactoryTimeoutException>();
    }

    public virtual async Task should_serve_stale_when_hard_timeout_fires_with_fallback()
    {
        await ResetAsync();
        var cache = CreateCache(Faker.Random.AlphaNumeric(8));
        var key = Faker.Random.AlphaNumeric(10);
        var options = _CreateTimeoutOptions(factorySoftTimeout: Timeout.InfiniteTimeSpan);
        var factoryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await cache.GetOrAddAsync(key, _ => ValueTask.FromResult<string?>("stale"), options, AbortToken);
        await AdvanceAsync(options.Duration + TimeSpan.FromMilliseconds(50));

        async ValueTask<string?> Factory(CancellationToken cancellationToken)
        {
            factoryStarted.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            return "fresh";
        }

        var timeoutTask = cache.GetOrAddAsync(key, Factory, options, AbortToken).AsTask();
        await factoryStarted.Task;
        await TriggerTimeoutAsync(options.FactoryHardTimeout, timeoutTask);
        var result = await timeoutTask;

        result.Value.Should().Be("stale");
        result.IsStale.Should().BeTrue();
    }

    public virtual async Task should_return_stale_to_waiter_when_soft_timeout_elapses_acquiring_lock()
    {
        await ResetAsync();
        var cache = CreateCache(Faker.Random.AlphaNumeric(8));
        var key = Faker.Random.AlphaNumeric(10);
        var options = _CreateTimeoutOptions();
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstGate = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondFactoryCalls = 0;

        await cache.GetOrAddAsync(key, _ => ValueTask.FromResult<string?>("stale"), options, AbortToken);
        await AdvanceAsync(options.Duration + TimeSpan.FromMilliseconds(50));

        async ValueTask<string?> FirstFactory(CancellationToken cancellationToken)
        {
            firstStarted.SetResult();
            return await firstGate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        ValueTask<string?> SecondFactory(CancellationToken _)
        {
            secondFactoryCalls++;
            return ValueTask.FromResult<string?>("second");
        }

        var first = cache.GetOrAddAsync(key, FirstFactory, options, AbortToken).AsTask();
        await firstStarted.Task;
        await Task.Yield();
        var second = cache.GetOrAddAsync(key, SecondFactory, options, AbortToken).AsTask();
        await TriggerTimeoutAsync(options.FactorySoftTimeout, second);
        var secondResult = await second;
        firstGate.SetResult("fresh");
        await first;

        secondResult.Value.Should().Be("stale");
        secondResult.IsStale.Should().BeTrue();
        secondFactoryCalls.Should().Be(0);
    }

    public virtual async Task should_eager_refresh_before_expiration()
    {
        await ResetAsync();
        var cache = CreateCache(Faker.Random.AlphaNumeric(8));
        var key = Faker.Random.AlphaNumeric(10);
        var options = _CreateEagerOptions();

        await cache.GetOrAddAsync(key, _ => ValueTask.FromResult<string?>("v1"), options, AbortToken);
        // Past the eager point (50% of 400ms) but well before logical expiration.
        await AdvanceAsync(TimeSpan.FromMilliseconds(250));

        var hit = await cache.GetOrAddAsync(key, _ => ValueTask.FromResult<string?>("v2"), options, AbortToken);

        hit.Value.Should().Be("v1");
        hit.IsStale.Should().BeFalse();

        await WaitUntilAsync(async () =>
        {
            var cached = await cache.GetAsync<string>(key, AbortToken);
            return cached.HasValue && cached.Value == "v2";
        });
    }

    public virtual async Task should_not_stampede_eager_refresh_across_concurrent_readers()
    {
        await ResetAsync();
        var cache = CreateCache(Faker.Random.AlphaNumeric(8));
        var key = Faker.Random.AlphaNumeric(10);
        var options = _CreateEagerOptions();
        var factoryCalls = 0;
        var factoryGate = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);

        await cache.GetOrAddAsync(key, _ => ValueTask.FromResult<string?>("v1"), options, AbortToken);
        await AdvanceAsync(TimeSpan.FromMilliseconds(250));

        async ValueTask<string?> Factory(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref factoryCalls);
            return await factoryGate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        var results = await Task.WhenAll(
            Enumerable.Range(0, 8).Select(_ => cache.GetOrAddAsync(key, Factory, options, AbortToken).AsTask())
        );

        results.Should().AllSatisfy(result => result.Value.Should().Be("v1"));

        factoryGate.SetResult("v2");

        await WaitUntilAsync(async () =>
        {
            var cached = await cache.GetAsync<string>(key, AbortToken);
            return cached.HasValue && cached.Value == "v2";
        });

        factoryCalls.Should().Be(1);
    }

    public virtual async Task should_keep_sliding_entry_alive_when_read_within_idle_window()
    {
        await ResetAsync();
        var cache = CreateCache(Faker.Random.AlphaNumeric(8));
        var key = Faker.Random.AlphaNumeric(10);
        var options = _CreateSlidingOptions();

        await cache.GetOrAddAsync(key, _ => ValueTask.FromResult<string?>("value"), options, AbortToken);
        await AdvanceAsync(options.SlidingExpiration!.Value - TimeSpan.FromMilliseconds(25));

        var firstRead = await cache.GetAsync<string>(key, AbortToken);

        await AdvanceAsync(options.SlidingExpiration.Value - TimeSpan.FromMilliseconds(25));
        var secondRead = await cache.GetAsync<string>(key, AbortToken);

        await AdvanceAsync(options.SlidingExpiration.Value + TimeSpan.FromMilliseconds(75));
        var idleRead = await cache.GetAsync<string>(key, AbortToken);

        firstRead.Value.Should().Be("value");
        secondRead.Value.Should().Be("value");
        idleRead.HasValue.Should().BeFalse();
    }

    public virtual async Task should_expire_sliding_entry_at_absolute_duration_cap()
    {
        await ResetAsync();
        var cache = CreateCache(Faker.Random.AlphaNumeric(8));
        var key = Faker.Random.AlphaNumeric(10);
        var options = _CreateSlidingOptions(
            duration: TimeSpan.FromMilliseconds(450),
            sliding: TimeSpan.FromMilliseconds(150)
        );

        await cache.GetOrAddAsync(key, _ => ValueTask.FromResult<string?>("value"), options, AbortToken);

        for (var i = 0; i < 3; i++)
        {
            await AdvanceAsync(TimeSpan.FromMilliseconds(100));
            (await cache.GetAsync<string>(key, AbortToken)).HasValue.Should().BeTrue();
        }

        await AdvanceAsync(TimeSpan.FromMilliseconds(180));
        var capped = await cache.GetAsync<string>(key, AbortToken);

        capped.HasValue.Should().BeFalse();
    }

    public virtual async Task should_not_rearm_sliding_entry_when_metadata_is_read()
    {
        await ResetAsync();
        var cache = CreateCache(Faker.Random.AlphaNumeric(8));
        var key = Faker.Random.AlphaNumeric(10);
        var options = _CreateSlidingOptions(sliding: TimeSpan.FromMilliseconds(150));

        await cache.GetOrAddAsync(key, _ => ValueTask.FromResult<string?>("value"), options, AbortToken);
        await AdvanceAsync(TimeSpan.FromMilliseconds(100));

        (await cache.ExistsAsync(key, AbortToken)).Should().BeTrue();

        await AdvanceAsync(TimeSpan.FromMilliseconds(90));
        var expired = await cache.GetAsync<string>(key, AbortToken);

        expired.HasValue.Should().BeFalse();
    }

    public virtual async Task should_not_rearm_non_sliding_entry()
    {
        await ResetAsync();
        var cache = CreateCache(Faker.Random.AlphaNumeric(8));
        var key = Faker.Random.AlphaNumeric(10);
        var options = new CacheEntryOptions { Duration = TimeSpan.FromMilliseconds(180) };

        await cache.GetOrAddAsync(key, _ => ValueTask.FromResult<string?>("value"), options, AbortToken);
        await AdvanceAsync(TimeSpan.FromMilliseconds(120));

        (await cache.GetAsync<string>(key, AbortToken)).HasValue.Should().BeTrue();

        await AdvanceAsync(TimeSpan.FromMilliseconds(100));
        var expired = await cache.GetAsync<string>(key, AbortToken);

        expired.HasValue.Should().BeFalse();
    }

    public virtual async Task should_extend_entry_when_conditional_factory_reports_not_modified()
    {
        await ResetAsync();
        var cache = CreateCache(Faker.Random.AlphaNumeric(8));
        var key = Faker.Random.AlphaNumeric(10);
        var options = _CreateConditionalOptions();

        var seeded = await cache.GetOrAddAsync<string>(
            key,
            (context, _) => ValueTask.FromResult(context.Modified("v1", "etag-v1")),
            options,
            AbortToken
        );

        await AdvanceAsync(options.Duration + TimeSpan.FromMilliseconds(50));

        // Capture observations and assert after the call: an assertion failure thrown inside the factory
        // would be swallowed by fail-safe (stale present) and the test would pass vacuously.
        var observedHasStale = false;
        string? observedETag = null;

        var extended = await cache.GetOrAddAsync<string>(
            key,
            (context, _) =>
            {
                observedHasStale = context.HasStaleValue;
                observedETag = context.ETag;
                return ValueTask.FromResult(context.NotModified());
            },
            options,
            AbortToken
        );

        var cached = await cache.GetAsync<string>(key, AbortToken);

        seeded.Value.Should().Be("v1");
        observedHasStale.Should().BeTrue();
        observedETag.Should().Be("etag-v1");
        extended.HasValue.Should().BeTrue();
        extended.Value.Should().Be("v1");
        extended.IsStale.Should().BeFalse();
        cached.HasValue.Should().BeTrue("the NotModified restamp must leave the entry fresh again");
        cached.Value.Should().Be("v1");
    }

    public virtual async Task should_replace_entry_when_conditional_factory_reports_modified()
    {
        await ResetAsync();
        var cache = CreateCache(Faker.Random.AlphaNumeric(8));
        var key = Faker.Random.AlphaNumeric(10);
        var options = _CreateConditionalOptions();

        await cache.GetOrAddAsync<string>(
            key,
            (context, _) => ValueTask.FromResult(context.Modified("v1", "etag-v1")),
            options,
            AbortToken
        );

        await AdvanceAsync(options.Duration + TimeSpan.FromMilliseconds(50));

        var replaced = await cache.GetOrAddAsync<string>(
            key,
            (context, _) => ValueTask.FromResult(context.Modified("v2", "etag-v2")),
            options,
            AbortToken
        );

        var cached = await cache.GetAsync<string>(key, AbortToken);

        replaced.HasValue.Should().BeTrue();
        replaced.Value.Should().Be("v2");
        replaced.IsStale.Should().BeFalse();
        cached.HasValue.Should().BeTrue();
        cached.Value.Should().Be("v2");
    }

    // Fail-safe keeps the entry physically retained past its logical expiry, so a stale last-known-good value
    // (and its validators) is still available to the conditional factory after AdvanceAsync(Duration).
    private static CacheEntryOptions _CreateConditionalOptions() =>
        new()
        {
            Duration = TimeSpan.FromMilliseconds(250),
            IsFailSafeEnabled = true,
            FailSafeMaxDuration = TimeSpan.FromSeconds(5),
            FailSafeThrottleDuration = TimeSpan.FromMilliseconds(200),
        };

    private static CacheEntryOptions _CreateFailSafeOptions(TimeSpan? throttleDuration = null) =>
        new()
        {
            Duration = TimeSpan.FromMilliseconds(100),
            IsFailSafeEnabled = true,
            FailSafeMaxDuration = TimeSpan.FromMilliseconds(900),
            FailSafeThrottleDuration = throttleDuration ?? TimeSpan.FromMilliseconds(200),
        };

    private static CacheEntryOptions _CreateTimeoutOptions(
        bool isFailSafeEnabled = true,
        TimeSpan? factorySoftTimeout = null
    ) =>
        new()
        {
            Duration = TimeSpan.FromMilliseconds(100),
            IsFailSafeEnabled = isFailSafeEnabled,
            FailSafeMaxDuration = TimeSpan.FromSeconds(2),
            FailSafeThrottleDuration = TimeSpan.FromMilliseconds(200),
            FactorySoftTimeout = factorySoftTimeout ?? TimeSpan.FromMilliseconds(75),
            FactoryHardTimeout = TimeSpan.FromMilliseconds(250),
            BackgroundFactoryCeiling = TimeSpan.FromSeconds(2),
        };

    private async ValueTask TriggerTimeoutAsync(TimeSpan timeout, Task pendingTimeout)
    {
        await Task.Yield();
        await AdvanceAsync(timeout);

        // The coordinator arms its delay timer only after the factory has started, so with a fake time
        // provider the advance above can land before the timer exists — leaving it armed at the already
        // advanced "now" plus the timeout, with nothing left to move the clock (a permanent hang). Keep
        // nudging time forward on a real-time cadence until the caller-observable task completes.
        for (var attempt = 0; attempt < 50 && !pendingTimeout.IsCompleted; attempt++)
        {
            await TimeProvider.System.Delay(TimeSpan.FromMilliseconds(10), AbortToken);
            await AdvanceAsync(TimeSpan.FromMilliseconds(20));
        }
    }

    private async ValueTask WaitUntilAsync(Func<ValueTask<bool>> condition)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            if (await condition())
            {
                return;
            }

            await AdvanceAsync(TimeSpan.FromMilliseconds(20));
            await TimeProvider.System.Delay(TimeSpan.FromMilliseconds(10), AbortToken);
        }

        throw new TimeoutException("Condition was not satisfied within the polling window.");
    }

    private static CacheEntryOptions _CreateSlidingOptions(TimeSpan? duration = null, TimeSpan? sliding = null) =>
        new()
        {
            Duration = duration ?? TimeSpan.FromMilliseconds(700),
            SlidingExpiration = sliding ?? TimeSpan.FromMilliseconds(200),
        };

    private static CacheEntryOptions _CreateEagerOptions() =>
        new() { Duration = TimeSpan.FromMilliseconds(400), EagerRefreshThreshold = 0.5f };

    protected sealed record CacheConformanceObject(Guid Id, string Name);
}
