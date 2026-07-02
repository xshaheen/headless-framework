// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections;
using System.Reflection;
using Headless.Caching;
using Headless.Testing.Tests;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

public sealed class InMemoryCacheTests : TestBase
{
    private readonly FakeTimeProvider _timeProvider = new();

    private InMemoryCache _CreateCache(InMemoryCacheOptions? options = null)
    {
        options ??= new InMemoryCacheOptions();
        return new InMemoryCache(_timeProvider, options);
    }

    private static CacheEntryEnvelope _GetEntryEnvelope(InMemoryCache cache, string key)
    {
        var memory = (IEnumerable)_GetMemory(cache);

        foreach (var item in memory)
        {
            var type = item.GetType();
#pragma warning disable REFL009 // Reflection targets runtime KeyValuePair entries from ConcurrentDictionary.
            var itemKey = (string)type.GetProperty("Key")!.GetValue(item)!;
#pragma warning restore REFL009

            if (string.Equals(itemKey, key, StringComparison.Ordinal))
            {
#pragma warning disable REFL009 // Reflection targets runtime KeyValuePair entries from ConcurrentDictionary.
                var entry = type.GetProperty("Value")!.GetValue(item)!;
#pragma warning restore REFL009

                return new CacheEntryEnvelope(
                    _GetEntryProperty<DateTime?>(entry, "LogicalExpiresAt"),
                    _GetEntryProperty<DateTime?>(entry, "PhysicalExpiresAt"),
                    _GetEntryProperty<TimeSpan?>(entry, "SlidingExpiration"),
                    _GetEntryProperty<IReadOnlySet<string>?>(entry, "Tags")
                );
            }
        }

        throw new InvalidOperationException($"Cache entry '{key}' was not found.");
    }

    private static void _ReplaceEntryEnvelope(
        InMemoryCache cache,
        string key,
        object? value,
        DateTime? logicalExpiresAt,
        DateTime? physicalExpiresAt
    )
    {
        var memory = _GetMemory(cache);
        var entryType = typeof(InMemoryCache).GetNestedType("CacheEntry", BindingFlags.NonPublic);
        entryType.Should().NotBeNull();

        // Binds the CacheEntry constructor by exact signature. Each M1 envelope PR (#373 fail-safe,
        // #378 tags) adds parameters here; when that happens GetConstructor returns null and the NotBeNull
        // assertion below fails with a descriptive message instead of an opaque NRE at Invoke.
        var constructor = entryType!.GetConstructor(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            [
                typeof(object),
                typeof(DateTime?),
                typeof(DateTime?),
                typeof(TimeSpan?),
                typeof(TimeProvider),
                typeof(bool),
                typeof(bool),
                typeof(long),
                typeof(IReadOnlyCollection<string>),
                typeof(DateTime?),
                typeof(string),
                typeof(DateTime?),
                typeof(DateTime?),
                typeof(long),
            ],
            modifiers: null
        );
        constructor
            .Should()
            .NotBeNull("the private CacheEntry constructor signature changed — update _ReplaceEntryEnvelope to match");

        var entry = constructor!.Invoke([
            value,
            logicalExpiresAt,
            physicalExpiresAt,
            null,
            typeof(InMemoryCache)
                .GetField("_timeProvider", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)!
                .GetValue(cache),
            false,
            true,
            0L,
            null,
            null,
            null,
            null,
            null,
            -1L,
        ]);
#pragma warning disable REFL009 // ConcurrentDictionary indexer is reached through runtime reflection.
        memory.GetType().GetProperty("Item")!.SetValue(memory, entry, [key]);
#pragma warning restore REFL009
    }

    private static object _GetMemory(InMemoryCache cache)
    {
        var field = typeof(InMemoryCache).GetField(
            "_memory",
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly
        );
        field.Should().NotBeNull();

        return field!.GetValue(cache)!;
    }

    private static async Task _StartMaintenanceAsync(InMemoryCache cache)
    {
        var method = typeof(InMemoryCache).GetMethod(
            "_StartMaintenanceAsync",
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly,
            null,
            [typeof(bool)],
            null
        );
        method.Should().NotBeNull();

        var task = (Task)method!.Invoke(cache, [true])!;
        await task;
        await TimeProvider.System.Delay(TimeSpan.FromMilliseconds(50), TestContext.Current.CancellationToken);
    }

    private static T? _GetEntryProperty<T>(object entry, string propertyName)
    {
        var property = entry
            .GetType()
            .GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        property.Should().NotBeNull();

        return (T?)property.GetValue(entry);
    }

    private static void _AssertEnvelopeParity(InMemoryCache cache, string key, DateTime expectedExpiration)
    {
        var envelope = _GetEntryEnvelope(cache, key);
        envelope.LogicalExpiresAt.Should().Be(expectedExpiration);
        envelope.PhysicalExpiresAt.Should().Be(expectedExpiration);
        envelope.SlidingExpiration.Should().BeNull();
        envelope.Tags.Should().BeNull();
    }

    private sealed record CacheEntryEnvelope(
        DateTime? LogicalExpiresAt,
        DateTime? PhysicalExpiresAt,
        TimeSpan? SlidingExpiration,
        IReadOnlySet<string>? Tags
    );

    #region UpsertAsync

    [Fact]
    public async Task should_upsert_value_when_key_does_not_exist()
    {
        // given
        using var cache = _CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        var value = Faker.Random.Int();

        // when
        var result = await cache.UpsertAsync(key, value, TimeSpan.FromMinutes(5), AbortToken);

        // then
        result.Should().BeTrue();
        var cached = await cache.GetAsync<int>(key, AbortToken);
        cached.HasValue.Should().BeTrue();
        cached.Value.Should().Be(value);
    }

    [Fact]
    public async Task should_upsert_value_when_key_exists()
    {
        // given
        using var cache = _CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        var initialValue = Faker.Random.Int();
        var newValue = Faker.Random.Int();
        await cache.UpsertAsync(key, initialValue, TimeSpan.FromMinutes(5), AbortToken);

        // when
        var result = await cache.UpsertAsync(key, newValue, TimeSpan.FromMinutes(5), AbortToken);

        // then
        result.Should().BeTrue();
        var cached = await cache.GetAsync<int>(key, AbortToken);
        cached.Value.Should().Be(newValue);
    }

    [Fact]
    public async Task should_upsert_null_value_when_value_is_null()
    {
        // given
        using var cache = _CreateCache();
        var key = Faker.Random.AlphaNumeric(10);

        // when
        var result = await cache.UpsertAsync<string>(key, null, TimeSpan.FromMinutes(5), AbortToken);

        // then
        result.Should().BeTrue();
        var cached = await cache.GetAsync<string>(key, AbortToken);
        cached.HasValue.Should().BeTrue();
        cached.Value.Should().BeNull();
    }

    [Fact]
    public async Task should_evict_when_expiration_is_zero()
    {
        // given - non-positive duration is "expire immediately" across every provider (matches Redis)
        using var cache = _CreateCache();
        var key = Faker.Random.AlphaNumeric(10);

        // when
        var result = await cache.UpsertAsync(key, 42, TimeSpan.Zero, AbortToken);

        // then
        result.Should().BeFalse();
        var cached = await cache.GetAsync<int>(key, AbortToken);
        cached.HasValue.Should().BeFalse();
    }

    [Fact]
    public async Task should_store_without_expiration_when_expiration_is_null()
    {
        // given
        using var cache = _CreateCache();
        var key = Faker.Random.AlphaNumeric(10);

        // when
        await cache.UpsertAsync(key, 42, null, AbortToken);
        _timeProvider.Advance(TimeSpan.FromDays(365));

        // then
        var cached = await cache.GetAsync<int>(key, AbortToken);
        cached.HasValue.Should().BeTrue();
        cached.Value.Should().Be(42);
    }

    #endregion

    #region CacheEntry Envelope

    [Fact]
    public async Task should_store_logical_and_physical_expiration_for_factory_entry()
    {
        // given
        using var cache = _CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        var duration = TimeSpan.FromMinutes(10);
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        // when
        await cache.GetOrAddAsync(key, _ => ValueTask.FromResult<string?>("value"), duration, AbortToken);

        // then
        var envelope = _GetEntryEnvelope(cache, key);
        envelope.LogicalExpiresAt.Should().Be(now.Add(duration));
        envelope.PhysicalExpiresAt.Should().Be(now.Add(duration));
        envelope.SlidingExpiration.Should().BeNull();
        envelope.Tags.Should().BeNull();
    }

    [Fact]
    public async Task should_store_sliding_expiration_for_factory_entry()
    {
        // given
        using var cache = _CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var options = new CacheEntryOptions
        {
            Duration = TimeSpan.FromMinutes(10),
            SlidingExpiration = TimeSpan.FromMinutes(2),
        };

        // when
        await cache.GetOrAddAsync(key, _ => ValueTask.FromResult<string?>("value"), options, AbortToken);

        // then
        var envelope = _GetEntryEnvelope(cache, key);
        envelope.LogicalExpiresAt.Should().Be(now.Add(options.SlidingExpiration.Value));
        envelope.PhysicalExpiresAt.Should().Be(now.Add(options.Duration));
        envelope.SlidingExpiration.Should().Be(options.SlidingExpiration);
    }

    [Fact]
    public async Task should_rearm_sliding_entry_on_value_read_without_moving_physical_cap()
    {
        // given
        using var cache = _CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        var options = new CacheEntryOptions
        {
            Duration = TimeSpan.FromSeconds(10),
            SlidingExpiration = TimeSpan.FromSeconds(2),
        };
        await cache.GetOrAddAsync(key, _ => ValueTask.FromResult<string?>("value"), options, AbortToken);
        var before = _GetEntryEnvelope(cache, key);
        _timeProvider.Advance(TimeSpan.FromMilliseconds(1300));

        // when
        var cached = await cache.GetAsync<string>(key, AbortToken);

        // then
        var after = _GetEntryEnvelope(cache, key);
        cached.Value.Should().Be("value");
        after.LogicalExpiresAt.Should().Be(_timeProvider.GetUtcNow().UtcDateTime.Add(options.SlidingExpiration.Value));
        after.PhysicalExpiresAt.Should().Be(before.PhysicalExpiresAt);
        after.SlidingExpiration.Should().Be(options.SlidingExpiration);
    }

    [Fact]
    public async Task should_not_rearm_sliding_entry_before_rearm_threshold()
    {
        // given
        using var cache = _CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        var options = new CacheEntryOptions
        {
            Duration = TimeSpan.FromSeconds(10),
            SlidingExpiration = TimeSpan.FromSeconds(2),
        };
        await cache.GetOrAddAsync(key, _ => ValueTask.FromResult<string?>("value"), options, AbortToken);
        var before = _GetEntryEnvelope(cache, key);
        _timeProvider.Advance(TimeSpan.FromMilliseconds(250));

        // when
        var cached = await cache.GetAsync<string>(key, AbortToken);

        // then
        var after = _GetEntryEnvelope(cache, key);
        cached.Value.Should().Be("value");
        after.LogicalExpiresAt.Should().Be(before.LogicalExpiresAt);
    }

    [Fact]
    public async Task should_not_rearm_sliding_entry_on_metadata_read()
    {
        // given
        using var cache = _CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        var options = new CacheEntryOptions
        {
            Duration = TimeSpan.FromSeconds(10),
            SlidingExpiration = TimeSpan.FromSeconds(2),
        };
        await cache.GetOrAddAsync(key, _ => ValueTask.FromResult<string?>("value"), options, AbortToken);
        var before = _GetEntryEnvelope(cache, key);
        _timeProvider.Advance(TimeSpan.FromMilliseconds(1300));

        // when
        var exists = await cache.ExistsAsync(key, AbortToken);
        var expiration = await cache.GetExpirationAsync(key, AbortToken);

        // then
        var after = _GetEntryEnvelope(cache, key);
        exists.Should().BeTrue();
        expiration.Should().BeCloseTo(TimeSpan.FromMilliseconds(700), TimeSpan.FromMilliseconds(1));
        after.LogicalExpiresAt.Should().Be(before.LogicalExpiresAt);
    }

