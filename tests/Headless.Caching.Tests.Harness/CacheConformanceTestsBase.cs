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

    private static CacheEntryOptions _CreateFailSafeOptions(TimeSpan? throttleDuration = null) =>
        new()
        {
            Duration = TimeSpan.FromMilliseconds(100),
            IsFailSafeEnabled = true,
            FailSafeMaxDuration = TimeSpan.FromMilliseconds(900),
            FailSafeThrottleDuration = throttleDuration ?? TimeSpan.FromMilliseconds(200),
        };

    protected sealed record CacheConformanceObject(Guid Id, string Name);
}
