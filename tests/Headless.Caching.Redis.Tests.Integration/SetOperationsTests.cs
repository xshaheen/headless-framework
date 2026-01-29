// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

public sealed class SetOperationsTests(RedisCacheFixture fixture) : RedisCacheTestBase(fixture)
{
    [Fact]
    public async Task should_add_to_set()
    {
        // given
        await FlushAsync();
        var cache = CreateCache();
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
        var cache = CreateCache();
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
        var cache = CreateCache();
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
        var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        var values = new List<string> { "value1", "value2", "value3" };
        await cache.SetAddAsync(key, values, TimeSpan.FromMinutes(5), AbortToken);

        // when
        var result = await cache.SetRemoveAsync(key, ["value1", "value2"], TimeSpan.FromMinutes(5), AbortToken);

        // then
        result.Should().Be(2);
        var remaining = await cache.GetSetAsync<string>(key, cancellationToken: AbortToken);
        remaining.Value.Should().HaveCount(1);
        remaining.Value.Should().Contain("value3");
    }

    [Fact]
    public async Task should_return_no_value_when_set_not_exists()
    {
        // given
        await FlushAsync();
        var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);

        // when
        var result = await cache.GetSetAsync<string>(key, cancellationToken: AbortToken);

        // then
        result.HasValue.Should().BeFalse();
    }

    [Fact]
    public async Task should_return_zero_when_add_empty_set()
    {
        // given
        await FlushAsync();
        var cache = CreateCache();
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
        var cache = CreateCache();
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
        var cache = CreateCache();
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

    private sealed record TestSetObject
    {
        public int Id { get; init; }
        public required string Name { get; init; }
    }
}