    [Fact]
    public async Task should_remove_idle_sliding_entry_during_maintenance_before_physical_cap()
    {
        // given
        using var cache = _CreateCache(new InMemoryCacheOptions { MaintenanceInterval = TimeSpan.FromMilliseconds(1) });
        var key = Faker.Random.AlphaNumeric(10);
        var options = new CacheEntryOptions
        {
            Duration = TimeSpan.FromMinutes(5),
            SlidingExpiration = TimeSpan.FromMilliseconds(100),
        };
        await cache.GetOrAddAsync(key, _ => ValueTask.FromResult<string?>("value"), options, AbortToken);
        _timeProvider.Advance(TimeSpan.FromMilliseconds(150));

        // when
        await _StartMaintenanceAsync(cache);

        // then
        var count = await cache.GetCountAsync(cancellationToken: AbortToken);
        count.Should().Be(0);
    }

    [Fact]
    public async Task should_store_logical_and_physical_expiration_for_upsert_entry()
    {
        // given
        using var cache = _CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        var duration = TimeSpan.FromMinutes(5);
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        // when
        await cache.UpsertAsync(key, "value", duration, AbortToken);

        // then
        var envelope = _GetEntryEnvelope(cache, key);
        envelope.LogicalExpiresAt.Should().Be(now.Add(duration));
        envelope.PhysicalExpiresAt.Should().Be(now.Add(duration));
        envelope.SlidingExpiration.Should().BeNull();
    }

    [Fact]
    public async Task should_store_empty_reserved_envelope_slots_for_new_entry()
    {
        // given
        using var cache = _CreateCache();
        var key = Faker.Random.AlphaNumeric(10);

        // when
        await cache.UpsertAsync(key, "value", TimeSpan.FromMinutes(5), AbortToken);

        // then
        var envelope = _GetEntryEnvelope(cache, key);
        envelope.Tags.Should().BeNull();
    }

    [Fact]
    public async Task should_store_null_logical_and_physical_expiration_for_eternal_entry()
    {
        // given
        using var cache = _CreateCache();
        var key = Faker.Random.AlphaNumeric(10);

        // when
        await cache.UpsertAsync(key, "value", expiration: null, AbortToken);

        // then
        var envelope = _GetEntryEnvelope(cache, key);
        envelope.LogicalExpiresAt.Should().BeNull();
        envelope.PhysicalExpiresAt.Should().BeNull();
        envelope.SlidingExpiration.Should().BeNull();
    }

    [Fact]
    public async Task should_use_logical_expiration_for_value_reads_and_physical_expiration_for_key_presence()
    {
        // given
        using var cache = _CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        _ReplaceEntryEnvelope(
            cache,
            key,
            "value",
            logicalExpiresAt: now.AddMinutes(-1),
            physicalExpiresAt: now.AddMinutes(5)
        );

        // when
        var cached = await cache.GetAsync<string>(key, AbortToken);
        var exists = await cache.ExistsAsync(key, AbortToken);
        var expiration = await cache.GetExpirationAsync(key, AbortToken);
        var count = await cache.GetCountAsync(cancellationToken: AbortToken);
        var keys = await cache.GetAllKeysByPrefixAsync("", AbortToken);

        // then
        cached.HasValue.Should().BeFalse();
        exists.Should().BeFalse();
        expiration.Should().BeNull();
        count.Should().Be(1);
        keys.Should().Contain(key);
    }

    [Fact]
    public async Task should_store_logical_and_physical_expiration_for_representative_write_entries()
    {
        // given
        using var cache = _CreateCache();
        var duration = TimeSpan.FromMinutes(5);
        var expectedExpiration = _timeProvider.GetUtcNow().UtcDateTime.Add(duration);

        // when
        await cache.TryInsertAsync("try-insert", "value", duration, AbortToken);
        await cache.UpsertAsync("try-replace", "old", duration, AbortToken);
        await cache.TryReplaceAsync("try-replace", "new", duration, AbortToken);
        await cache.UpsertAsync("try-replace-equal", "old", duration, AbortToken);
        await cache.TryReplaceIfEqualAsync("try-replace-equal", "old", "new", duration, AbortToken);
        await cache.UpsertAllAsync(
            new Dictionary<string, string>(StringComparer.Ordinal) { ["upsert-all"] = "value" },
            duration,
            AbortToken
        );
        await cache.IncrementAsync("increment", 1L, duration, AbortToken);
        await cache.UpsertAsync("set-if-higher-refresh", 10L, duration, AbortToken);
        await cache.SetIfHigherAsync("set-if-higher-refresh", 5L, duration, AbortToken);
        await cache.UpsertAsync("set-if-lower-refresh", 5L, duration, AbortToken);
        await cache.SetIfLowerAsync("set-if-lower-refresh", 10L, duration, AbortToken);
        await cache.SetAddAsync("set-add", ["value"], duration, AbortToken);

        // then
        _AssertEnvelopeParity(cache, "try-insert", expectedExpiration);
        _AssertEnvelopeParity(cache, "try-replace", expectedExpiration);
        _AssertEnvelopeParity(cache, "try-replace-equal", expectedExpiration);
        _AssertEnvelopeParity(cache, "upsert-all", expectedExpiration);
        _AssertEnvelopeParity(cache, "increment", expectedExpiration);
        _AssertEnvelopeParity(cache, "set-if-higher-refresh", expectedExpiration);
        _AssertEnvelopeParity(cache, "set-if-lower-refresh", expectedExpiration);
        _AssertEnvelopeParity(cache, "set-add", expectedExpiration);
    }

    #endregion

    #region UpsertAllAsync

    [Fact]
    public async Task should_upsert_all_values_when_dictionary_has_items()
    {
        // given
        using var cache = _CreateCache();
        var values = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            [Faker.Random.AlphaNumeric(10)] = 1,
            [Faker.Random.AlphaNumeric(10)] = 2,
            [Faker.Random.AlphaNumeric(10)] = 3,
        };

        // when
        var result = await cache.UpsertAllAsync(values, TimeSpan.FromMinutes(5), AbortToken);

