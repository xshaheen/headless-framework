// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;
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

        async ValueTask<string?> factory(CancellationToken cancellationToken)
        {
            factoryStarted.SetResult();
            return await factoryGate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        var timeoutTask = cache.GetOrAddAsync(key, factory, options, AbortToken).AsTask();
        await factoryStarted.Task;
        await _TriggerTimeoutAsync(options.FactorySoftTimeout, timeoutTask);

        var timedOut = await timeoutTask;
        factoryGate.SetResult("fresh");

        await _WaitUntilAsync(async () =>
        {
            var cached = await cache.GetAsync<string>(key, AbortToken);
            return cached is { HasValue: true, Value: "fresh" };
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

        async ValueTask<string?> factory(CancellationToken cancellationToken)
        {
            factoryStarted.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            return "fresh";
        }

        var timeoutTask = cache.GetOrAddAsync(key, factory, options, AbortToken).AsTask();
        await factoryStarted.Task;
        await _TriggerTimeoutAsync(options.FactoryHardTimeout, timeoutTask);
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

        async ValueTask<string?> factory(CancellationToken cancellationToken)
        {
            factoryStarted.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            return "fresh";
        }

        var timeoutTask = cache.GetOrAddAsync(key, factory, options, AbortToken).AsTask();
        await factoryStarted.Task;
        await _TriggerTimeoutAsync(options.FactoryHardTimeout, timeoutTask);
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

        async ValueTask<string?> firstFactory(CancellationToken cancellationToken)
        {
            firstStarted.SetResult();
            return await firstGate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        ValueTask<string?> secondFactory(CancellationToken _)
        {
            secondFactoryCalls++;
            return ValueTask.FromResult<string?>("second");
        }

        var first = cache.GetOrAddAsync(key, firstFactory, options, AbortToken).AsTask();
        await firstStarted.Task;
        await Task.Yield();
        var second = cache.GetOrAddAsync(key, secondFactory, options, AbortToken).AsTask();
        await _TriggerTimeoutAsync(options.FactorySoftTimeout, second);
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

        await _WaitUntilAsync(async () =>
        {
            var cached = await cache.GetAsync<string>(key, AbortToken);
            return cached is { HasValue: true, Value: "v2" };
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

        async ValueTask<string?> factory(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref factoryCalls);
            return await factoryGate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        var results = await Task.WhenAll(
            Enumerable.Range(0, 8).Select(_ => cache.GetOrAddAsync(key, factory, options, AbortToken).AsTask())
        );

        results.Should().AllSatisfy(result => result.Value.Should().Be("v1"));

        factoryGate.SetResult("v2");

        await _WaitUntilAsync(async () =>
        {
            var cached = await cache.GetAsync<string>(key, AbortToken);
            return cached is { HasValue: true, Value: "v2" };
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

    public virtual async Task should_refresh_sliding_entry_without_reading_value()
    {
        await ResetAsync();
        var cache = CreateCache(Faker.Random.AlphaNumeric(8));
        var key = Faker.Random.AlphaNumeric(10);
        var options = _CreateSlidingOptions(sliding: TimeSpan.FromMilliseconds(150));

        await cache.UpsertEntryAsync(key, "value", options, AbortToken);
        await AdvanceAsync(TimeSpan.FromMilliseconds(100));

        await cache.RefreshAsync(key, AbortToken);

        await AdvanceAsync(TimeSpan.FromMilliseconds(90));
        var refreshed = await cache.GetAsync<string>(key, AbortToken);

        refreshed.HasValue.Should().BeTrue();
        refreshed.Value.Should().Be("value");
    }

    public virtual async Task should_not_refresh_non_sliding_entry()
    {
        await ResetAsync();
        var cache = CreateCache(Faker.Random.AlphaNumeric(8));
        var key = Faker.Random.AlphaNumeric(10);
        var options = new CacheEntryOptions { Duration = TimeSpan.FromMilliseconds(180) };

        await cache.UpsertEntryAsync(key, "value", options, AbortToken);
        await AdvanceAsync(TimeSpan.FromMilliseconds(120));

        await cache.RefreshAsync(key, AbortToken);

        await AdvanceAsync(TimeSpan.FromMilliseconds(100));
        var expired = await cache.GetAsync<string>(key, AbortToken);

        expired.HasValue.Should().BeFalse();
    }

    public virtual async Task should_ignore_refresh_for_missing_entry()
    {
        await ResetAsync();
        var cache = CreateCache(Faker.Random.AlphaNumeric(8));

        var act = async () => await cache.RefreshAsync(Faker.Random.AlphaNumeric(10), AbortToken);

        await act.Should().NotThrowAsync();
    }

    public virtual async Task should_refresh_tagged_sliding_entry()
    {
        await ResetAsync();
        var cache = CreateCache(Faker.Random.AlphaNumeric(8));
        var key = Faker.Random.AlphaNumeric(10);
        var tag = Faker.Random.AlphaNumeric(8);
        var options = new CacheEntryOptions
        {
            Duration = TimeSpan.FromMilliseconds(700),
            SlidingExpiration = TimeSpan.FromMilliseconds(150),
            Tags = [tag],
        };

        await cache.UpsertEntryAsync(key, "value", options, AbortToken);
        await AdvanceAsync(TimeSpan.FromMilliseconds(100));

        // A tagged entry exercises the Redis full-frame fallback (the header-only read cannot recover tags).
        await cache.RefreshAsync(key, AbortToken);

        await AdvanceAsync(TimeSpan.FromMilliseconds(90));
        var refreshed = await cache.GetAsync<string>(key, AbortToken);

        refreshed.HasValue.Should().BeTrue();
        refreshed.Value.Should().Be("value");
    }

    public virtual async Task should_not_resurrect_tag_invalidated_entry_on_refresh()
    {
        await ResetAsync();
        var cache = CreateCache(Faker.Random.AlphaNumeric(8));
        var key = Faker.Random.AlphaNumeric(10);
        var tag = Faker.Random.AlphaNumeric(8);
        var options = new CacheEntryOptions
        {
            Duration = TimeSpan.FromMinutes(5),
            SlidingExpiration = TimeSpan.FromMilliseconds(200),
            Tags = [tag],
        };

        await cache.UpsertEntryAsync(key, "value", options, AbortToken);
        await AdvanceAsync(TimeSpan.FromMilliseconds(10));
        await cache.RemoveByTagAsync(tag, AbortToken);

        // Refresh must not re-arm (or otherwise revive) a tag-invalidated entry: the read stays a miss.
        await cache.RefreshAsync(key, AbortToken);

        var invalidated = await cache.GetAsync<string>(key, AbortToken);
        invalidated.HasValue.Should().BeFalse();
    }

    public virtual async Task should_expire_immediately_when_upsert_duration_is_non_positive()
    {
        await ResetAsync();
        var cache = CreateCache(Faker.Random.AlphaNumeric(8));
        var key = Faker.Random.AlphaNumeric(10);

        // A non-positive Duration (e.g. a past BCL absolute expiration) is "expire immediately" across providers,
        // not an error: the write is an immediate eviction and the entry is never observable.
        await cache.UpsertEntryAsync(key, "value", new CacheEntryOptions { Duration = TimeSpan.Zero }, AbortToken);

        var immediate = await cache.GetAsync<string>(key, AbortToken);
        immediate.HasValue.Should().BeFalse();
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

    public virtual async Task should_remove_entries_by_tag()
    {
        await ResetAsync();
        var cache = CreateCache(Faker.Random.AlphaNumeric(8));
        var keyA = Faker.Random.AlphaNumeric(10);
        var keyB = Faker.Random.AlphaNumeric(10);
        var keyC = Faker.Random.AlphaNumeric(10);
        var firstTag = Faker.Random.AlphaNumeric(8);
        var secondTag = Faker.Random.AlphaNumeric(8);

        await cache.GetOrAddAsync(
            keyA,
            _ => ValueTask.FromResult<string?>("a"),
            new CacheEntryOptions { Duration = TimeSpan.FromMinutes(5), Tags = [firstTag] },
            AbortToken
        );

        await cache.GetOrAddAsync(
            keyB,
            _ => ValueTask.FromResult<string?>("b"),
            new CacheEntryOptions { Duration = TimeSpan.FromMinutes(5), Tags = [firstTag, secondTag] },
            AbortToken
        );

        await cache.GetOrAddAsync(
            keyC,
            _ => ValueTask.FromResult<string?>("c"),
            new CacheEntryOptions { Duration = TimeSpan.FromMinutes(5), Tags = [secondTag] },
            AbortToken
        );

        // Advance so the invalidation marker is strictly newer than the entries' birth times (Family-2 compares
        // CreatedAt against the marker; with a frozen test clock they would otherwise tie).
        await AdvanceAsync(TimeSpan.FromMilliseconds(10));
        await cache.RemoveByTagAsync(firstTag, AbortToken);

        var cachedA = await cache.GetAsync<string>(keyA, AbortToken);
        var cachedB = await cache.GetAsync<string>(keyB, AbortToken);
        var cachedC = await cache.GetAsync<string>(keyC, AbortToken);

        // Logical invalidation: every entry carrying firstTag now reads as a miss; entries without it are intact.
        cachedA.HasValue.Should().BeFalse();
        cachedB.HasValue.Should().BeFalse();
        cachedC.HasValue.Should().BeTrue();
        cachedC.Value.Should().Be("c");
    }

    public virtual async Task should_remove_entry_via_any_of_its_tags()
    {
        await ResetAsync();
        var cache = CreateCache(Faker.Random.AlphaNumeric(8));
        var key = Faker.Random.AlphaNumeric(10);
        var firstTag = Faker.Random.AlphaNumeric(8);
        var secondTag = Faker.Random.AlphaNumeric(8);

        await cache.GetOrAddAsync(
            key,
            _ => ValueTask.FromResult<string?>("value"),
            new CacheEntryOptions { Duration = TimeSpan.FromMinutes(5), Tags = [firstTag, secondTag] },
            AbortToken
        );

        await AdvanceAsync(TimeSpan.FromMilliseconds(10));
        await cache.RemoveByTagAsync(secondTag, AbortToken);
        var cached = await cache.GetAsync<string>(key, AbortToken);

        cached.HasValue.Should().BeFalse();
    }

    public virtual async Task should_not_remove_recreated_entry_without_tag()
    {
        await ResetAsync();
        var cache = CreateCache(Faker.Random.AlphaNumeric(8));
        var key = Faker.Random.AlphaNumeric(10);
        var tag = Faker.Random.AlphaNumeric(8);

        await cache.GetOrAddAsync(
            key,
            _ => ValueTask.FromResult<string?>("tagged"),
            new CacheEntryOptions { Duration = TimeSpan.FromMinutes(5), Tags = [tag] },
            AbortToken
        );

        // Remove the KEY (not the tag), then re-create the same key WITHOUT the tag. The tag membership is
        // pinned to the removed entry's version, so it must not take the re-created entry down with it.
        await cache.RemoveAsync(key, AbortToken);
        await cache.UpsertAsync(key, "recreated", TimeSpan.FromMinutes(10), AbortToken);

        await cache.RemoveByTagAsync(tag, AbortToken);
        var cached = await cache.GetAsync<string>(key, AbortToken);

        // Version-pinned: the re-created entry has a newer birth time than the tag marker, so it survives.
        cached.HasValue.Should().BeTrue();
        cached.Value.Should().Be("recreated");
    }

    public virtual async Task should_tag_entries_via_conditional_context_and_tagged_upsert()
    {
        await ResetAsync();
        var cache = CreateCache(Faker.Random.AlphaNumeric(8));
        var conditionalKey = Faker.Random.AlphaNumeric(10);
        var upsertKey = Faker.Random.AlphaNumeric(10);
        var tag = Faker.Random.AlphaNumeric(8);

        await cache.GetOrAddAsync<string>(
            conditionalKey,
            (context, _) =>
            {
                context.Tags = [tag];
                return ValueTask.FromResult(context.Modified("v1"));
            },
            new CacheEntryOptions { Duration = TimeSpan.FromMinutes(5) },
            AbortToken
        );

        await cache.UpsertEntryAsync(
            upsertKey,
            "v2",
            new CacheEntryOptions { Duration = TimeSpan.FromMinutes(5), Tags = [tag] },
            AbortToken
        );

        await AdvanceAsync(TimeSpan.FromMilliseconds(10));
        await cache.RemoveByTagAsync(tag, AbortToken);
        var conditionalCached = await cache.GetAsync<string>(conditionalKey, AbortToken);
        var upsertCached = await cache.GetAsync<string>(upsertKey, AbortToken);

        conditionalCached.HasValue.Should().BeFalse();
        upsertCached.HasValue.Should().BeFalse();
    }

    public virtual async Task should_honor_failsafe_options_in_tagged_upsert()
    {
        await ResetAsync();
        var cache = CreateCache(Faker.Random.AlphaNumeric(8));
        var key = Faker.Random.AlphaNumeric(10);
        var tag = Faker.Random.AlphaNumeric(8);
        var options = new CacheEntryOptions
        {
            Duration = TimeSpan.FromMilliseconds(100),
            IsFailSafeEnabled = true,
            FailSafeMaxDuration = TimeSpan.FromMilliseconds(900),
            FailSafeThrottleDuration = TimeSpan.FromMilliseconds(200),
            Tags = [tag],
        };

        // The tagged upsert must extend physical retention exactly like a factory write would, so the entry
        // can serve as a fail-safe stale reserve after its logical expiry.
        await cache.UpsertEntryAsync(key, "value", options, AbortToken);
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

    public virtual async Task should_serve_tag_invalidated_entry_as_failsafe_reserve()
    {
        await ResetAsync();
        var cache = CreateCache(Faker.Random.AlphaNumeric(8));
        var key = Faker.Random.AlphaNumeric(10);
        var tag = Faker.Random.AlphaNumeric(8);
        var options = new CacheEntryOptions
        {
            Duration = TimeSpan.FromMinutes(5),
            IsFailSafeEnabled = true,
            FailSafeMaxDuration = TimeSpan.FromMinutes(30),
            FailSafeThrottleDuration = TimeSpan.FromMilliseconds(200),
            Tags = [tag],
        };

        await cache.GetOrAddAsync(key, _ => ValueTask.FromResult<string?>("value"), options, AbortToken);

        // Logical tag invalidation demotes the entry to a fail-safe reserve: a direct read misses, but a
        // GetOrAddAsync whose factory fails still serves the stale value (the reserve was preserved, not deleted).
        await AdvanceAsync(TimeSpan.FromMilliseconds(10));
        await cache.RemoveByTagAsync(tag, AbortToken);

        var directRead = await cache.GetAsync<string>(key, AbortToken);
        directRead.HasValue.Should().BeFalse();

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

    public virtual async Task should_logically_clear_with_clear_async_preserving_reserves()
    {
        await ResetAsync();
        var cache = CreateCache(Faker.Random.AlphaNumeric(8));
        var taggedKey = Faker.Random.AlphaNumeric(10);
        var untaggedKey = Faker.Random.AlphaNumeric(10);
        var tag = Faker.Random.AlphaNumeric(8);
        var failSafe = new CacheEntryOptions
        {
            Duration = TimeSpan.FromMinutes(5),
            IsFailSafeEnabled = true,
            FailSafeMaxDuration = TimeSpan.FromMinutes(30),
            FailSafeThrottleDuration = TimeSpan.FromMilliseconds(200),
        };

        await cache.GetOrAddAsync(
            taggedKey,
            _ => ValueTask.FromResult<string?>("tagged"),
            failSafe with
            {
                Tags = [tag],
            },
            AbortToken
        );
        await cache.GetOrAddAsync(untaggedKey, _ => ValueTask.FromResult<string?>("untagged"), failSafe, AbortToken);

        await AdvanceAsync(TimeSpan.FromMilliseconds(10));
        await cache.ClearAsync(AbortToken);

        // Both tagged and untagged entries read as misses after a logical clear.
        (await cache.GetAsync<string>(taggedKey, AbortToken))
            .HasValue.Should()
            .BeFalse();
        (await cache.GetAsync<string>(untaggedKey, AbortToken)).HasValue.Should().BeFalse();

        // ...but their fail-safe reserves are preserved: a failing factory still serves the stale value.
        var stale = await cache.GetOrAddAsync<string>(
            untaggedKey,
            _ => throw new InvalidOperationException("downstream unavailable"),
            failSafe,
            AbortToken
        );

        stale.HasValue.Should().BeTrue();
        stale.Value.Should().Be("untagged");
        stale.IsStale.Should().BeTrue();

        // A re-created entry (newer birth time) is unaffected by the earlier clear marker.
        await cache.UpsertAsync(taggedKey, "recreated", TimeSpan.FromMinutes(5), AbortToken);
        var recreated = await cache.GetAsync<string>(taggedKey, AbortToken);
        recreated.HasValue.Should().BeTrue();
        recreated.Value.Should().Be("recreated");
    }

    public virtual async Task should_drop_reserves_with_flush_async()
    {
        await ResetAsync();
        var cache = CreateCache(Faker.Random.AlphaNumeric(8));
        var key = Faker.Random.AlphaNumeric(10);
        var failSafe = new CacheEntryOptions
        {
            Duration = TimeSpan.FromMinutes(5),
            IsFailSafeEnabled = true,
            FailSafeMaxDuration = TimeSpan.FromMinutes(30),
            FailSafeThrottleDuration = TimeSpan.FromMilliseconds(200),
        };

        await cache.GetOrAddAsync(key, _ => ValueTask.FromResult<string?>("value"), failSafe, AbortToken);

        // Advance so the logical remove-generation marker (distributed providers) is strictly newer than the
        // entry's birth time; a physical wipe is unaffected by the gap.
        await AdvanceAsync(TimeSpan.FromMilliseconds(10));

        // Flush drops reserves: unlike ClearAsync, no fail-safe reserve survives, so a failing factory cannot serve
        // a stale value. Removal may be physical (in-process L1) or logical (distributed remove-generation marker);
        // this contract is about the observable outcome, not the mechanism.
        await cache.FlushAsync(AbortToken);

        (await cache.GetAsync<string>(key, AbortToken)).HasValue.Should().BeFalse();

        var act = async () =>
            await cache.GetOrAddAsync<string>(
                key,
                _ => throw new InvalidOperationException("downstream unavailable"),
                failSafe,
                AbortToken
            );

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    public virtual async Task should_add_only_new_set_members_and_compare_strings_case_sensitively()
    {
        await ResetAsync();
        var cache = CreateCache(Faker.Random.AlphaNumeric(8));
        var countKey = Faker.Random.AlphaNumeric(10);
        var caseKey = Faker.Random.AlphaNumeric(10);

        // Added-count: SetAddAsync returns only members that were not already present (matches Redis ZADD); the
        // re-add of "b" must not be counted.
        var firstAdd = await cache.SetAddAsync(countKey, new[] { "a", "b" }, TimeSpan.FromMinutes(5), AbortToken);
        var secondAdd = await cache.SetAddAsync(countKey, new[] { "b", "c" }, TimeSpan.FromMinutes(5), AbortToken);

        // Case-sensitivity: string members use ordinal equality across every provider, so "X" and "x" are distinct.
        var caseAdd = await cache.SetAddAsync(caseKey, new[] { "X", "x" }, TimeSpan.FromMinutes(5), AbortToken);
        var members = await cache.GetSetAsync<string>(caseKey, cancellationToken: AbortToken);

        firstAdd.Should().Be(2);
        secondAdd.Should().Be(1); // only "c" is genuinely new
        caseAdd.Should().Be(2);
        members.HasValue.Should().BeTrue();
        members.Value.Should().HaveCount(2);
    }

    public virtual async Task should_keep_zero_total_after_decrementing_to_zero()
    {
        await ResetAsync();
        var cache = CreateCache(Faker.Random.AlphaNumeric(8));
        var key = Faker.Random.AlphaNumeric(10);

        await cache.IncrementAsync(key, 5L, TimeSpan.FromMinutes(5), AbortToken);
        var result = await cache.IncrementAsync(key, -5L, TimeSpan.FromMinutes(5), AbortToken);
        var cached = await cache.GetAsync<long>(key, AbortToken);

        // A decrement that lands on exactly 0 keeps 0 as a valid stored value across providers — not a miss.
        result.Should().Be(0);
        cached.HasValue.Should().BeTrue("a 0 total is a valid stored value, not a miss");
        cached.Value.Should().Be(0);
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

    private async ValueTask _TriggerTimeoutAsync(TimeSpan timeout, Task pendingTimeout)
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

    private async ValueTask _WaitUntilAsync(Func<ValueTask<bool>> condition)
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

    #region IBufferCache (zero-intermediate-copy buffer path)

    public virtual async Task should_round_trip_raw_payload_via_buffer_path()
    {
        await ResetAsync();
        var cache = CreateCache(Faker.Random.AlphaNumeric(8));
        var buffer = (IBufferCache)cache;
        var key = Faker.Random.AlphaNumeric(10);
        var payload = Faker.Random.Bytes(1024);
        var writer = new ArrayBufferWriter<byte>();

        await buffer.UpsertRawAsync(
            key,
            new ReadOnlySequence<byte>(payload),
            new CacheEntryOptions { Duration = TimeSpan.FromMinutes(5) },
            AbortToken
        );
        var found = await buffer.TryGetToAsync(key, writer, AbortToken);

        found.Should().BeTrue();
        writer.WrittenSpan.ToArray().Should().Equal(payload);
    }

    public virtual async Task should_round_trip_multi_segment_raw_payload_via_buffer_path()
    {
        await ResetAsync();
        var cache = CreateCache(Faker.Random.AlphaNumeric(8));
        var buffer = (IBufferCache)cache;
        var key = Faker.Random.AlphaNumeric(10);
        var first = Faker.Random.Bytes(300);
        var second = Faker.Random.Bytes(300);
        var third = Faker.Random.Bytes(300);
        var expected = first.Concat(second).Concat(third).ToArray();
        var writer = new ArrayBufferWriter<byte>();

        await buffer.UpsertRawAsync(
            key,
            _MultiSegment(first, second, third),
            new CacheEntryOptions { Duration = TimeSpan.FromMinutes(5) },
            AbortToken
        );
        var found = await buffer.TryGetToAsync(key, writer, AbortToken);

        found.Should().BeTrue();
        writer.WrittenSpan.ToArray().Should().Equal(expected);
    }

    public virtual async Task should_read_raw_written_payload_via_generic_path()
    {
        await ResetAsync();
        var cache = CreateCache(Faker.Random.AlphaNumeric(8));
        var buffer = (IBufferCache)cache;
        var key = Faker.Random.AlphaNumeric(10);
        var payload = Faker.Random.Bytes(512);

        // Raw write must be readable by the typed byte[] path: the framing is identical to UpsertEntryAsync.
        await buffer.UpsertRawAsync(
            key,
            new ReadOnlySequence<byte>(payload),
            new CacheEntryOptions { Duration = TimeSpan.FromMinutes(5) },
            AbortToken
        );
        var cached = await cache.GetAsync<byte[]>(key, AbortToken);

        cached.HasValue.Should().BeTrue();
        cached.Value.Should().Equal(payload);
    }

    public virtual async Task should_read_generic_written_payload_via_buffer_path()
    {
        await ResetAsync();
        var cache = CreateCache(Faker.Random.AlphaNumeric(8));
        var buffer = (IBufferCache)cache;
        var key = Faker.Random.AlphaNumeric(10);
        var payload = Faker.Random.Bytes(512);
        var writer = new ArrayBufferWriter<byte>();

        // Typed byte[] write must be readable by the buffer path (cross-path fidelity, the reverse direction).
        await cache.UpsertEntryAsync(
            key,
            payload,
            new CacheEntryOptions { Duration = TimeSpan.FromMinutes(5) },
            AbortToken
        );
        var found = await buffer.TryGetToAsync(key, writer, AbortToken);

        found.Should().BeTrue();
        writer.WrittenSpan.ToArray().Should().Equal(payload);
    }

    public virtual async Task should_invalidate_raw_written_payload_by_tag()
    {
        await ResetAsync();
        var cache = CreateCache(Faker.Random.AlphaNumeric(8));
        var buffer = (IBufferCache)cache;
        var key = Faker.Random.AlphaNumeric(10);
        var tag = Faker.Random.AlphaNumeric(8);
        var payload = Faker.Random.Bytes(256);
        var writer = new ArrayBufferWriter<byte>();

        await buffer.UpsertRawAsync(
            key,
            new ReadOnlySequence<byte>(payload),
            new CacheEntryOptions { Duration = TimeSpan.FromMinutes(5), Tags = [tag] },
            AbortToken
        );

        // Advance so the invalidation marker is strictly newer than the entry's birth time (Family-2 compares
        // CreatedAt against the marker; with a frozen test clock they would otherwise tie).
        await AdvanceAsync(TimeSpan.FromMilliseconds(10));
        await cache.RemoveByTagAsync(tag, AbortToken);
        var found = await buffer.TryGetToAsync(key, writer, AbortToken);

        found.Should().BeFalse();
        writer.WrittenCount.Should().Be(0);
    }

    public virtual async Task should_return_false_and_write_nothing_on_buffer_miss()
    {
        await ResetAsync();
        var cache = CreateCache(Faker.Random.AlphaNumeric(8));
        var buffer = (IBufferCache)cache;
        var writer = new ArrayBufferWriter<byte>();

        var found = await buffer.TryGetToAsync(Faker.Random.AlphaNumeric(10), writer, AbortToken);

        found.Should().BeFalse();
        writer.WrittenCount.Should().Be(0);
    }

    public virtual async Task should_round_trip_empty_raw_payload_via_buffer_path()
    {
        await ResetAsync();
        var cache = CreateCache(Faker.Random.AlphaNumeric(8));
        var buffer = (IBufferCache)cache;
        var key = Faker.Random.AlphaNumeric(10);
        var writer = new ArrayBufferWriter<byte>();

        await buffer.UpsertRawAsync(
            key,
            ReadOnlySequence<byte>.Empty,
            new CacheEntryOptions { Duration = TimeSpan.FromMinutes(5) },
            AbortToken
        );
        var found = await buffer.TryGetToAsync(key, writer, AbortToken);

        // An empty raw upsert is a present-but-empty entry across every provider: the read is a hit that writes
        // zero bytes (distinct from a miss, which the previous test pins to false + nothing written).
        found.Should().BeTrue();
        writer.WrittenCount.Should().Be(0);
    }

    public virtual async Task should_expire_raw_written_payload_after_duration()
    {
        await ResetAsync();
        var cache = CreateCache(Faker.Random.AlphaNumeric(8));
        var buffer = (IBufferCache)cache;
        var key = Faker.Random.AlphaNumeric(10);
        var payload = Faker.Random.Bytes(128);
        var expiration = TimeSpan.FromMilliseconds(250);

        await buffer.UpsertRawAsync(
            key,
            new ReadOnlySequence<byte>(payload),
            new CacheEntryOptions { Duration = expiration },
            AbortToken
        );

        var beforeWriter = new ArrayBufferWriter<byte>();
        var beforeExpiry = await buffer.TryGetToAsync(key, beforeWriter, AbortToken);

        await AdvancePastExpirationAsync(expiration);

        var afterWriter = new ArrayBufferWriter<byte>();
        var afterExpiry = await buffer.TryGetToAsync(key, afterWriter, AbortToken);

        beforeExpiry.Should().BeTrue();
        beforeWriter.WrittenSpan.ToArray().Should().Equal(payload);
        afterExpiry.Should().BeFalse();
        afterWriter.WrittenCount.Should().Be(0);
    }

    private static ReadOnlySequence<byte> _MultiSegment(params byte[][] segments)
    {
        BufferSegment? first = null;
        BufferSegment? last = null;

        foreach (var segment in segments)
        {
            last = last is null ? first = new BufferSegment(segment) : last.Append(segment);
        }

        return new ReadOnlySequence<byte>(first!, 0, last!, last!.Memory.Length);
    }

    private sealed class BufferSegment : ReadOnlySequenceSegment<byte>
    {
        public BufferSegment(ReadOnlyMemory<byte> memory) => Memory = memory;

        public BufferSegment Append(ReadOnlyMemory<byte> memory)
        {
            var next = new BufferSegment(memory) { RunningIndex = RunningIndex + Memory.Length };
            Next = next;

            return next;
        }
    }

    #endregion

    protected sealed record CacheConformanceObject(Guid Id, string Name);
}
