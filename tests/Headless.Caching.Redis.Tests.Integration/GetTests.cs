// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

public sealed class GetTests(RedisCacheFixture fixture) : RedisCacheTestBase(fixture)
{
    [Fact]
    public async Task should_return_no_value_when_key_not_exists()
    {
        // given
        await FlushAsync();
        var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);

        // when
        var result = await cache.GetAsync<string>(key, AbortToken);

        // then
        result.HasValue.Should().BeFalse();
        result.IsNull.Should().BeTrue();
    }

    [Fact]
    public async Task should_return_value_when_key_exists()
    {
        // given
        await FlushAsync();
        var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        var value = Faker.Lorem.Sentence();
        await cache.UpsertAsync(key, value, TimeSpan.FromMinutes(5), AbortToken);

        // when
        var result = await cache.GetAsync<string>(key, AbortToken);

        // then
        result.HasValue.Should().BeTrue();
        result.Value.Should().Be(value);
    }

    [Fact]
    public async Task should_get_all_values()
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
        var result = await cache.GetAllAsync<string>(keys, AbortToken);

        // then
        result.Should().HaveCount(3);
        for (var i = 0; i < 3; i++)
        {
            result[keys[i]].Value.Should().Be($"value-{i}");
        }
    }

    [Fact]
    public async Task should_return_empty_dict_when_get_all_with_empty_keys()
    {
        // given
        await FlushAsync();
        var cache = CreateCache();

        // when
        var result = await cache.GetAllAsync<string>([], AbortToken);

        // then
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task should_get_by_prefix()
    {
        // given
        await FlushAsync();
        var cache = CreateCache();
        var prefix = "test:";
        await cache.UpsertAsync($"{prefix}key1", "value1", TimeSpan.FromMinutes(5), AbortToken);
        await cache.UpsertAsync($"{prefix}key2", "value2", TimeSpan.FromMinutes(5), AbortToken);
        await cache.UpsertAsync("other:key3", "value3", TimeSpan.FromMinutes(5), AbortToken);

        // when
        var result = await cache.GetByPrefixAsync<string>(prefix, AbortToken);

        // then
        result.Should().HaveCount(2);
        result.Values.Select(v => v.Value).Should().BeEquivalentTo(["value1", "value2"]);
    }

    [Fact]
    public async Task should_get_all_keys_by_prefix()
    {
        // given
        await FlushAsync();
        var cache = CreateCache();
        var prefix = "prefix:";
        await cache.UpsertAsync($"{prefix}key1", "value1", TimeSpan.FromMinutes(5), AbortToken);
        await cache.UpsertAsync($"{prefix}key2", "value2", TimeSpan.FromMinutes(5), AbortToken);
        await cache.UpsertAsync("other:key3", "value3", TimeSpan.FromMinutes(5), AbortToken);

        // when
        var result = await cache.GetAllKeysByPrefixAsync(prefix, AbortToken);

        // then
        result.Should().HaveCount(2);
        result.Should().Contain(key => key.Contains("key1"));
        result.Should().Contain(key => key.Contains("key2"));
    }

    [Fact]
    public async Task should_check_exists()
    {
        // given
        await FlushAsync();
        var cache = CreateCache();
        var existingKey = Faker.Random.AlphaNumeric(10);
        var missingKey = Faker.Random.AlphaNumeric(10);
        await cache.UpsertAsync(existingKey, "value", TimeSpan.FromMinutes(5), AbortToken);

        // when
        var existsResult = await cache.ExistsAsync(existingKey, AbortToken);
        var missingResult = await cache.ExistsAsync(missingKey, AbortToken);

        // then
        existsResult.Should().BeTrue();
        missingResult.Should().BeFalse();
    }

    [Fact]
    public async Task should_get_count()
    {
        // given
        await FlushAsync();
        var cache = CreateCache();
        var prefix = "count:";
        await cache.UpsertAsync($"{prefix}key1", "value1", TimeSpan.FromMinutes(5), AbortToken);
        await cache.UpsertAsync($"{prefix}key2", "value2", TimeSpan.FromMinutes(5), AbortToken);
        await cache.UpsertAsync("other:key3", "value3", TimeSpan.FromMinutes(5), AbortToken);

        // when
        var totalCount = await cache.GetCountAsync(cancellationToken: AbortToken);
        var prefixCount = await cache.GetCountAsync(prefix, AbortToken);

        // then
        totalCount.Should().Be(3);
        prefixCount.Should().Be(2);
    }
}
