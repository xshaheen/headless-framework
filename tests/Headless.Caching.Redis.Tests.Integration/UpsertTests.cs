// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

public sealed class UpsertTests(RedisCacheFixture fixture) : RedisCacheTestBase(fixture)
{
    [Fact]
    public async Task should_upsert_string_value()
    {
        // given
        await FlushAsync();
        using var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        var value = Faker.Lorem.Sentence();

        // when
        var result = await cache.UpsertAsync(key, value, TimeSpan.FromMinutes(5), AbortToken);

        // then
        result.Should().BeTrue();
        var cached = await cache.GetAsync<string>(key, AbortToken);
        cached.HasValue.Should().BeTrue();
        cached.Value.Should().Be(value);
    }

    [Fact]
    public async Task should_upsert_complex_object()
    {
        // given
        await FlushAsync();
        using var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        var value = new TestObject { Id = Faker.Random.Guid(), Name = Faker.Name.FullName() };

        // when
        var result = await cache.UpsertAsync(key, value, TimeSpan.FromMinutes(5), AbortToken);

        // then
        result.Should().BeTrue();
        var cached = await cache.GetAsync<TestObject>(key, AbortToken);
        cached.HasValue.Should().BeTrue();
        cached.Value.Should().NotBeNull();
        cached.Value!.Id.Should().Be(value.Id);
        cached.Value.Name.Should().Be(value.Name);
    }

    [Fact]
    public async Task should_upsert_null_value()
    {
        // given
        await FlushAsync();
        using var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);

        // when
        var result = await cache.UpsertAsync<string?>(key, null, TimeSpan.FromMinutes(5), AbortToken);

        // then
        result.Should().BeTrue();
        var cached = await cache.GetAsync<string>(key, AbortToken);
        cached.HasValue.Should().BeTrue();
        cached.IsNull.Should().BeTrue();
    }

    [Fact]
    public async Task should_overwrite_existing_value()
    {
        // given
        await FlushAsync();
        using var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        var originalValue = Faker.Lorem.Sentence();
        var newValue = Faker.Lorem.Sentence();
        await cache.UpsertAsync(key, originalValue, TimeSpan.FromMinutes(5), AbortToken);

        // when
        var result = await cache.UpsertAsync(key, newValue, TimeSpan.FromMinutes(5), AbortToken);

        // then
        result.Should().BeTrue();
        var cached = await cache.GetAsync<string>(key, AbortToken);
        cached.Value.Should().Be(newValue);
    }

    [Fact]
    public async Task should_delete_key_when_expiration_is_zero()
    {
        // given
        await FlushAsync();
        using var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        await cache.UpsertAsync(key, "value", TimeSpan.FromMinutes(5), AbortToken);

        // when
        var result = await cache.UpsertAsync(key, "new value", TimeSpan.Zero, AbortToken);

        // then
        result.Should().BeFalse();
        var exists = await cache.ExistsAsync(key, AbortToken);
        exists.Should().BeFalse();
    }

    [Fact]
    public async Task should_upsert_without_expiration()
    {
        // given
        await FlushAsync();
        using var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        var value = Faker.Lorem.Sentence();

        // when
        var result = await cache.UpsertAsync(key, value, expiration: null, AbortToken);

        // then
        result.Should().BeTrue();
        var ttl = await cache.GetExpirationAsync(key, AbortToken);
        ttl.Should().BeNull();
    }

    [Fact]
    public async Task should_upsert_all_values()
    {
        // given
        await FlushAsync();
        using var cache = CreateCache();
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [Faker.Random.AlphaNumeric(10)] = Faker.Lorem.Sentence(),
            [Faker.Random.AlphaNumeric(10)] = Faker.Lorem.Sentence(),
            [Faker.Random.AlphaNumeric(10)] = Faker.Lorem.Sentence(),
        };

        // when
        var result = await cache.UpsertAllAsync(values, TimeSpan.FromMinutes(5), AbortToken);

        // then
        result.Should().Be(3);
        foreach (var (key, value) in values)
        {
            var cached = await cache.GetAsync<string>(key, AbortToken);
            cached.Value.Should().Be(value);
        }
    }

    [Fact]
    public async Task should_return_zero_when_upsert_all_with_empty_dictionary()
    {
        // given
        await FlushAsync();
        using var cache = CreateCache();
        var values = new Dictionary<string, string>(StringComparer.Ordinal);

        // when
        var result = await cache.UpsertAllAsync(values, TimeSpan.FromMinutes(5), AbortToken);

        // then
        result.Should().Be(0);
    }

    private sealed record TestObject
    {
        public Guid Id { get; init; }
        public required string Name { get; init; }
    }
}
