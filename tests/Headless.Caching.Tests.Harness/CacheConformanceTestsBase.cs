// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Testing.Tests;

namespace Tests;

public abstract class CacheConformanceTestsBase : TestBase
{
    protected abstract ICache CreateCache(string keyPrefix);

    protected virtual ValueTask ResetAsync() => ValueTask.CompletedTask;

    protected virtual ValueTask AdvancePastExpirationAsync(TimeSpan expiration) => ValueTask.CompletedTask;

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
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [firstKey] = "value",
            [nullKey] = null,
        };

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

    protected sealed record CacheConformanceObject(Guid Id, string Name);
}