        // then
        result.Should().Be(3);
        foreach (var (k, v) in values)
        {
            var cached = await cache.GetAsync<int>(k, AbortToken);
            cached.Value.Should().Be(v);
        }
    }

    [Fact]
    public async Task should_return_zero_when_dictionary_is_empty()
    {
        // given
        using var cache = _CreateCache();
        var values = new Dictionary<string, int>(StringComparer.Ordinal);

        // when
        var result = await cache.UpsertAllAsync(values, TimeSpan.FromMinutes(5), AbortToken);

        // then
        result.Should().Be(0);
    }

    [Fact]
    public async Task should_throw_when_upsert_all_expiration_is_zero()
    {
        // given
        using var cache = _CreateCache();
        var values = new Dictionary<string, int>(StringComparer.Ordinal) { ["key1"] = 1, ["key2"] = 2 };

        // when
        var act = async () => await cache.UpsertAllAsync(values, TimeSpan.Zero, AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    #endregion

    #region TryInsertAsync

    [Fact]
    public async Task should_insert_value_when_key_does_not_exist()
    {
        // given
        using var cache = _CreateCache();
        var key = Faker.Random.AlphaNumeric(10);

        // when
        var result = await cache.TryInsertAsync(key, 42, TimeSpan.FromMinutes(5), AbortToken);

        // then
        result.Should().BeTrue();
        var cached = await cache.GetAsync<int>(key, AbortToken);
        cached.Value.Should().Be(42);
    }

    [Fact]
    public async Task should_not_insert_when_key_exists()
    {
        // given
        using var cache = _CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        await cache.UpsertAsync(key, 42, TimeSpan.FromMinutes(5), AbortToken);

        // when
        var result = await cache.TryInsertAsync(key, 99, TimeSpan.FromMinutes(5), AbortToken);

        // then
        result.Should().BeFalse();
        var cached = await cache.GetAsync<int>(key, AbortToken);
        cached.Value.Should().Be(42);
    }

    [Fact]
    public async Task should_insert_when_existing_key_is_expired()
    {
        // given
        using var cache = _CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        await cache.UpsertAsync(key, 42, TimeSpan.FromSeconds(1), AbortToken);
        _timeProvider.Advance(TimeSpan.FromSeconds(2));

        // when
        var result = await cache.TryInsertAsync(key, 99, TimeSpan.FromMinutes(5), AbortToken);

        // then
        result.Should().BeTrue();
        var cached = await cache.GetAsync<int>(key, AbortToken);
        cached.Value.Should().Be(99);
    }

    [Fact]
    public async Task should_throw_when_try_insert_expiration_is_zero()
    {
        // given
        using var cache = _CreateCache();
        var key = Faker.Random.AlphaNumeric(10);

        // when
        var act = async () => await cache.TryInsertAsync(key, 42, TimeSpan.Zero, AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    #endregion

    #region TryReplaceAsync

    [Fact]
    public async Task should_replace_value_when_key_exists()
    {
        // given
        using var cache = _CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        await cache.UpsertAsync(key, 42, TimeSpan.FromMinutes(5), AbortToken);

        // when
        var result = await cache.TryReplaceAsync(key, 99, TimeSpan.FromMinutes(5), AbortToken);

        // then
        result.Should().BeTrue();
        var cached = await cache.GetAsync<int>(key, AbortToken);
        cached.Value.Should().Be(99);
    }

    [Fact]
    public async Task should_not_replace_when_key_does_not_exist()
    {
        // given
        using var cache = _CreateCache();
        var key = Faker.Random.AlphaNumeric(10);

        // when
        var result = await cache.TryReplaceAsync(key, 42, TimeSpan.FromMinutes(5), AbortToken);

        // then
        result.Should().BeFalse();
    }

    #endregion

    #region TryReplaceIfEqualAsync

    [Fact]
    public async Task should_return_false_when_entry_is_expired()
    {
        // given
        using var cache = _CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        var expiration = TimeSpan.FromMilliseconds(50);
        await cache.UpsertAsync(key, 42, expiration, AbortToken);

        _timeProvider.Advance(TimeSpan.FromMilliseconds(100));

        // when
        var result = await cache.TryReplaceIfEqualAsync(key, 42, 99, TimeSpan.FromMinutes(5), AbortToken);

        // then
        result.Should().BeFalse();
    }

    [Fact]
    public async Task should_replace_when_value_equals_expected()
    {
        // given
        using var cache = _CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        await cache.UpsertAsync(key, 42, TimeSpan.FromMinutes(5), AbortToken);

        // when
        var result = await cache.TryReplaceIfEqualAsync(key, 42, 99, TimeSpan.FromMinutes(5), AbortToken);

        // then
        result.Should().BeTrue();
        var cached = await cache.GetAsync<int>(key, AbortToken);
        cached.Value.Should().Be(99);
    }

    [Fact]
    public async Task should_not_replace_when_value_does_not_equal_expected()
    {
        // given
        using var cache = _CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        await cache.UpsertAsync(key, 42, TimeSpan.FromMinutes(5), AbortToken);

        // when
        var result = await cache.TryReplaceIfEqualAsync(key, 100, 99, TimeSpan.FromMinutes(5), AbortToken);

        // then
        result.Should().BeFalse();
        var cached = await cache.GetAsync<int>(key, AbortToken);
        cached.Value.Should().Be(42);
    }

    [Fact]
    public async Task should_return_false_when_key_does_not_exist_for_replace_if_equal()
    {
        // given
        using var cache = _CreateCache();
        var key = Faker.Random.AlphaNumeric(10);

        // when
        var result = await cache.TryReplaceIfEqualAsync(key, 42, 99, TimeSpan.FromMinutes(5), AbortToken);

        // then
        result.Should().BeFalse();
    }

    #endregion

    #region IncrementAsync (double)

    [Fact]
    public async Task should_increment_double_when_key_does_not_exist()
    {
        // given
        using var cache = _CreateCache();
        var key = Faker.Random.AlphaNumeric(10);

        // when
        var result = await cache.IncrementAsync(key, 5.5, TimeSpan.FromMinutes(5), AbortToken);

        // then
        result.Should().Be(5.5);
        var cached = await cache.GetAsync<double>(key, AbortToken);
        cached.Value.Should().Be(5.5);
    }

    [Fact]
    public async Task should_increment_double_when_key_exists()
    {
        // given
        using var cache = _CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        await cache.UpsertAsync(key, 10.0, TimeSpan.FromMinutes(5), AbortToken);

        // when
        var result = await cache.IncrementAsync(key, 5.5, TimeSpan.FromMinutes(5), AbortToken);

        // then
        result.Should().Be(15.5);
    }

    [Fact]
    public async Task should_decrement_double_when_amount_is_negative()
    {
        // given
        using var cache = _CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        await cache.UpsertAsync(key, 10.0, TimeSpan.FromMinutes(5), AbortToken);

        // when
        var result = await cache.IncrementAsync(key, -3.5, TimeSpan.FromMinutes(5), AbortToken);

        // then
        result.Should().Be(6.5);
    }

    [Fact]
    public async Task should_evict_and_return_zero_when_double_increment_expiration_is_zero()
    {
        // given - non-positive duration is "expire immediately" across every provider (matches Redis)
        using var cache = _CreateCache();
        var key = Faker.Random.AlphaNumeric(10);

        // when
        var result = await cache.IncrementAsync(key, 5.5, TimeSpan.Zero, AbortToken);

        // then
        result.Should().Be(0);
        var cached = await cache.GetAsync<double>(key, AbortToken);
        cached.HasValue.Should().BeFalse();
    }

    #endregion

    #region IncrementAsync (long)

    [Fact]
    public async Task should_increment_long_when_key_does_not_exist()
    {
        // given
        using var cache = _CreateCache();
        var key = Faker.Random.AlphaNumeric(10);

        // when
        var result = await cache.IncrementAsync(key, 5L, TimeSpan.FromMinutes(5), AbortToken);

        // then
        result.Should().Be(5L);
    }

    [Fact]
    public async Task should_increment_long_when_key_exists()
    {
        // given
        using var cache = _CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        await cache.IncrementAsync(key, 10L, TimeSpan.FromMinutes(5), AbortToken);

        // when
        var result = await cache.IncrementAsync(key, 5L, TimeSpan.FromMinutes(5), AbortToken);

        // then
        result.Should().Be(15L);
    }

    [Fact]
    public async Task should_decrement_long_when_amount_is_negative()
    {
        // given
        using var cache = _CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        await cache.IncrementAsync(key, 10L, TimeSpan.FromMinutes(5), AbortToken);

        // when
        var result = await cache.IncrementAsync(key, -3L, TimeSpan.FromMinutes(5), AbortToken);

        // then
        result.Should().Be(7L);
    }

    [Fact]
    public async Task should_evict_and_return_zero_when_long_increment_expiration_is_zero()
    {
        // given - non-positive duration is "expire immediately" across every provider (matches Redis)
        using var cache = _CreateCache();
        var key = Faker.Random.AlphaNumeric(10);

        // when
        var result = await cache.IncrementAsync(key, 5L, TimeSpan.Zero, AbortToken);

        // then
        result.Should().Be(0L);
        var cached = await cache.GetAsync<long>(key, AbortToken);
        cached.HasValue.Should().BeFalse();
    }

    #endregion

    #region SetIfHigherAsync (double)

    [Fact]
    public async Task should_set_double_when_key_does_not_exist_for_set_if_higher()
    {
        // given
        using var cache = _CreateCache();
        var key = Faker.Random.AlphaNumeric(10);

        // when
        var result = await cache.SetIfHigherAsync(key, 10.0, TimeSpan.FromMinutes(5), AbortToken);

        // then
        result.Should().Be(10.0);
        var cached = await cache.GetAsync<double>(key, AbortToken);
        cached.Value.Should().Be(10.0);
    }

    [Fact]
    public async Task should_set_double_when_value_is_higher()
    {
        // given
        using var cache = _CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        await cache.UpsertAsync(key, 5.0, TimeSpan.FromMinutes(5), AbortToken);

        // when
        var result = await cache.SetIfHigherAsync(key, 10.0, TimeSpan.FromMinutes(5), AbortToken);

        // then
        result.Should().Be(5.0); // returns difference
        var cached = await cache.GetAsync<double>(key, AbortToken);
        cached.Value.Should().Be(10.0);
    }

    [Fact]
    public async Task should_not_set_double_when_value_is_lower()
    {
        // given
        using var cache = _CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        await cache.UpsertAsync(key, 10.0, TimeSpan.FromMinutes(5), AbortToken);

        // when
        var result = await cache.SetIfHigherAsync(key, 5.0, TimeSpan.FromMinutes(5), AbortToken);

        // then
        result.Should().Be(0);
        var cached = await cache.GetAsync<double>(key, AbortToken);
        cached.Value.Should().Be(10.0);
    }

    #endregion

    #region SetIfHigherAsync (long)

    [Fact]
    public async Task should_set_long_when_key_does_not_exist_for_set_if_higher()
    {
        // given
        using var cache = _CreateCache();
        var key = Faker.Random.AlphaNumeric(10);

        // when
        var result = await cache.SetIfHigherAsync(key, 10L, TimeSpan.FromMinutes(5), AbortToken);

        // then
        result.Should().Be(10L);
    }

    [Fact]
    public async Task should_set_long_when_value_is_higher()
    {
        // given
        using var cache = _CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        await cache.IncrementAsync(key, 5L, TimeSpan.FromMinutes(5), AbortToken);

        // when
        var result = await cache.SetIfHigherAsync(key, 10L, TimeSpan.FromMinutes(5), AbortToken);

        // then
        result.Should().Be(5L);
        var cached = await cache.GetAsync<long>(key, AbortToken);
        cached.Value.Should().Be(10L);
    }

    [Fact]
    public async Task should_not_set_long_when_value_is_lower()
    {
        // given
        using var cache = _CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        await cache.IncrementAsync(key, 10L, TimeSpan.FromMinutes(5), AbortToken);

        // when
        var result = await cache.SetIfHigherAsync(key, 5L, TimeSpan.FromMinutes(5), AbortToken);

        // then
        result.Should().Be(0L);
        var cached = await cache.GetAsync<long>(key, AbortToken);
        cached.Value.Should().Be(10L);
    }

    #endregion

    #region SetIfLowerAsync (double)

    [Fact]
    public async Task should_set_double_when_key_does_not_exist_for_set_if_lower()
    {
        // given
        using var cache = _CreateCache();
        var key = Faker.Random.AlphaNumeric(10);

        // when
        var result = await cache.SetIfLowerAsync(key, 10.0, TimeSpan.FromMinutes(5), AbortToken);

        // then
        result.Should().Be(10.0);
    }

    [Fact]
    public async Task should_set_double_when_value_is_lower()
    {
        // given
        using var cache = _CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        await cache.UpsertAsync(key, 10.0, TimeSpan.FromMinutes(5), AbortToken);

        // when
        var result = await cache.SetIfLowerAsync(key, 5.0, TimeSpan.FromMinutes(5), AbortToken);

        // then
        result.Should().Be(5.0); // returns difference
        var cached = await cache.GetAsync<double>(key, AbortToken);
        cached.Value.Should().Be(5.0);
    }

    [Fact]
    public async Task should_not_set_double_when_value_is_higher_for_set_if_lower()
    {
        // given
        using var cache = _CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        await cache.UpsertAsync(key, 5.0, TimeSpan.FromMinutes(5), AbortToken);

        // when
        var result = await cache.SetIfLowerAsync(key, 10.0, TimeSpan.FromMinutes(5), AbortToken);

        // then
        result.Should().Be(0);
        var cached = await cache.GetAsync<double>(key, AbortToken);
        cached.Value.Should().Be(5.0);
    }

    #endregion

    #region SetIfLowerAsync (long)

    [Fact]
    public async Task should_set_long_when_key_does_not_exist_for_set_if_lower()
    {
        // given
        using var cache = _CreateCache();
        var key = Faker.Random.AlphaNumeric(10);

        // when
        var result = await cache.SetIfLowerAsync(key, 10L, TimeSpan.FromMinutes(5), AbortToken);

        // then
        result.Should().Be(10L);
    }

    [Fact]
    public async Task should_set_long_when_value_is_lower()
    {
        // given
        using var cache = _CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        await cache.IncrementAsync(key, 10L, TimeSpan.FromMinutes(5), AbortToken);

        // when
        var result = await cache.SetIfLowerAsync(key, 5L, TimeSpan.FromMinutes(5), AbortToken);

        // then
        result.Should().Be(5L);
        var cached = await cache.GetAsync<long>(key, AbortToken);
        cached.Value.Should().Be(5L);
    }

    [Fact]
    public async Task should_not_set_long_when_value_is_higher_for_set_if_lower()
    {
        // given
        using var cache = _CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        await cache.IncrementAsync(key, 5L, TimeSpan.FromMinutes(5), AbortToken);

        // when
        var result = await cache.SetIfLowerAsync(key, 10L, TimeSpan.FromMinutes(5), AbortToken);

        // then
        result.Should().Be(0L);
        var cached = await cache.GetAsync<long>(key, AbortToken);
        cached.Value.Should().Be(5L);
    }

    #endregion

    #region SetAddAsync

    [Fact]
    public async Task should_add_items_to_new_set()
    {
        // given
        using var cache = _CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        var items = new[] { 1, 2, 3 };

        // when
        var result = await cache.SetAddAsync(key, items, TimeSpan.FromMinutes(5), AbortToken);

        // then
        result.Should().Be(3);
    }

    [Fact]
    public async Task should_add_items_to_existing_set()
    {
        // given
        using var cache = _CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        await cache.SetAddAsync(key, new[] { 1, 2 }, TimeSpan.FromMinutes(5), AbortToken);

        // when
        var result = await cache.SetAddAsync(key, new[] { 3, 4 }, TimeSpan.FromMinutes(5), AbortToken);

        // then
        result.Should().Be(2);
    }

    [Fact]
    public async Task should_count_only_newly_added_members_when_some_already_exist()
    {
        // SetAddAsync returns the count of members actually added (mirrors Redis ZADD), excluding members already
        // present in the set.
        // given
        using var cache = _CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        await cache.SetAddAsync(key, new[] { 1, 2 }, TimeSpan.FromMinutes(5), AbortToken);

        // when - re-add 2 (already present) plus 3 (new)
        var result = await cache.SetAddAsync(key, new[] { 2, 3 }, TimeSpan.FromMinutes(5), AbortToken);

        // then
        result.Should().Be(1);
    }

    [Fact]
    public async Task should_add_string_to_set()
    {
        // given
        using var cache = _CreateCache();
        var key = Faker.Random.AlphaNumeric(10);

        // when
        var result = await cache.SetAddAsync(key, new[] { "test-value" }, TimeSpan.FromMinutes(5), AbortToken);

        // then
        result.Should().Be(1);
    }

    [Fact]
    public async Task should_roundtrip_string_set_through_get_set()
    {
        // Regression: SetAddAsync<string> previously stored Dictionary<object, DateTime?>
        // while GetSetAsync<string> read Dictionary<string, DateTime?>, causing InvalidCastException.
        // given
        using var cache = _CreateCache();
        var key = Faker.Random.AlphaNumeric(10);

        // when
        await cache.SetAddAsync(key, new[] { "a", "b" }, expiration: null, AbortToken);
        var result = await cache.GetSetAsync<string>(key, cancellationToken: AbortToken);

        // then
        result.HasValue.Should().BeTrue();
        result.Value.Should().BeEquivalentTo(["a", "b"]);
    }

    [Fact]
    public async Task should_merge_strings_into_existing_set()
    {
        // given
        using var cache = _CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        await cache.SetAddAsync(key, new[] { "a", "b" }, TimeSpan.FromMinutes(5), AbortToken);

        // when
        await cache.SetAddAsync(key, new[] { "c", "d" }, TimeSpan.FromMinutes(5), AbortToken);
        var result = await cache.GetSetAsync<string>(key, cancellationToken: AbortToken);

        // then
        result.HasValue.Should().BeTrue();
        result.Value.Should().BeEquivalentTo(["a", "b", "c", "d"]);
    }

    [Fact]
    public async Task should_return_zero_when_adding_null_items_to_set()
    {
        // given
        using var cache = _CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        var items = new int?[] { null, null };

        // when
        var result = await cache.SetAddAsync(key, items, TimeSpan.FromMinutes(5), AbortToken);

        // then
        result.Should().Be(0);
    }

    #endregion

    #region GetSetAsync

    [Fact]
    public async Task should_get_all_set_items()
    {
        // given
        using var cache = _CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        await cache.SetAddAsync(key, new[] { 1, 2, 3 }, TimeSpan.FromMinutes(5), AbortToken);

        // when - SetAddAsync with non-string stores as IDictionary<object, DateTime?>
        var result = await cache.GetSetAsync<object>(key, cancellationToken: AbortToken);

        // then
        result.HasValue.Should().BeTrue();
        result.Value.Should().BeEquivalentTo([1, 2, 3]);
    }

    [Fact]
    public async Task should_return_no_value_when_set_does_not_exist()
    {
        // given
        using var cache = _CreateCache();
        var key = Faker.Random.AlphaNumeric(10);

        // when
        var result = await cache.GetSetAsync<object>(key, cancellationToken: AbortToken);

        // then - an absent key returns CacheValue<T>.NoValue (Value is null, not an empty collection)
        result.HasValue.Should().BeFalse();
        result.Value.Should().BeNull();
    }

    [Fact]
    public async Task should_page_set_items()
    {
        // given
        using var cache = _CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        await cache.SetAddAsync(key, new[] { 1, 2, 3, 4, 5 }, TimeSpan.FromMinutes(5), AbortToken);

        // when - SetAddAsync with non-string stores as IDictionary<object, DateTime?>
        var result = await cache.GetSetAsync<object>(key, pageIndex: 1, pageSize: 2, cancellationToken: AbortToken);

        // then
        result.HasValue.Should().BeTrue();
        result.Value.Should().HaveCount(2);
    }

    #endregion

    #region SetRemoveAsync

    [Fact]
    public async Task should_remove_items_from_set()
    {
        // given
        using var cache = _CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        await cache.SetAddAsync(key, new[] { 1, 2, 3 }, TimeSpan.FromMinutes(5), AbortToken);

        // when
        var result = await cache.SetRemoveAsync(key, new[] { 2 }, null, AbortToken);

        // then
        result.Should().Be(1);
        var set = await cache.GetSetAsync<object>(key, cancellationToken: AbortToken);
        set.Value.Should().BeEquivalentTo([1, 3]);
    }

    [Fact]
    public async Task should_remove_string_from_set()
    {
        // given
        using var cache = _CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        await cache.SetAddAsync(key, new[] { "item1", "item2" }, TimeSpan.FromMinutes(5), AbortToken);

        // when
        var result = await cache.SetRemoveAsync(key, new[] { "item1" }, null, AbortToken);

        // then
        result.Should().Be(1);
        var remaining = await cache.GetSetAsync<string>(key, cancellationToken: AbortToken);
        remaining.Value.Should().BeEquivalentTo(["item2"]);
    }

    [Fact]
    public async Task should_roundtrip_guid_set_through_get_set()
    {
        // Regression: non-string T must still use the object-keyed branch end-to-end.
        // given
        using var cache = _CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };

        // when
        await cache.SetAddAsync(key, ids, TimeSpan.FromMinutes(5), AbortToken);
        var result = await cache.GetSetAsync<Guid>(key, cancellationToken: AbortToken);

        // then
        result.HasValue.Should().BeTrue();
        result.Value.Should().BeEquivalentTo(ids);
    }

    [Fact]
    public async Task should_return_zero_when_removing_from_nonexistent_set()
    {
        // given
        using var cache = _CreateCache();
        var key = Faker.Random.AlphaNumeric(10);

        // when
        var result = await cache.SetRemoveAsync(key, new[] { 1 }, null, AbortToken);

        // then
        result.Should().Be(0);
    }

    [Fact]
    public async Task should_return_zero_when_removing_strings_from_nonexistent_set()
    {
        // Regression: string-branch must hit the TryUpdate no-op path without throwing.
        // given
        using var cache = _CreateCache();
        var key = Faker.Random.AlphaNumeric(10);

        // when
        var result = await cache.SetRemoveAsync(key, new[] { "missing" }, null, AbortToken);

        // then
        result.Should().Be(0);
    }

    [Fact]
    public async Task should_roundtrip_add_then_remove_for_string_set()
    {
        // Regression: explicit add->remove->get path for T=string from the bug report.
        // given
        using var cache = _CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        await cache.SetAddAsync(key, new[] { "a", "b", "c" }, TimeSpan.FromMinutes(5), AbortToken);

        // when
        var removed = await cache.SetRemoveAsync(key, new[] { "a", "c" }, null, AbortToken);
        var remaining = await cache.GetSetAsync<string>(key, cancellationToken: AbortToken);

        // then
        removed.Should().Be(2);
        remaining.HasValue.Should().BeTrue();
        remaining.Value.Should().BeEquivalentTo(["b"]);
    }

    [Fact]
    public async Task should_return_zero_when_adding_only_null_strings_to_set()
    {
        // Regression: previously the buggy string branch would silently count nulls as 1.
        // given
        using var cache = _CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        var items = new string?[] { null, null };

        // when
        var result = await cache.SetAddAsync(key, items!, TimeSpan.FromMinutes(5), AbortToken);

        // then
        result.Should().Be(0);
    }

    [Fact]
    public async Task should_treat_string_set_members_case_sensitively()
    {
        // String members use StringComparer.Ordinal (case-sensitive), matching Redis byte-exact set membership.
        // given
        using var cache = _CreateCache();
        var key = Faker.Random.AlphaNumeric(10);

        // when
        var added = await cache.SetAddAsync(
            key,
            new[] { "Hello", "HELLO", "world" },
            TimeSpan.FromMinutes(5),
            AbortToken
        );
        var result = await cache.GetSetAsync<string>(key, cancellationToken: AbortToken);

        // then
        added.Should().Be(3); // all three are distinct under ordinal comparison
        result.HasValue.Should().BeTrue();
        result.Value.Should().HaveCount(3);
    }

    [Fact]
    public async Task should_page_string_set_items()
    {
        // The string-keyed read branch must honor pageIndex/pageSize like the object-keyed branch.
        // given
        using var cache = _CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        await cache.SetAddAsync(key, new[] { "a", "b", "c", "d", "e" }, TimeSpan.FromMinutes(5), AbortToken);

        // when
        var page = await cache.GetSetAsync<string>(key, pageIndex: 1, pageSize: 2, cancellationToken: AbortToken);

        // then
        page.HasValue.Should().BeTrue();
        page.Value.Should().HaveCount(2);
    }

    #endregion

    #region GetAsync

    [Fact]
    public async Task should_get_cached_value()
    {
        // given
        using var cache = _CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        await cache.UpsertAsync(key, "test-value", TimeSpan.FromMinutes(5), AbortToken);

        // when
        var result = await cache.GetAsync<string>(key, AbortToken);

        // then
        result.HasValue.Should().BeTrue();
        result.Value.Should().Be("test-value");
    }

    [Fact]
    public async Task should_return_no_value_when_key_does_not_exist()
    {
        // given
        using var cache = _CreateCache();
        var key = Faker.Random.AlphaNumeric(10);

        // when
        var result = await cache.GetAsync<string>(key, AbortToken);

        // then
        result.HasValue.Should().BeFalse();
    }

    [Fact]
    public async Task should_return_no_value_when_key_is_expired()
    {
        // given
        using var cache = _CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        await cache.UpsertAsync(key, "test-value", TimeSpan.FromSeconds(1), AbortToken);
        _timeProvider.Advance(TimeSpan.FromSeconds(2));

        // when
        var result = await cache.GetAsync<string>(key, AbortToken);

        // then
        result.HasValue.Should().BeFalse();
    }

    #endregion

    #region GetAllAsync

    [Fact]
    public async Task should_get_all_cached_values()
    {
        // given
        using var cache = _CreateCache();
        var key1 = Faker.Random.AlphaNumeric(10);
        var key2 = Faker.Random.AlphaNumeric(10);
        await cache.UpsertAsync(key1, "value1", TimeSpan.FromMinutes(5), AbortToken);
        await cache.UpsertAsync(key2, "value2", TimeSpan.FromMinutes(5), AbortToken);

        // when
        var result = await cache.GetAllAsync<string>([key1, key2], AbortToken);

        // then
        result.Should().HaveCount(2);
        result[key1].Value.Should().Be("value1");
        result[key2].Value.Should().Be("value2");
    }

    [Fact]
    public async Task should_return_no_value_for_missing_keys()
    {
        // given
        using var cache = _CreateCache();
        var existingKey = Faker.Random.AlphaNumeric(10);
        var missingKey = Faker.Random.AlphaNumeric(10);
        await cache.UpsertAsync(existingKey, "value", TimeSpan.FromMinutes(5), AbortToken);

        // when
        var result = await cache.GetAllAsync<string>([existingKey, missingKey], AbortToken);

        // then
        result[existingKey].HasValue.Should().BeTrue();
        result[missingKey].HasValue.Should().BeFalse();
    }

    #endregion

    #region GetByPrefixAsync

    [Fact]
    public async Task should_get_values_by_prefix()
    {
        // given
        using var cache = _CreateCache();
        var prefix = Faker.Random.AlphaNumeric(5);
        await cache.UpsertAsync($"{prefix}:key1", "value1", TimeSpan.FromMinutes(5), AbortToken);
        await cache.UpsertAsync($"{prefix}:key2", "value2", TimeSpan.FromMinutes(5), AbortToken);
        await cache.UpsertAsync("other:key3", "value3", TimeSpan.FromMinutes(5), AbortToken);

        // when
        var result = await cache.GetByPrefixAsync<string>($"{prefix}:", AbortToken);

        // then
        result.Should().HaveCount(2);
    }

    #endregion

    #region ExistsAsync

    [Fact]
    public async Task should_return_true_when_key_exists()
    {
        // given
        using var cache = _CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        await cache.UpsertAsync(key, "value", TimeSpan.FromMinutes(5), AbortToken);

        // when
        var result = await cache.ExistsAsync(key, AbortToken);

        // then
        result.Should().BeTrue();
    }

    [Fact]
    public async Task should_return_false_when_key_does_not_exist()
    {
        // given
        using var cache = _CreateCache();
        var key = Faker.Random.AlphaNumeric(10);

        // when
        var result = await cache.ExistsAsync(key, AbortToken);

        // then
        result.Should().BeFalse();
    }

    [Fact]
    public async Task should_return_false_when_key_is_expired()
    {
        // given
        using var cache = _CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        await cache.UpsertAsync(key, "value", TimeSpan.FromSeconds(1), AbortToken);
        _timeProvider.Advance(TimeSpan.FromSeconds(2));

        // when
        var result = await cache.ExistsAsync(key, AbortToken);

        // then
        result.Should().BeFalse();
    }

    #endregion

    #region GetCountAsync

    [Fact]
    public async Task should_return_total_count()
    {
        // given
        using var cache = _CreateCache();
        await cache.UpsertAsync("key1", "value1", TimeSpan.FromMinutes(5), AbortToken);
        await cache.UpsertAsync("key2", "value2", TimeSpan.FromMinutes(5), AbortToken);

        // when
        var result = await cache.GetCountAsync(cancellationToken: AbortToken);

        // then
        result.Should().Be(2);
    }

    [Fact]
    public async Task should_return_count_by_prefix()
    {
        // given
        using var cache = _CreateCache();
        var prefix = Faker.Random.AlphaNumeric(5);
        await cache.UpsertAsync($"{prefix}:key1", "value1", TimeSpan.FromMinutes(5), AbortToken);
        await cache.UpsertAsync($"{prefix}:key2", "value2", TimeSpan.FromMinutes(5), AbortToken);
        await cache.UpsertAsync("other:key3", "value3", TimeSpan.FromMinutes(5), AbortToken);

        // when
        var result = await cache.GetCountAsync($"{prefix}:", AbortToken);

        // then
        result.Should().Be(2);
    }

    [Fact]
    public async Task should_exclude_expired_items_from_count()
    {
        // given
        using var cache = _CreateCache();
        await cache.UpsertAsync("key1", "value1", TimeSpan.FromSeconds(1), AbortToken);
        await cache.UpsertAsync("key2", "value2", TimeSpan.FromMinutes(5), AbortToken);
        _timeProvider.Advance(TimeSpan.FromSeconds(2));

        // when
        var result = await cache.GetCountAsync(cancellationToken: AbortToken);

        // then
        result.Should().Be(1);
    }

    #endregion

    #region GetExpirationAsync

    [Fact]
    public async Task should_return_expiration_time()
    {
        // given
        using var cache = _CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        await cache.UpsertAsync(key, "value", TimeSpan.FromMinutes(5), AbortToken);

        // when
        var result = await cache.GetExpirationAsync(key, AbortToken);

        // then
        result.Should().NotBeNull();
        result!.Value.Should().BeCloseTo(TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task should_return_null_when_key_does_not_exist()
    {
        // given
        using var cache = _CreateCache();
        var key = Faker.Random.AlphaNumeric(10);

        // when
        var result = await cache.GetExpirationAsync(key, AbortToken);

        // then
        result.Should().BeNull();
    }

    [Fact]
    public async Task should_return_null_when_no_expiration_set()
    {
        // given
        using var cache = _CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        await cache.UpsertAsync(key, "value", null, AbortToken);

        // when
        var result = await cache.GetExpirationAsync(key, AbortToken);

        // then
        result.Should().BeNull();
    }

    #endregion

    #region RemoveAsync

    [Fact]
    public async Task should_remove_existing_key()
    {
        // given
        using var cache = _CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        await cache.UpsertAsync(key, "value", TimeSpan.FromMinutes(5), AbortToken);

        // when
        var result = await cache.RemoveAsync(key, AbortToken);

        // then
        result.Should().BeTrue();
        (await cache.ExistsAsync(key, AbortToken)).Should().BeFalse();
    }

    [Fact]
    public async Task should_return_false_when_key_does_not_exist_for_remove()
    {
        // given
        using var cache = _CreateCache();
        var key = Faker.Random.AlphaNumeric(10);

        // when
        var result = await cache.RemoveAsync(key, AbortToken);

        // then
        result.Should().BeFalse();
    }

    [Fact]
    public async Task should_return_false_when_key_is_expired_for_remove()
    {
        // given
        using var cache = _CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        await cache.UpsertAsync(key, "value", TimeSpan.FromSeconds(1), AbortToken);
        _timeProvider.Advance(TimeSpan.FromSeconds(2));

        // when
        var result = await cache.RemoveAsync(key, AbortToken);

        // then
        result.Should().BeFalse();
    }

    #endregion

    #region RemoveIfEqualAsync

    [Fact]
    public async Task should_remove_when_value_equals_expected()
    {
        // given
        using var cache = _CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        await cache.UpsertAsync(key, 42, TimeSpan.FromMinutes(5), AbortToken);

        // when
        var result = await cache.RemoveIfEqualAsync(key, 42, AbortToken);

        // then
        result.Should().BeTrue();
    }

    [Fact]
    public async Task should_not_remove_when_value_does_not_equal_expected()
    {
        // given
        using var cache = _CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        await cache.UpsertAsync(key, 42, TimeSpan.FromMinutes(5), AbortToken);

        // when
        var result = await cache.RemoveIfEqualAsync(key, 99, AbortToken);

        // then
        result.Should().BeFalse();
        (await cache.ExistsAsync(key, AbortToken)).Should().BeTrue();
    }

    #endregion

    #region RemoveAllAsync

    [Fact]
    public async Task should_remove_all_specified_keys()
    {
        // given
        using var cache = _CreateCache();
        var key1 = Faker.Random.AlphaNumeric(10);
        var key2 = Faker.Random.AlphaNumeric(10);
        await cache.UpsertAsync(key1, "value1", TimeSpan.FromMinutes(5), AbortToken);
        await cache.UpsertAsync(key2, "value2", TimeSpan.FromMinutes(5), AbortToken);

        // when
        var result = await cache.RemoveAllAsync([key1, key2], AbortToken);

        // then
        result.Should().Be(2);
        (await cache.ExistsAsync(key1, AbortToken)).Should().BeFalse();
        (await cache.ExistsAsync(key2, AbortToken)).Should().BeFalse();
    }

    [Fact]
    public async Task should_handle_nonexistent_keys_in_remove_all()
    {
        // given
        using var cache = _CreateCache();
        var existingKey = Faker.Random.AlphaNumeric(10);
        var nonexistentKey = Faker.Random.AlphaNumeric(10);
        await cache.UpsertAsync(existingKey, "value", TimeSpan.FromMinutes(5), AbortToken);

        // when
        var result = await cache.RemoveAllAsync([existingKey, nonexistentKey], AbortToken);

        // then
        result.Should().Be(1);
    }

    #endregion

    #region RemoveByPrefixAsync

    [Fact]
    public async Task should_remove_all_keys_with_prefix()
    {
        // given
        using var cache = _CreateCache();
        var prefix = Faker.Random.AlphaNumeric(5);
        await cache.UpsertAsync($"{prefix}:key1", "value1", TimeSpan.FromMinutes(5), AbortToken);
        await cache.UpsertAsync($"{prefix}:key2", "value2", TimeSpan.FromMinutes(5), AbortToken);
        await cache.UpsertAsync("other:key3", "value3", TimeSpan.FromMinutes(5), AbortToken);

        // when
        var result = await cache.RemoveByPrefixAsync($"{prefix}:", AbortToken);

        // then
        result.Should().Be(2);
        (await cache.ExistsAsync("other:key3", AbortToken)).Should().BeTrue();
    }

    #endregion

    #region FlushAsync

    [Fact]
    public async Task should_remove_all_keys_on_flush()
    {
        // given
        using var cache = _CreateCache();
        await cache.UpsertAsync("key1", "value1", TimeSpan.FromMinutes(5), AbortToken);
        await cache.UpsertAsync("key2", "value2", TimeSpan.FromMinutes(5), AbortToken);

        // when
        await cache.FlushAsync(AbortToken);

        // then
        var count = await cache.GetCountAsync(cancellationToken: AbortToken);
        count.Should().Be(0);
    }

    [Fact]
    public async Task should_clear_fail_safe_stale_reserve_on_flush()
    {
        // FusionCache parity (CanClearWithFailSafeAsync): flush must wipe the physical fail-safe
        // reserve, not just logically-live entries. A fail-safe entry keeps a physical reserve
        // beyond logical expiry; after flush that reserve must be gone, so a fail-safe read whose
        // factory throws gets nothing instead of serving the stale value.
        // given
        using var cache = _CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        // entry already past logical expiry but still within the physical fail-safe reserve
        await ((IFactoryCacheStore)cache).SetEntryAsync(
            key,
            new CacheStoreEntryWrite<int>
            {
                Value = 1,
                IsNull = false,
                LogicalExpiresAt = now.AddMinutes(-1),
                PhysicalExpiresAt = now.AddMinutes(10),
            },
            AbortToken
        );

        var failSafeOptions = new CacheEntryOptions
        {
            Duration = TimeSpan.FromMinutes(1),
            IsFailSafeEnabled = true,
            FailSafeMaxDuration = TimeSpan.FromMinutes(10),
            FailSafeThrottleDuration = TimeSpan.FromSeconds(30),
        };

        // sanity: the stale reserve serves the old value before the flush
        var beforeFlush = await cache.GetOrAddAsync<int>(
            key,
            _ => throw new InvalidOperationException("factory failed"),
            failSafeOptions,
            AbortToken
        );
        beforeFlush.IsStale.Should().BeTrue();
        beforeFlush.Value.Should().Be(1);

        // re-seed the stale reserve (the read above may have refreshed logical expiry)
        await ((IFactoryCacheStore)cache).SetEntryAsync(
            key,
            new CacheStoreEntryWrite<int>
            {
                Value = 1,
                IsNull = false,
                LogicalExpiresAt = now.AddMinutes(-1),
                PhysicalExpiresAt = now.AddMinutes(10),
            },
            AbortToken
        );

        // when
        await cache.FlushAsync(AbortToken);

        // then — reserve is gone: no entry, and a throwing fail-safe factory cannot serve the old value
        (await cache.ExistsAsync(key, AbortToken))
            .Should()
            .BeFalse();
        (await cache.GetCountAsync(cancellationToken: AbortToken)).Should().Be(0);

        var afterFlush = await cache.GetOrAddAsync(key, _ => ValueTask.FromResult(2), failSafeOptions, AbortToken);
        afterFlush.IsStale.Should().BeFalse("flush wiped the stale reserve, so the factory runs fresh");
        afterFlush.Value.Should().Be(2);
    }

    [Fact]
    public async Task should_not_write_cache_when_reading_a_missing_key()
    {
        // FusionCache parity (GetOrDefaultDoesNotSetAsync): a plain read never writes the cache.
        // Reading a missing key must not materialize an entry.
        // given
        using var cache = _CreateCache();
        var key = Faker.Random.AlphaNumeric(10);

        // when
        var result = await cache.GetAsync<int>(key, AbortToken);

        // then
        result.HasValue.Should().BeFalse();
        (await cache.ExistsAsync(key, AbortToken)).Should().BeFalse();
        (await cache.GetCountAsync(cancellationToken: AbortToken)).Should().Be(0);
    }

    #endregion

    #region Expiration Behavior

    [Fact]
    public async Task should_expire_item_after_expiration_time()
    {
        // given
        using var cache = _CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        await cache.UpsertAsync(key, "value", TimeSpan.FromMinutes(1), AbortToken);

        // when
        _timeProvider.Advance(TimeSpan.FromMinutes(2));

        // then
        var result = await cache.GetAsync<string>(key, AbortToken);
        result.HasValue.Should().BeFalse();
    }

    [Fact]
    public async Task should_not_expire_item_before_expiration_time()
    {
        // given
        using var cache = _CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        await cache.UpsertAsync(key, "value", TimeSpan.FromMinutes(5), AbortToken);

        // when
        _timeProvider.Advance(TimeSpan.FromMinutes(4));

        // then
        var result = await cache.GetAsync<string>(key, AbortToken);
        result.HasValue.Should().BeTrue();
    }

    [Fact]
    public async Task should_treat_entry_as_expired_at_exact_expiration_tick()
    {
        // Pins the `<= now` boundary: an entry whose PhysicalExpiresAt == GetUtcNow() is expired,
        // not alive. If the check regresses to `< now` (strict less-than) this test will fail
        // because the entry would still be returned as a hit at the exact tick.

        // given
        using var cache = _CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        var duration = TimeSpan.FromMinutes(5);

        await cache.UpsertAsync(key, "value", duration, TestContext.Current.CancellationToken);

        // when — advance by exactly the duration so GetUtcNow() == PhysicalExpiresAt
        _timeProvider.Advance(duration);

        // then — entry is expired AT the exact tick (inclusive boundary)
        var result = await cache.GetAsync<string>(key, TestContext.Current.CancellationToken);
        result.HasValue.Should().BeFalse("entry must be expired when now == expiresAt (inclusive boundary)");
    }

    [Fact]
    public async Task should_return_value_one_tick_before_expiration()
    {
        // Sibling to the at-tick test: ensures the >= boundary does not over-expire.
        // An entry whose PhysicalExpiresAt is one tick in the future must still be a hit.
        // If the check incorrectly uses `<= now` for a time strictly before expiry this would fail,
        // but that is an impossible regression; the test's real value is documenting the just-before boundary.

        // given
        using var cache = _CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        var duration = TimeSpan.FromMinutes(5);

        await cache.UpsertAsync(key, "value", duration, TestContext.Current.CancellationToken);

        // when — advance to one tick before expiration
        _timeProvider.Advance(duration - TimeSpan.FromTicks(1));

        // then — entry is still alive one tick before expiry
        var result = await cache.GetAsync<string>(key, TestContext.Current.CancellationToken);
        result.HasValue.Should().BeTrue("entry must be alive one tick before expiration");
        result.Value.Should().Be("value");
    }

    #endregion

    #region MaxItems/LRU Eviction

    [Fact]
    public async Task should_evict_lru_item_when_max_items_exceeded()
    {
        // given
        var options = new InMemoryCacheOptions { MaxItems = 3 };
        using var cache = _CreateCache(options);

        // Add items with time advancement to create clear LRU order
        await cache.UpsertAsync("key1", "value1", TimeSpan.FromMinutes(5), AbortToken);
        _timeProvider.Advance(TimeSpan.FromMilliseconds(10));
        await cache.UpsertAsync("key2", "value2", TimeSpan.FromMinutes(5), AbortToken);
        _timeProvider.Advance(TimeSpan.FromMilliseconds(10));
        await cache.UpsertAsync("key3", "value3", TimeSpan.FromMinutes(5), AbortToken);
        _timeProvider.Advance(TimeSpan.FromMilliseconds(10));

        // when - add 4th item
        await cache.UpsertAsync("key4", "value4", TimeSpan.FromMinutes(5), AbortToken);

        // Wait briefly to allow async eviction to complete
        await Task.Delay(100, AbortToken);

        // then - key1 (oldest) should be evicted
        var count = await cache.GetCountAsync(cancellationToken: AbortToken);
        count.Should().BeLessThanOrEqualTo(3);
    }

    [Fact]
    public async Task should_update_access_time_on_get()
    {
        // given
        var options = new InMemoryCacheOptions { MaxItems = 3 };
        using var cache = _CreateCache(options);

        await cache.UpsertAsync("key1", "value1", TimeSpan.FromMinutes(5), AbortToken);
        _timeProvider.Advance(TimeSpan.FromMilliseconds(10));
        await cache.UpsertAsync("key2", "value2", TimeSpan.FromMinutes(5), AbortToken);
        _timeProvider.Advance(TimeSpan.FromMilliseconds(10));
        await cache.UpsertAsync("key3", "value3", TimeSpan.FromMinutes(5), AbortToken);
        _timeProvider.Advance(TimeSpan.FromMilliseconds(10));

        // Access key1 to make it recently used
        await cache.GetAsync<string>("key1", AbortToken);
        _timeProvider.Advance(TimeSpan.FromMilliseconds(10));

        // when - add 4th item
        await cache.UpsertAsync("key4", "value4", TimeSpan.FromMinutes(5), AbortToken);
        await Task.Delay(100, AbortToken);

        // then - key1 should still exist (was recently accessed)
        var key1Exists = await cache.ExistsAsync("key1", AbortToken);
        key1Exists.Should().BeTrue();
    }

    // #31 — Concurrent LRU eviction stress: 50+ parallel tasks inserting random entries into a capacity-capped
    // InMemoryCache (small MaxItems). After Task.WhenAll, CurrentMemorySize must be >= 0 and must equal the
    // sum of sizes of surviving (live, non-expired) entries. Verifies that Interlocked.Add across 10+ call
    // sites never leaves _currentMemorySize negative or inconsistent under contention.
    [Fact]
    public async Task should_keep_current_memory_size_non_negative_and_consistent_under_concurrent_lru_eviction()
    {
        // given — small MaxItems cap (10) to provoke heavy eviction under concurrent writes
        const int maxItems = 10;
        const long entrySize = 100;
        const int parallelTasks = 60;
        var options = new InMemoryCacheOptions
        {
            MaxItems = maxItems,
            MaxMemorySize = maxItems * entrySize,
            SizeCalculator = _ => entrySize,
        };
        using var cache = _CreateCache(options);

        // when — 60 tasks each insert a unique random-key entry concurrently
        var tasks = Enumerable
            .Range(0, parallelTasks)
            .Select(i =>
                Task.Run(async () =>
                {
                    var key = Faker.Random.AlphaNumeric(20) + i.ToString(CultureInfo.InvariantCulture);
                    await cache.UpsertAsync(key, Faker.Random.AlphaNumeric(5), TimeSpan.FromMinutes(5));
                })
            )
            .ToArray();

        await Task.WhenAll(tasks);

        // Allow LRU background eviction to settle (eviction is async maintenance)
        await Task.Delay(200, AbortToken);

        // then — CurrentMemorySize must be non-negative
        var reportedSize = cache.CurrentMemorySize;
        reportedSize
            .Should()
            .BeGreaterThanOrEqualTo(0, "Interlocked.Add across concurrent evictions must never go negative");

        // CurrentMemorySize must equal the sum of live surviving entries' sizes
        // (surviving = not yet evicted; each entry has size = entrySize)
        var survivingCount = await cache.GetCountAsync(cancellationToken: AbortToken);
        var expectedSize = survivingCount * entrySize;
        reportedSize
            .Should()
            .Be(
                expectedSize,
                $"CurrentMemorySize ({reportedSize}) must equal surviving entries ({survivingCount}) × entry size ({entrySize})"
            );

        // surviving count must not exceed the cap
        survivingCount.Should().BeLessThanOrEqualTo(maxItems);
    }

    #endregion

    #region CloneValues Option

    [Fact]
    public async Task should_clone_values_when_option_enabled()
    {
        // given
        var options = new InMemoryCacheOptions { CloneValues = true };
        using var cache = _CreateCache(options);
        var key = Faker.Random.AlphaNumeric(10);
        var original = new TestClass { Value = 1 };
        await cache.UpsertAsync(key, original, TimeSpan.FromMinutes(5), AbortToken);

        // when - modify original
        original.Value = 2;

        // then - cached value should be unchanged
        var cached = await cache.GetAsync<TestClass>(key, AbortToken);
        cached.Value!.Value.Should().Be(1);
    }

    [Fact]
    public async Task should_not_clone_values_when_option_disabled()
    {
        // given
        var options = new InMemoryCacheOptions { CloneValues = false };
        using var cache = _CreateCache(options);
        var key = Faker.Random.AlphaNumeric(10);
        var original = new TestClass { Value = 1 };
        await cache.UpsertAsync(key, original, TimeSpan.FromMinutes(5), AbortToken);

        // when - modify original
        original.Value = 2;

        // then - cached value should also be modified (same reference)
        var cached = await cache.GetAsync<TestClass>(key, AbortToken);
        cached.Value!.Value.Should().Be(2);
    }

    [Fact]
    public async Task should_return_different_instance_on_each_get_when_clone_enabled()
    {
        // given
        var options = new InMemoryCacheOptions { CloneValues = true };
        using var cache = _CreateCache(options);
        var key = Faker.Random.AlphaNumeric(10);
        await cache.UpsertAsync(key, new TestClass { Value = 1 }, TimeSpan.FromMinutes(5), AbortToken);

        // when
        var result1 = await cache.GetAsync<TestClass>(key, AbortToken);
        var result2 = await cache.GetAsync<TestClass>(key, AbortToken);

        // then
        ReferenceEquals(result1.Value, result2.Value).Should().BeFalse();
    }

    [Fact]
    public async Task should_clone_get_or_add_hits_when_option_enabled()
    {
        // given
        var options = new InMemoryCacheOptions { CloneValues = true };
        using var cache = _CreateCache(options);
        var key = Faker.Random.AlphaNumeric(10);
        await cache.UpsertAsync(key, new TestClass { Value = 1 }, TimeSpan.FromMinutes(5), AbortToken);

        // when
        var first = await cache.GetOrAddAsync<TestClass>(
            key,
            _ => ValueTask.FromResult<TestClass?>(new TestClass { Value = 2 }),
            TimeSpan.FromMinutes(5),
            AbortToken
        );
        var second = await cache.GetOrAddAsync<TestClass>(
            key,
            _ => ValueTask.FromResult<TestClass?>(new TestClass { Value = 3 }),
            TimeSpan.FromMinutes(5),
            AbortToken
        );

        first.Value!.Value = 99;

        // then
        ReferenceEquals(first.Value, second.Value).Should().BeFalse();
        second.Value!.Value.Should().Be(1);
    }

    [Fact]
    public async Task should_clone_failsafe_stale_value_when_option_enabled()
    {
        // given
        var options = new InMemoryCacheOptions { CloneValues = true };
        using var cache = _CreateCache(options);
        var key = Faker.Random.AlphaNumeric(10);
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        await ((IFactoryCacheStore)cache).SetEntryAsync(
            key,
            new CacheStoreEntryWrite<TestClass>
            {
                Value = new TestClass { Value = 1 },
                IsNull = false,
                LogicalExpiresAt = now.AddMinutes(-1),
                PhysicalExpiresAt = now.AddMinutes(5),
            },
            AbortToken
        );

        var failSafeOptions = new CacheEntryOptions
        {
            Duration = TimeSpan.FromMinutes(1),
            IsFailSafeEnabled = true,
            FailSafeMaxDuration = TimeSpan.FromMinutes(10),
            FailSafeThrottleDuration = TimeSpan.FromSeconds(30),
        };

        // when
        var stale = await cache.GetOrAddAsync<TestClass>(
            key,
            _ => throw new InvalidOperationException("factory failed"),
            failSafeOptions,
            AbortToken
        );
        stale.Value!.Value = 99;
        var cached = await cache.GetOrAddAsync<TestClass>(
            key,
            _ => ValueTask.FromResult<TestClass?>(new TestClass { Value = 2 }),
            failSafeOptions,
            AbortToken
        );

        // then
        stale.IsStale.Should().BeTrue();
        cached.Value!.Value.Should().Be(1);
    }

    [Fact]
    public async Task should_handle_reference_equality_limitation_when_clone_enabled()
    {
        // given
        var options = new InMemoryCacheOptions { CloneValues = true };
        using var cache = _CreateCache(options);
        var key = Faker.Random.AlphaNumeric(10);
        var original = new TestClass { Value = 42 };
        await cache.UpsertAsync(key, original, TimeSpan.FromMinutes(5), AbortToken);

        // when
        // even though 'original' has the same values, it is a different instance than what's in cache
        // because the cache stores a deep-cloned copy. object.Equals will fail.
        var result = await cache.TryReplaceIfEqualAsync(
            key,
            original,
            new TestClass { Value = 99 },
            TimeSpan.FromMinutes(5),
            AbortToken
        );

        // then
        result.Should().BeFalse("reference equality fails for cloned custom classes that don't override Equals");
    }

    [Fact]
    public async Task should_round_trip_entry_metadata_through_factory_store()
    {
        // given
        using var cache = _CreateCache();
        var store = (IFactoryCacheStore)cache;
        var key = Faker.Random.AlphaNumeric(10);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var entry = new CacheStoreEntryWrite<string>
        {
            Value = "value",
            IsNull = false,
            LogicalExpiresAt = now.AddMinutes(5),
            PhysicalExpiresAt = now.AddMinutes(10),
            EagerRefreshAt = now.AddMinutes(4),
            ETag = "W/\"v42\"",
            LastModifiedAt = now.AddMinutes(-30),
            Tags = ["tenant:1", "products"],
        };

        // when
        await store.SetEntryAsync(key, in entry, AbortToken);
        var roundTripped = await store.TryGetEntryAsync<string>(key, AbortToken);

        // then
        roundTripped.Found.Should().BeTrue();
        roundTripped.Value.Should().Be("value");
        roundTripped.LogicalExpiresAt.Should().Be(entry.LogicalExpiresAt);
        roundTripped.PhysicalExpiresAt.Should().Be(entry.PhysicalExpiresAt);
        roundTripped.EagerRefreshAt.Should().Be(entry.EagerRefreshAt);
        roundTripped.ETag.Should().Be(entry.ETag);
        roundTripped.LastModifiedAt.Should().Be(entry.LastModifiedAt);
        roundTripped.Tags.Should().BeEquivalentTo("tenant:1", "products");
    }

    [Fact]
    public async Task should_not_corrupt_memory_size_on_failed_replace_with_cloning()
    {
        // given
        var options = new InMemoryCacheOptions
        {
            CloneValues = true,
            SizeCalculator = _ => 100,
            MaxMemorySize = 1000,
        };
        using var cache = _CreateCache(options);
        var key = Faker.Random.AlphaNumeric(10);
        await cache.UpsertAsync(key, new TestClass { Value = 1 }, TimeSpan.FromMinutes(5), AbortToken);
        var initialSize = cache.CurrentMemorySize;

        // when - failed replacement (wrong expected value)
        await cache.TryReplaceIfEqualAsync(
            key,
            new TestClass { Value = 99 },
            new TestClass { Value = 2 },
            TimeSpan.FromMinutes(5),
            AbortToken
        );

        // then - size should still be exactly 100
        cache.CurrentMemorySize.Should().Be(initialSize);
    }

    #endregion

    #region KeyPrefix Handling

    [Fact]
    public async Task should_prefix_keys_when_key_prefix_is_set()
    {
        // given
        const string prefix = "myapp:";
        var options = new InMemoryCacheOptions { KeyPrefix = prefix };
        using var cache = _CreateCache(options);
        var key = Faker.Random.AlphaNumeric(10);

        // when
        await cache.UpsertAsync(key, "value", TimeSpan.FromMinutes(5), AbortToken);

        // then - key should be accessible via original key
        var result = await cache.GetAsync<string>(key, AbortToken);
        result.HasValue.Should().BeTrue();
    }

    [Fact]
    public async Task should_use_prefix_for_all_operations()
    {
        // given
        const string prefix = "myapp:";
        var options = new InMemoryCacheOptions { KeyPrefix = prefix };
        using var cache = _CreateCache(options);
        var key = Faker.Random.AlphaNumeric(10);
        await cache.UpsertAsync(key, "value", TimeSpan.FromMinutes(5), AbortToken);

        // when
        var exists = await cache.ExistsAsync(key, AbortToken);
        var removed = await cache.RemoveAsync(key, AbortToken);

        // then
        exists.Should().BeTrue();
        removed.Should().BeTrue();
    }

    #endregion

    #region Complex Type Support

    [Theory]
    [InlineData(42)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public async Task should_convert_boxed_simple_value_to_concrete_int_on_read(int boxedInt)
    {
        // FusionCache parity (HandlesFlexibleSimpleTypeConversionsAsync): a value written boxed as
        // object round-trips to its concrete type on read. Headless only tested the failure side
        // (should_throw_on_invalid_type_conversion...); this pins the success round-trip.
        // given
        using var cache = _CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        object boxed = boxedInt;

        // when
        await cache.UpsertAsync(key, boxed, TimeSpan.FromMinutes(5), AbortToken);
        var result = await cache.GetAsync<int>(key, AbortToken);

        // then
        result.HasValue.Should().BeTrue();
        result.Value.Should().Be(boxedInt);
    }

    [Fact]
    public async Task should_convert_boxed_simple_values_of_various_types_on_read()
    {
        // Covers long/bool/string in addition to the int case above.
        // given
        using var cache = _CreateCache();
        var longKey = Faker.Random.AlphaNumeric(10);
        var boolKey = Faker.Random.AlphaNumeric(10);
        var stringKey = Faker.Random.AlphaNumeric(10);

        // when
        await cache.UpsertAsync<object>(longKey, 9_000_000_000L, TimeSpan.FromMinutes(5), AbortToken);
        await cache.UpsertAsync<object>(boolKey, true, TimeSpan.FromMinutes(5), AbortToken);
        await cache.UpsertAsync<object>(stringKey, "boxed-string", TimeSpan.FromMinutes(5), AbortToken);

        // then
        (await cache.GetAsync<long>(longKey, AbortToken))
            .Value.Should()
            .Be(9_000_000_000L);
        (await cache.GetAsync<bool>(boolKey, AbortToken)).Value.Should().BeTrue();
        (await cache.GetAsync<string>(stringKey, AbortToken)).Value.Should().Be("boxed-string");
    }

    [Fact]
    public async Task should_convert_boxed_complex_value_to_concrete_type_on_read()
    {
        // FusionCache parity (HandlesFlexibleComplexTypeConversionsAsync): a complex object written
        // boxed as object reads back as its concrete type with fields intact. Distinct from the typed
        // should_cache_complex_objects — this exercises the boxed-object -> concrete path.
        // given
        using var cache = _CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        object boxed = new TestClass { Value = 1234 };

        // when
        await cache.UpsertAsync(key, boxed, TimeSpan.FromMinutes(5), AbortToken);
        var result = await cache.GetAsync<TestClass>(key, AbortToken);

        // then
        result.HasValue.Should().BeTrue();
        result.Value!.Value.Should().Be(1234);
    }

    [Fact]
    public async Task should_cache_complex_objects()
    {
        // given
        using var cache = _CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        var value = new TestClass { Value = 42 };

        // when
        await cache.UpsertAsync(key, value, TimeSpan.FromMinutes(5), AbortToken);
        var result = await cache.GetAsync<TestClass>(key, AbortToken);

        // then
        result.HasValue.Should().BeTrue();
        result.Value!.Value.Should().Be(42);
    }

    [Fact]
    public async Task should_cache_collections()
    {
        // given
        using var cache = _CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        var value = new List<int> { 1, 2, 3 };

        // when
        await cache.UpsertAsync(key, value, TimeSpan.FromMinutes(5), AbortToken);
        var result = await cache.GetAsync<List<int>>(key, AbortToken);

        // then
        result.HasValue.Should().BeTrue();
        result.Value.Should().BeEquivalentTo([1, 2, 3]);
    }

    [Fact]
    public async Task should_cache_dictionaries()
    {
        // given
        using var cache = _CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        var value = new Dictionary<string, int>(StringComparer.Ordinal) { ["a"] = 1, ["b"] = 2 };

        // when
        await cache.UpsertAsync(key, value, TimeSpan.FromMinutes(5), AbortToken);
        var result = await cache.GetAsync<Dictionary<string, int>>(key, AbortToken);

        // then
        result.HasValue.Should().BeTrue();
        result
            .Value.Should()
            .BeEquivalentTo(new Dictionary<string, int>(StringComparer.Ordinal) { ["a"] = 1, ["b"] = 2 });
    }

    #endregion

    #region Dispose

    [Fact]
    public async Task should_clear_cache_on_dispose()
    {
        // given
        var cache = _CreateCache();
        await cache.UpsertAsync("key", "value", TimeSpan.FromMinutes(5), AbortToken);

        // when
        cache.Dispose();

        // then - accessing after dispose should handle gracefully
        // The cache clears on dispose
    }

    [Fact]
    public void should_be_idempotent_on_multiple_dispose()
    {
        // given
        var cache = _CreateCache();

        // when & then - should not throw
        cache.Dispose();
        cache.Dispose();
    }

    #endregion

    #region Memory Management

    [Fact]
    public void should_throw_when_max_memory_size_without_size_calculator()
    {
        // given
        var options = new InMemoryCacheOptions { MaxMemorySize = 1024 };

        // when
        var act = () => _CreateCache(options);

        // then
        act.Should().Throw<ArgumentException>().WithMessage("*SizeCalculator*");
    }

    [Fact]
    public void should_throw_when_max_entry_size_without_size_calculator()
    {
        // given
        var options = new InMemoryCacheOptions { MaxEntrySize = 100 };

        // when
        var act = () => _CreateCache(options);

        // then
        act.Should().Throw<ArgumentException>().WithMessage("*SizeCalculator*");
    }

    [Fact]
    public async Task should_track_memory_size_on_upsert()
    {
        // given
        var options = new InMemoryCacheOptions { MaxMemorySize = 10000, SizeCalculator = _ => 100 };
        using var cache = _CreateCache(options);

        // when
        await cache.UpsertAsync("key1", "value1", TimeSpan.FromMinutes(5), AbortToken);
        await cache.UpsertAsync("key2", "value2", TimeSpan.FromMinutes(5), AbortToken);

        // then
        cache.CurrentMemorySize.Should().Be(200);
    }

    [Fact]
    public async Task should_update_memory_size_on_replace()
    {
        // given
        var sizeMap = new Dictionary<string, long>(StringComparer.Ordinal) { ["small"] = 50, ["large"] = 150 };
        var options = new InMemoryCacheOptions
        {
            MaxMemorySize = 10000,
            SizeCalculator = v => v is string s ? sizeMap.GetValueOrDefault(s, 100) : 100,
        };
        using var cache = _CreateCache(options);
        await cache.UpsertAsync("key1", "small", TimeSpan.FromMinutes(5), AbortToken);
        var initialSize = cache.CurrentMemorySize;

        // when
        await cache.UpsertAsync("key1", "large", TimeSpan.FromMinutes(5), AbortToken);

        // then
        cache.CurrentMemorySize.Should().Be(initialSize + 100); // 150 - 50 = 100 delta
    }

    [Fact]
    public async Task should_decrease_memory_size_on_remove()
    {
        // given
        var options = new InMemoryCacheOptions { MaxMemorySize = 10000, SizeCalculator = _ => 100 };
        using var cache = _CreateCache(options);
        await cache.UpsertAsync("key1", "value1", TimeSpan.FromMinutes(5), AbortToken);
        await cache.UpsertAsync("key2", "value2", TimeSpan.FromMinutes(5), AbortToken);

        // when
        await cache.RemoveAsync("key1", AbortToken);

        // then
        cache.CurrentMemorySize.Should().Be(100);
    }

    [Fact]
    public async Task should_reset_memory_size_on_flush()
    {
        // given
        var options = new InMemoryCacheOptions { MaxMemorySize = 10000, SizeCalculator = _ => 100 };
        using var cache = _CreateCache(options);
        await cache.UpsertAsync("key1", "value1", TimeSpan.FromMinutes(5), AbortToken);
        await cache.UpsertAsync("key2", "value2", TimeSpan.FromMinutes(5), AbortToken);

        // when
        await cache.FlushAsync(AbortToken);

        // then
        cache.CurrentMemorySize.Should().Be(0);
    }

    [Fact]
    public async Task should_evict_when_max_memory_exceeded()
    {
        // given
        var options = new InMemoryCacheOptions
        {
            MaxMemorySize = 250, // Only allows ~2 entries
            SizeCalculator = _ => 100,
        };
        using var cache = _CreateCache(options);

        // when
        await cache.UpsertAsync("key1", "value1", TimeSpan.FromMinutes(5), AbortToken);
        _timeProvider.Advance(TimeSpan.FromMilliseconds(10));
        await cache.UpsertAsync("key2", "value2", TimeSpan.FromMinutes(5), AbortToken);
        _timeProvider.Advance(TimeSpan.FromMilliseconds(10));
        await cache.UpsertAsync("key3", "value3", TimeSpan.FromMinutes(5), AbortToken);

        // Allow compaction to run
        await Task.Delay(100, AbortToken);

        // then
        cache.CurrentMemorySize.Should().BeLessThanOrEqualTo(250);
    }

    [Fact]
    public async Task should_skip_entry_when_exceeds_max_entry_size()
    {
        // given
        var options = new InMemoryCacheOptions
        {
            MaxEntrySize = 50,
            SizeCalculator = _ => 100, // All entries are 100 bytes
        };
        using var cache = _CreateCache(options);

        // when
        var result = await cache.UpsertAsync("key1", "value1", TimeSpan.FromMinutes(5), AbortToken);

        // then
        result.Should().BeFalse();
        (await cache.ExistsAsync("key1", AbortToken)).Should().BeFalse();
    }

    [Fact]
    public async Task should_throw_when_entry_exceeds_max_entry_size_and_throw_enabled()
    {
        // given
        var options = new InMemoryCacheOptions
        {
            MaxEntrySize = 50,
            SizeCalculator = _ => 100,
            ShouldThrowOnMaxEntrySizeExceeded = true,
        };
        using var cache = _CreateCache(options);

        // when
        var act = async () => await cache.UpsertAsync("key1", "value1", TimeSpan.FromMinutes(5), AbortToken);

        // then
        await act.Should().ThrowAsync<MaxEntrySizeExceededException>();
    }

    [Fact]
    public async Task should_allow_entry_when_within_max_entry_size()
    {
        // given
        var options = new InMemoryCacheOptions { MaxEntrySize = 150, SizeCalculator = _ => 100 };
        using var cache = _CreateCache(options);

        // when
        var result = await cache.UpsertAsync("key1", "value1", TimeSpan.FromMinutes(5), AbortToken);

        // then
        result.Should().BeTrue();
        (await cache.ExistsAsync("key1", AbortToken)).Should().BeTrue();
    }

    [Fact]
    public async Task should_track_memory_with_fixed_sizing()
    {
        // given - fixed sizing where each entry is 100 bytes
        var options = new InMemoryCacheOptions
        {
            MaxMemorySize = 1000,
            SizeCalculator = _ => 100, // Fixed size
        };
        using var cache = _CreateCache(options);

        // when
        for (var i = 0; i < 5; i++)
        {
            await cache.UpsertAsync($"key{i}", $"value{i}", TimeSpan.FromMinutes(5), AbortToken);
        }

        // then
        cache.CurrentMemorySize.Should().Be(500);
    }

    [Fact]
    public async Task should_track_memory_with_dynamic_sizing()
    {
        // given - dynamic sizing based on string length
        var options = new InMemoryCacheOptions
        {
            MaxMemorySize = 10000,
            SizeCalculator = v => v is string s ? s.Length * 2 : 100, // Simple size calculation
        };
        using var cache = _CreateCache(options);

        // when
        await cache.UpsertAsync("key1", "short", TimeSpan.FromMinutes(5), AbortToken); // 5*2 = 10
        await cache.UpsertAsync("key2", "longer", TimeSpan.FromMinutes(5), AbortToken); // 6*2 = 12

        // then
        cache.CurrentMemorySize.Should().Be(22);
    }

    [Fact]
    public async Task should_handle_negative_size_from_calculator()
    {
        // given - calculator that returns negative for some values
        var options = new InMemoryCacheOptions
        {
            MaxMemorySize = 10000,
            SizeCalculator = v => v is string s && string.Equals(s, "skip", StringComparison.Ordinal) ? -1 : 100,
        };
        using var cache = _CreateCache(options);

        // when
        await cache.UpsertAsync("key1", "skip", TimeSpan.FromMinutes(5), AbortToken);
        await cache.UpsertAsync("key2", "keep", TimeSpan.FromMinutes(5), AbortToken);

        // then - negative sizes are treated as 0
        cache.CurrentMemorySize.Should().Be(100); // Only key2 counted
    }

    [Fact]
    public async Task should_track_memory_on_try_insert()
    {
        // given
        var options = new InMemoryCacheOptions { MaxMemorySize = 10000, SizeCalculator = _ => 100 };
        using var cache = _CreateCache(options);

        // when
        await cache.TryInsertAsync("key1", "value1", TimeSpan.FromMinutes(5), AbortToken);

        // then
        cache.CurrentMemorySize.Should().Be(100);
    }

    [Fact]
    public async Task should_not_track_memory_on_failed_try_insert()
    {
        // given
        var options = new InMemoryCacheOptions { MaxMemorySize = 10000, SizeCalculator = _ => 100 };
        using var cache = _CreateCache(options);
        await cache.UpsertAsync("key1", "existing", TimeSpan.FromMinutes(5), AbortToken);

        // when
        await cache.TryInsertAsync("key1", "new", TimeSpan.FromMinutes(5), AbortToken);

        // then - size should remain unchanged
        cache.CurrentMemorySize.Should().Be(100);
    }

    [Fact]
    public async Task should_track_memory_on_increment()
    {
        // given
        var options = new InMemoryCacheOptions
        {
            MaxMemorySize = 10000,
            SizeCalculator = _ => 8, // Size of a long/double
        };
        using var cache = _CreateCache(options);

        // when
        await cache.IncrementAsync("counter", 1L, TimeSpan.FromMinutes(5), AbortToken);

        // then
        cache.CurrentMemorySize.Should().Be(8);
    }

    [Fact]
    public async Task should_track_memory_on_set_add()
    {
        // given
        var options = new InMemoryCacheOptions { MaxMemorySize = 10000, SizeCalculator = _ => 50 };
        using var cache = _CreateCache(options);

        // when
        await cache.SetAddAsync("set1", new[] { 1, 2, 3 }, TimeSpan.FromMinutes(5), AbortToken);

        // then
        cache.CurrentMemorySize.Should().Be(50);
    }

    [Fact]
    public async Task should_reset_memory_on_dispose()
    {
        // given
        var options = new InMemoryCacheOptions { MaxMemorySize = 10000, SizeCalculator = _ => 100 };
        var cache = _CreateCache(options);
        await cache.UpsertAsync("key1", "value1", TimeSpan.FromMinutes(5), AbortToken);

        // when
        cache.Dispose();

        // then
        cache.CurrentMemorySize.Should().Be(0);
    }

    [Fact]
    public async Task should_prefer_larger_items_for_eviction_when_memory_constrained()
    {
        // given - cache with memory constraint
        var sizeMap = new Dictionary<string, long>(StringComparer.Ordinal)
        {
            ["small1"] = 50,
            ["small2"] = 50,
            ["large"] = 200,
        };
        var options = new InMemoryCacheOptions
        {
            MaxMemorySize = 250, // Will trigger eviction when adding large
            SizeCalculator = v => v is string s ? sizeMap.GetValueOrDefault(s, 100) : 100,
        };
        using var cache = _CreateCache(options);

        // Add entries with time gaps
        await cache.UpsertAsync("key1", "small1", TimeSpan.FromMinutes(5), AbortToken);
        _timeProvider.Advance(TimeSpan.FromMilliseconds(10));
        await cache.UpsertAsync("key2", "small2", TimeSpan.FromMinutes(5), AbortToken);
        _timeProvider.Advance(TimeSpan.FromMilliseconds(10));

        // when - add large entry that exceeds limit
        await cache.UpsertAsync("key3", "large", TimeSpan.FromMinutes(5), AbortToken);
        await Task.Delay(100, AbortToken); // Allow compaction

        // then - eviction should have occurred
        cache.CurrentMemorySize.Should().BeLessThanOrEqualTo(250);
    }

    #endregion

    #region Serialization Error Handling

    [Fact]
    public async Task should_return_no_value_on_get_when_serialization_error_and_throw_disabled()
    {
        // given
        var options = new InMemoryCacheOptions
        {
            CloneValues = false, // Don't clone on store
            ShouldThrowOnSerializationError = false,
        };
        using var cache = _CreateCache(options);

        // Store a value that will fail serialization on get
        await cache.UpsertAsync("key1", 42, TimeSpan.FromMinutes(5), AbortToken);

        // when - try to get as wrong type (which may cause serialization issues)
        var result = await cache.GetAsync<TestClass>("key1", AbortToken);

        // then - should return NoValue instead of throwing
        result.HasValue.Should().BeFalse();
    }

    [Fact]
    public async Task should_throw_on_invalid_type_conversion_when_throw_enabled()
    {
        // given
        var options = new InMemoryCacheOptions { CloneValues = false, ShouldThrowOnSerializationError = true };
        using var cache = _CreateCache(options);
        await cache.UpsertAsync("key1", 42, TimeSpan.FromMinutes(5), AbortToken);

        // when - try to get as wrong type
        var act = async () => await cache.GetAsync<TestClass>("key1", AbortToken);

        // then - should throw
        await act.Should().ThrowAsync<Exception>();
    }

    #endregion

    #region Case Sensitivity

    [Fact]
    public async Task should_treat_keys_as_case_sensitive()
    {
        // given
        using var cache = _CreateCache();

        // when
        await cache.UpsertAsync("test", "lowercase", TimeSpan.FromMinutes(5), AbortToken);
        await cache.UpsertAsync("Test", "capitalized", TimeSpan.FromMinutes(5), AbortToken);
        await cache.UpsertAsync("TEST", "uppercase", TimeSpan.FromMinutes(5), AbortToken);

        // then
        (await cache.GetAsync<string>("test", AbortToken))
            .Value.Should()
            .Be("lowercase");
        (await cache.GetAsync<string>("Test", AbortToken)).Value.Should().Be("capitalized");
        (await cache.GetAsync<string>("TEST", AbortToken)).Value.Should().Be("uppercase");
    }

    #endregion

    #region Very Long Expiration

    [Fact]
    public async Task should_handle_very_long_expiration()
    {
        // given
        using var cache = _CreateCache();

        // when - use a very long but valid expiration (100 years)
        await cache.UpsertAsync("key", "value", TimeSpan.FromDays(365 * 100), AbortToken);
        _timeProvider.Advance(TimeSpan.FromDays(365 * 50)); // Advance 50 years

        // then - still valid
        var result = await cache.GetAsync<string>("key", AbortToken);
        result.HasValue.Should().BeTrue();
        result.Value.Should().Be("value");
    }

    #endregion

    #region Concurrent Operations

    [Fact]
    public async Task should_handle_concurrent_add_only_one_succeeds()
    {
        // given
        using var cache = _CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        var successCount = 0;

        // when
        var tasks = Enumerable
            .Range(0, 10)
            .Select(async i =>
            {
                if (await cache.TryInsertAsync(key, i, TimeSpan.FromMinutes(5), AbortToken))
                {
                    Interlocked.Increment(ref successCount);
                }
            });

        await Task.WhenAll(tasks);

        // then
        successCount.Should().Be(1);
    }

    #endregion

    #region RemoveByPrefix Special Characters

    [Fact]
    public async Task should_treat_regex_special_chars_as_literals_in_prefix()
    {
        // given
        using var cache = _CreateCache();
        await cache.UpsertAsync("test.*key", "value1", TimeSpan.FromMinutes(5), AbortToken);
        await cache.UpsertAsync("test.other", "value2", TimeSpan.FromMinutes(5), AbortToken);

        // when - asterisk should be treated as literal, not regex
        var removed = await cache.RemoveByPrefixAsync("test.*", AbortToken);

        // then
        removed.Should().Be(1);
        (await cache.ExistsAsync("test.other", AbortToken)).Should().BeTrue();
    }

    [Fact]
    public async Task should_remove_by_prefix_with_brackets()
    {
        // given
        using var cache = _CreateCache();
        await cache.UpsertAsync("[test]key1", "value1", TimeSpan.FromMinutes(5), AbortToken);
        await cache.UpsertAsync("[test]key2", "value2", TimeSpan.FromMinutes(5), AbortToken);
        await cache.UpsertAsync("other", "value3", TimeSpan.FromMinutes(5), AbortToken);

        // when
        var removed = await cache.RemoveByPrefixAsync("[test]", AbortToken);

        // then
        removed.Should().Be(2);
        (await cache.ExistsAsync("other", AbortToken)).Should().BeTrue();
    }

    #endregion

    private sealed class TestClass
    {
        public int Value { get; set; }
    }
}
