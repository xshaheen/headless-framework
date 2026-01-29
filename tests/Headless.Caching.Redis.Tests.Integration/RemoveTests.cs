// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

public sealed class RemoveTests(RedisCacheFixture fixture) : RedisCacheTestBase(fixture)
{
    [Fact]
    public async Task should_remove_existing_key()
    {
        // given
        await FlushAsync();
        var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        await cache.UpsertAsync(key, "value", TimeSpan.FromMinutes(5), AbortToken);

        // when
        var result = await cache.RemoveAsync(key, AbortToken);

        // then
        result.Should().BeTrue();
        var exists = await cache.ExistsAsync(key, AbortToken);
        exists.Should().BeFalse();
    }

    [Fact]
    public async Task should_return_false_when_remove_non_existing_key()
    {
        // given
        await FlushAsync();
        var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);

        // when
        var result = await cache.RemoveAsync(key, AbortToken);

        // then
        result.Should().BeFalse();
    }

    [Fact]
    public async Task should_remove_if_equal_when_expected_matches()
    {
        // given
        await FlushAsync();
        var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        var value = Faker.Lorem.Sentence();
        await cache.UpsertAsync(key, value, TimeSpan.FromMinutes(5), AbortToken);

        // when
        var result = await cache.RemoveIfEqualAsync(key, value, AbortToken);

        // then
        result.Should().BeTrue();
        var exists = await cache.ExistsAsync(key, AbortToken);
        exists.Should().BeFalse();
    }

    [Fact]
    public async Task should_not_remove_if_equal_when_expected_not_matches()
    {
        // given
        await FlushAsync();
        var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        var value = Faker.Lorem.Sentence();
        var wrongExpected = Faker.Lorem.Sentence();
        await cache.UpsertAsync(key, value, TimeSpan.FromMinutes(5), AbortToken);

        // when
        var result = await cache.RemoveIfEqualAsync(key, wrongExpected, AbortToken);

        // then
        result.Should().BeFalse();
        var exists = await cache.ExistsAsync(key, AbortToken);
        exists.Should().BeTrue();
    }

    [Fact]
    public async Task should_remove_all_keys()
    {
        // given
        await FlushAsync();
        var cache = CreateCache();
        var keys = new List<string>();
        for (var i = 0; i < 3; i++)
        {
            var key = Faker.Random.AlphaNumeric(10);
            keys.Add(key);
            await cache.UpsertAsync(key, $"value-{i}", TimeSpan.FromMinutes(5), AbortToken);
        }

        // when
        var result = await cache.RemoveAllAsync(keys, AbortToken);

        // then
        result.Should().Be(3);
        foreach (var key in keys)
        {
            var exists = await cache.ExistsAsync(key, AbortToken);
            exists.Should().BeFalse();
        }
    }

    [Fact]
    public async Task should_return_zero_when_remove_all_empty_keys()
    {
        // given
        await FlushAsync();
        var cache = CreateCache();

        // when
        var result = await cache.RemoveAllAsync([], AbortToken);

        // then
        result.Should().Be(0);
    }

    [Fact]
    public async Task should_remove_by_prefix()
    {
        // given
        await FlushAsync();
        var cache = CreateCache();
        var prefix = "removeprefix:";
        await cache.UpsertAsync($"{prefix}key1", "value1", TimeSpan.FromMinutes(5), AbortToken);
        await cache.UpsertAsync($"{prefix}key2", "value2", TimeSpan.FromMinutes(5), AbortToken);
        await cache.UpsertAsync("other:key3", "value3", TimeSpan.FromMinutes(5), AbortToken);

        // when
        var result = await cache.RemoveByPrefixAsync(prefix, AbortToken);

        // then
        result.Should().Be(2);
        var exists1 = await cache.ExistsAsync($"{prefix}key1", AbortToken);
        var exists2 = await cache.ExistsAsync($"{prefix}key2", AbortToken);
        var exists3 = await cache.ExistsAsync("other:key3", AbortToken);
        exists1.Should().BeFalse();
        exists2.Should().BeFalse();
        exists3.Should().BeTrue();
    }

    [Fact]
    public async Task should_flush_all()
    {
        // given
        await FlushAsync();
        var cache = CreateCache();
        for (var i = 0; i < 5; i++)
        {
            await cache.UpsertAsync(Faker.Random.AlphaNumeric(10), "value", TimeSpan.FromMinutes(5), AbortToken);
        }

        // when
        await cache.FlushAsync(AbortToken);

        // then
        var count = await cache.GetCountAsync(cancellationToken: AbortToken);
        count.Should().Be(0);
    }
}
