// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

public sealed class TryReplaceTests(RedisCacheFixture fixture) : RedisCacheTestBase(fixture)
{
    [Fact]
    public async Task should_replace_when_key_exists()
    {
        // given
        await FlushAsync();
        var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        var originalValue = Faker.Lorem.Sentence();
        var newValue = Faker.Lorem.Sentence();
        await cache.UpsertAsync(key, originalValue, TimeSpan.FromMinutes(5), AbortToken);

        // when
        var result = await cache.TryReplaceAsync(key, newValue, TimeSpan.FromMinutes(5), AbortToken);

        // then
        result.Should().BeTrue();
        var cached = await cache.GetAsync<string>(key, AbortToken);
        cached.Value.Should().Be(newValue);
    }

    [Fact]
    public async Task should_not_replace_when_key_not_exists()
    {
        // given
        await FlushAsync();
        var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        var value = Faker.Lorem.Sentence();

        // when
        var result = await cache.TryReplaceAsync(key, value, TimeSpan.FromMinutes(5), AbortToken);

        // then
        result.Should().BeFalse();
        var cached = await cache.GetAsync<string>(key, AbortToken);
        cached.HasValue.Should().BeFalse();
    }

    [Fact]
    public async Task should_replace_if_equal_when_expected_matches()
    {
        // given
        await FlushAsync();
        var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        var originalValue = Faker.Lorem.Sentence();
        var newValue = Faker.Lorem.Sentence();
        await cache.UpsertAsync(key, originalValue, TimeSpan.FromMinutes(5), AbortToken);

        // when
        var result = await cache.TryReplaceIfEqualAsync(
            key,
            originalValue,
            newValue,
            TimeSpan.FromMinutes(5),
            AbortToken
        );

        // then
        result.Should().BeTrue();
        var cached = await cache.GetAsync<string>(key, AbortToken);
        cached.Value.Should().Be(newValue);
    }

    [Fact]
    public async Task should_not_replace_if_equal_when_expected_not_matches()
    {
        // given
        await FlushAsync();
        var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        var originalValue = Faker.Lorem.Sentence();
        var wrongExpected = Faker.Lorem.Sentence();
        var newValue = Faker.Lorem.Sentence();
        await cache.UpsertAsync(key, originalValue, TimeSpan.FromMinutes(5), AbortToken);

        // when
        var result = await cache.TryReplaceIfEqualAsync(
            key,
            wrongExpected,
            newValue,
            TimeSpan.FromMinutes(5),
            AbortToken
        );

        // then
        result.Should().BeFalse();
        var cached = await cache.GetAsync<string>(key, AbortToken);
        cached.Value.Should().Be(originalValue);
    }

    [Fact]
    public async Task should_not_replace_if_equal_when_key_not_exists()
    {
        // given
        await FlushAsync();
        var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        var expected = Faker.Lorem.Sentence();
        var newValue = Faker.Lorem.Sentence();

        // when
        var result = await cache.TryReplaceIfEqualAsync(key, expected, newValue, TimeSpan.FromMinutes(5), AbortToken);

        // then
        result.Should().BeFalse();
    }
}
