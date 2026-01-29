// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

public sealed class TryInsertTests(RedisCacheFixture fixture) : RedisCacheTestBase(fixture)
{
    [Fact]
    public async Task should_insert_when_key_not_exists()
    {
        // given
        await FlushAsync();
        using var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        var value = Faker.Lorem.Sentence();

        // when
        var result = await cache.TryInsertAsync(key, value, TimeSpan.FromMinutes(5), AbortToken);

        // then
        result.Should().BeTrue();
        var cached = await cache.GetAsync<string>(key, AbortToken);
        cached.Value.Should().Be(value);
    }

    [Fact]
    public async Task should_not_insert_when_key_exists()
    {
        // given
        await FlushAsync();
        using var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        var originalValue = Faker.Lorem.Sentence();
        var newValue = Faker.Lorem.Sentence();
        await cache.UpsertAsync(key, originalValue, TimeSpan.FromMinutes(5), AbortToken);

        // when
        var result = await cache.TryInsertAsync(key, newValue, TimeSpan.FromMinutes(5), AbortToken);

        // then
        result.Should().BeFalse();
        var cached = await cache.GetAsync<string>(key, AbortToken);
        cached.Value.Should().Be(originalValue);
    }

    [Fact]
    public async Task should_insert_null_value()
    {
        // given
        await FlushAsync();
        using var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);

        // when
        var result = await cache.TryInsertAsync<string?>(key, null, TimeSpan.FromMinutes(5), AbortToken);

        // then
        result.Should().BeTrue();
        var cached = await cache.GetAsync<string>(key, AbortToken);
        cached.HasValue.Should().BeTrue();
        cached.IsNull.Should().BeTrue();
    }
}
