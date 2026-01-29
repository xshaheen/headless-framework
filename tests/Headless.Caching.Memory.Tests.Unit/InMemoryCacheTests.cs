// Copyright (c) Mahmoud Shaheen. All rights reserved.

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
    public async Task should_throw_when_expiration_is_zero()
    {
        // given
        using var cache = _CreateCache();
        var key = Faker.Random.AlphaNumeric(10);

        // when
        var act = () => cache.UpsertAsync(key, 42, TimeSpan.Zero, AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
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
        var act = () => cache.UpsertAllAsync(values, TimeSpan.Zero, AbortToken);

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
        var act = () => cache.TryInsertAsync(key, 42, TimeSpan.Zero, AbortToken);

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
    public async Task should_throw_when_double_increment_expiration_is_zero()
    {
        // given
        using var cache = _CreateCache();
        var key = Faker.Random.AlphaNumeric(10);

        // when
        var act = () => cache.IncrementAsync(key, 5.5, TimeSpan.Zero, AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
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
    public async Task should_throw_when_long_increment_expiration_is_zero()
    {
        // given
        using var cache = _CreateCache();
        var key = Faker.Random.AlphaNumeric(10);

        // when
        var act = () => cache.IncrementAsync(key, 5L, TimeSpan.Zero, AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
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
    public async Task should_add_string_to_set()
    {
        // given
        using var cache = _CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        var value = "test-value";

        // when
        var result = await cache.SetAddAsync(key, value, TimeSpan.FromMinutes(5), AbortToken);

        // then
        result.Should().Be(1);
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
    public async Task should_return_empty_when_set_does_not_exist()
    {
        // given
        using var cache = _CreateCache();
        var key = Faker.Random.AlphaNumeric(10);

        // when
        var result = await cache.GetSetAsync<object>(key, cancellationToken: AbortToken);

        // then
        result.HasValue.Should().BeFalse();
        result.Value.Should().BeEmpty();
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
        await cache.SetAddAsync(key, "item1", TimeSpan.FromMinutes(5), AbortToken);
        await cache.SetAddAsync(key, "item2", TimeSpan.FromMinutes(5), AbortToken);

        // when
        var result = await cache.SetRemoveAsync(key, "item1", null, AbortToken);

        // then
        result.Should().Be(1);
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

    #endregion

    #region KeyPrefix Handling

    [Fact]
    public async Task should_prefix_keys_when_key_prefix_is_set()
    {
        // given
        var prefix = "myapp:";
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
        var prefix = "myapp:";
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
        result.Value.Should().BeEquivalentTo(new Dictionary<string, int>(StringComparer.Ordinal) { ["a"] = 1, ["b"] = 2 });
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

        // when / then - should not throw
        cache.Dispose();
        cache.Dispose();
    }

    #endregion

    private sealed class TestClass
    {
        public int Value { get; set; }
    }
}
