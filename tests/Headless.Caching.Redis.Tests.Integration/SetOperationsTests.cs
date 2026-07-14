// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

public sealed class SetOperationsTests(RedisCacheFixture fixture) : RedisCacheTestBase(fixture)
{
    [Fact]
    public async Task should_add_to_set()
    {
        // given
        await FlushAsync();
        using var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        var values = new List<string> { "value1", "value2", "value3" };

        // when
        var result = await cache.SetAddAsync(key, values, TimeSpan.FromMinutes(5), AbortToken);

        // then
        result.Should().Be(3);
    }

    [Fact]
    public async Task should_get_set_values()
    {
        // given
        await FlushAsync();
        using var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        var values = new List<string> { "value1", "value2", "value3" };
        await cache.SetAddAsync(key, values, TimeSpan.FromMinutes(5), AbortToken);

        // when
        var result = await cache.GetSetAsync<string>(key, cancellationToken: AbortToken);

        // then
        result.HasValue.Should().BeTrue();
        result.Value.Should().HaveCount(3);
        result.Value.Should().BeEquivalentTo(values);
    }

    [Fact]
    public async Task should_get_set_values_with_pagination()
    {
        // given
        await FlushAsync();
        using var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        var values = Enumerable.Range(1, 10).Select(i => $"value{i}").ToList();
        await cache.SetAddAsync(key, values, TimeSpan.FromMinutes(5), AbortToken);

        // when
        var result = await cache.GetSetAsync<string>(key, pageIndex: 1, pageSize: 3, cancellationToken: AbortToken);

        // then
        result.HasValue.Should().BeTrue();
        result.Value.Should().HaveCount(3);
    }

    [Fact]
    public async Task should_remove_from_set()
    {
        // given
        await FlushAsync();
        using var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        var values = new List<string> { "value1", "value2", "value3" };
        await cache.SetAddAsync(key, values, TimeSpan.FromMinutes(5), AbortToken);

        // when
        var result = await cache.SetRemoveAsync(key, ["value1", "value2"], TimeSpan.FromMinutes(5), AbortToken);

        // then
        result.Should().Be(2);
        var remaining = await cache.GetSetAsync<string>(key, cancellationToken: AbortToken);
        remaining.Value.Should().ContainSingle();
        remaining.Value.Should().Contain("value3");
    }

    [Fact]
    public async Task should_return_no_value_when_set_not_exists()
    {
        // given
        await FlushAsync();
        using var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);

        // when
        var result = await cache.GetSetAsync<string>(key, cancellationToken: AbortToken);

        // then - #553: an absent key is NoValue (Value is null), not a non-null empty collection
        result.HasValue.Should().BeFalse();
        result.Value.Should().BeNull();
    }

    [Fact]
    public async Task should_return_zero_when_add_empty_set()
    {
        // given
        await FlushAsync();
        using var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);

        // when
        var result = await cache.SetAddAsync<string>(key, [], TimeSpan.FromMinutes(5), AbortToken);

        // then
        result.Should().Be(0);
    }

    [Fact]
    public async Task should_add_string_as_single_value()
    {
        // given
        await FlushAsync();
        using var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);

        // when - passing string directly (will be treated as enumerable of chars if not handled)
        var result = await cache.SetAddAsync(key, new[] { "single-value" }, TimeSpan.FromMinutes(5), AbortToken);

        // then
        result.Should().Be(1);
        var values = await cache.GetSetAsync<string>(key, cancellationToken: AbortToken);
        values.Value.Should().Contain("single-value");
    }

    [Fact]
    public async Task should_handle_complex_objects_in_set()
    {
        // given
        await FlushAsync();
        using var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        var values = new List<TestSetObject>
        {
            new() { Id = 1, Name = "First" },
            new() { Id = 2, Name = "Second" },
        };

        // when
        var result = await cache.SetAddAsync(key, values, TimeSpan.FromMinutes(5), AbortToken);

        // then
        result.Should().Be(2);
        var retrieved = await cache.GetSetAsync<TestSetObject>(key, cancellationToken: AbortToken);
        retrieved.Value.Should().HaveCount(2);
    }

    [Fact]
    public async Task should_extend_key_ttl_when_existing_set_member_is_readded_with_later_expiration()
    {
        // given
        await FlushAsync();
        using var cache = CreateCache();
        var database = Fixture.ConnectionMultiplexer.GetDatabase();
        var key = Faker.Random.AlphaNumeric(10);

        await cache.SetAddAsync(key, ["value"], TimeSpan.FromSeconds(1), AbortToken);

        // when
        var added = await cache.SetAddAsync(key, ["value"], TimeSpan.FromSeconds(5), AbortToken);

        // then
        added.Should().Be(0);

        var ttl = await database.KeyTimeToLiveAsync(key);
        ttl.Should().NotBeNull();
        ttl!.Value.Should().BeCloseTo(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task should_rearm_key_ttl_after_removing_furthest_expiring_set_member()
    {
        // given
        await FlushAsync();
        using var cache = CreateCache();
        var database = Fixture.ConnectionMultiplexer.GetDatabase();
        var key = Faker.Random.AlphaNumeric(10);

        await cache.SetAddAsync(key, ["short"], TimeSpan.FromSeconds(3), AbortToken);
        await cache.SetAddAsync(key, ["long"], TimeSpan.FromSeconds(20), AbortToken);

        // when
        var removed = await cache.SetRemoveAsync(key, ["long"], expiration: null, AbortToken);

        // then
        removed.Should().Be(1);

        var ttl = await database.KeyTimeToLiveAsync(key);
        ttl.Should().NotBeNull();
        ttl!.Value.Should().BeLessThan(TimeSpan.FromSeconds(10));
        ttl.Value.Should().BeCloseTo(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task should_persist_set_key_when_member_without_expiration_is_added()
    {
        // given
        await FlushAsync();
        using var cache = CreateCache();
        var database = Fixture.ConnectionMultiplexer.GetDatabase();
        var key = Faker.Random.AlphaNumeric(10);

        await cache.SetAddAsync(key, ["expiring"], TimeSpan.FromSeconds(5), AbortToken);

        // when
        var added = await cache.SetAddAsync(key, ["immortal"], expiration: null, AbortToken);

        // then
        added.Should().Be(1);

        var ttl = await database.KeyTimeToLiveAsync(key);
        ttl.Should().BeNull();
    }

    private sealed record TestSetObject
    {
        public int Id { get; init; }
        public required string Name { get; init; }
    }
}
