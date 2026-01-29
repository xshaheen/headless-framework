// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

public sealed class SetIfHigherLowerTests(RedisCacheFixture fixture) : RedisCacheTestBase(fixture)
{
    #region SetIfHigher Long

    [Fact]
    public async Task should_set_if_higher_long_new_key()
    {
        // given
        await FlushAsync();
        var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);

        // when
        var result = await cache.SetIfHigherAsync(key, 100L, TimeSpan.FromMinutes(5), AbortToken);

        // then
        result.Should().Be(100);
    }

    [Fact]
    public async Task should_set_if_higher_long_when_value_is_higher()
    {
        // given
        await FlushAsync();
        var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        await cache.SetIfHigherAsync(key, 50L, TimeSpan.FromMinutes(5), AbortToken);

        // when
        var result = await cache.SetIfHigherAsync(key, 100L, TimeSpan.FromMinutes(5), AbortToken);

        // then
        result.Should().Be(100);
    }

    [Fact]
    public async Task should_not_set_if_higher_long_when_value_is_lower()
    {
        // given
        await FlushAsync();
        var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        await cache.SetIfHigherAsync(key, 100L, TimeSpan.FromMinutes(5), AbortToken);

        // when
        var result = await cache.SetIfHigherAsync(key, 50L, TimeSpan.FromMinutes(5), AbortToken);

        // then
        result.Should().Be(100);
    }

    #endregion

    #region SetIfHigher Double

    [Fact]
    public async Task should_set_if_higher_double_new_key()
    {
        // given
        await FlushAsync();
        var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);

        // when
        var result = await cache.SetIfHigherAsync(key, 100.5, TimeSpan.FromMinutes(5), AbortToken);

        // then
        result.Should().Be(100.5);
    }

    [Fact]
    public async Task should_set_if_higher_double_when_value_is_higher()
    {
        // given
        await FlushAsync();
        var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        await cache.SetIfHigherAsync(key, 50.5, TimeSpan.FromMinutes(5), AbortToken);

        // when
        var result = await cache.SetIfHigherAsync(key, 100.5, TimeSpan.FromMinutes(5), AbortToken);

        // then
        result.Should().Be(100.5);
    }

    [Fact]
    public async Task should_not_set_if_higher_double_when_value_is_lower()
    {
        // given
        await FlushAsync();
        var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        await cache.SetIfHigherAsync(key, 100.5, TimeSpan.FromMinutes(5), AbortToken);

        // when
        var result = await cache.SetIfHigherAsync(key, 50.5, TimeSpan.FromMinutes(5), AbortToken);

        // then
        result.Should().Be(100.5);
    }

    #endregion

    #region SetIfLower Long

    [Fact]
    public async Task should_set_if_lower_long_new_key()
    {
        // given
        await FlushAsync();
        var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);

        // when
        var result = await cache.SetIfLowerAsync(key, 100L, TimeSpan.FromMinutes(5), AbortToken);

        // then
        result.Should().Be(100);
    }

    [Fact]
    public async Task should_set_if_lower_long_when_value_is_lower()
    {
        // given
        await FlushAsync();
        var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        await cache.SetIfLowerAsync(key, 100L, TimeSpan.FromMinutes(5), AbortToken);

        // when
        var result = await cache.SetIfLowerAsync(key, 50L, TimeSpan.FromMinutes(5), AbortToken);

        // then
        result.Should().Be(50);
    }

    [Fact]
    public async Task should_not_set_if_lower_long_when_value_is_higher()
    {
        // given
        await FlushAsync();
        var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        await cache.SetIfLowerAsync(key, 50L, TimeSpan.FromMinutes(5), AbortToken);

        // when
        var result = await cache.SetIfLowerAsync(key, 100L, TimeSpan.FromMinutes(5), AbortToken);

        // then
        result.Should().Be(50);
    }

    #endregion

    #region SetIfLower Double

    [Fact]
    public async Task should_set_if_lower_double_new_key()
    {
        // given
        await FlushAsync();
        var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);

        // when
        var result = await cache.SetIfLowerAsync(key, 100.5, TimeSpan.FromMinutes(5), AbortToken);

        // then
        result.Should().Be(100.5);
    }

    [Fact]
    public async Task should_set_if_lower_double_when_value_is_lower()
    {
        // given
        await FlushAsync();
        var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        await cache.SetIfLowerAsync(key, 100.5, TimeSpan.FromMinutes(5), AbortToken);

        // when
        var result = await cache.SetIfLowerAsync(key, 50.5, TimeSpan.FromMinutes(5), AbortToken);

        // then
        result.Should().Be(50.5);
    }

    [Fact]
    public async Task should_not_set_if_lower_double_when_value_is_higher()
    {
        // given
        await FlushAsync();
        var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        await cache.SetIfLowerAsync(key, 50.5, TimeSpan.FromMinutes(5), AbortToken);

        // when
        var result = await cache.SetIfLowerAsync(key, 100.5, TimeSpan.FromMinutes(5), AbortToken);

        // then
        result.Should().Be(50.5);
    }

    #endregion

    #region Zero Expiration

    [Fact]
    public async Task should_return_zero_when_set_if_higher_long_with_zero_expiration()
    {
        // given
        await FlushAsync();
        var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        await cache.SetIfHigherAsync(key, 100L, TimeSpan.FromMinutes(5), AbortToken);

        // when
        var result = await cache.SetIfHigherAsync(key, 150L, TimeSpan.Zero, AbortToken);

        // then
        result.Should().Be(0);
        var exists = await cache.ExistsAsync(key, AbortToken);
        exists.Should().BeFalse();
    }

    [Fact]
    public async Task should_return_zero_when_set_if_lower_long_with_zero_expiration()
    {
        // given
        await FlushAsync();
        var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        await cache.SetIfLowerAsync(key, 100L, TimeSpan.FromMinutes(5), AbortToken);

        // when
        var result = await cache.SetIfLowerAsync(key, 50L, TimeSpan.Zero, AbortToken);

        // then
        result.Should().Be(0);
        var exists = await cache.ExistsAsync(key, AbortToken);
        exists.Should().BeFalse();
    }

    #endregion
}
