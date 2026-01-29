// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

public sealed class IncrementTests(RedisCacheFixture fixture) : RedisCacheTestBase(fixture)
{
    #region Long Increment

    [Fact]
    public async Task should_increment_long_new_key()
    {
        // given
        await FlushAsync();
        var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);

        // when
        var result = await cache.IncrementAsync(key, 10L, TimeSpan.FromMinutes(5), AbortToken);

        // then
        result.Should().Be(10);
    }

    [Fact]
    public async Task should_increment_long_existing_key()
    {
        // given
        await FlushAsync();
        var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        await cache.IncrementAsync(key, 10L, TimeSpan.FromMinutes(5), AbortToken);

        // when
        var result = await cache.IncrementAsync(key, 5L, TimeSpan.FromMinutes(5), AbortToken);

        // then
        result.Should().Be(15);
    }

    [Fact]
    public async Task should_decrement_long_with_negative_amount()
    {
        // given
        await FlushAsync();
        var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        await cache.IncrementAsync(key, 20L, TimeSpan.FromMinutes(5), AbortToken);

        // when
        var result = await cache.IncrementAsync(key, -5L, TimeSpan.FromMinutes(5), AbortToken);

        // then
        result.Should().Be(15);
    }

    [Fact]
    public async Task should_set_expiration_on_long_increment()
    {
        // given
        await FlushAsync();
        var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);

        // when
        await cache.IncrementAsync(key, 1L, TimeSpan.FromMinutes(5), AbortToken);

        // then
        var expiration = await cache.GetExpirationAsync(key, AbortToken);
        expiration.Should().NotBeNull();
        expiration!.Value.TotalMinutes.Should().BeLessThanOrEqualTo(5);
    }

    [Fact]
    public async Task should_return_zero_when_long_increment_with_zero_expiration()
    {
        // given
        await FlushAsync();
        var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        await cache.IncrementAsync(key, 10L, TimeSpan.FromMinutes(5), AbortToken);

        // when
        var result = await cache.IncrementAsync(key, 5L, TimeSpan.Zero, AbortToken);

        // then
        result.Should().Be(0);
        var exists = await cache.ExistsAsync(key, AbortToken);
        exists.Should().BeFalse();
    }

    #endregion

    #region Double Increment

    [Fact]
    public async Task should_increment_double_new_key()
    {
        // given
        await FlushAsync();
        var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);

        // when
        var result = await cache.IncrementAsync(key, 10.5, TimeSpan.FromMinutes(5), AbortToken);

        // then
        result.Should().Be(10.5);
    }

    [Fact]
    public async Task should_increment_double_existing_key()
    {
        // given
        await FlushAsync();
        var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        await cache.IncrementAsync(key, 10.5, TimeSpan.FromMinutes(5), AbortToken);

        // when
        var result = await cache.IncrementAsync(key, 2.5, TimeSpan.FromMinutes(5), AbortToken);

        // then
        result.Should().Be(13.0);
    }

    [Fact]
    public async Task should_decrement_double_with_negative_amount()
    {
        // given
        await FlushAsync();
        var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        await cache.IncrementAsync(key, 20.0, TimeSpan.FromMinutes(5), AbortToken);

        // when
        var result = await cache.IncrementAsync(key, -5.5, TimeSpan.FromMinutes(5), AbortToken);

        // then
        result.Should().Be(14.5);
    }

    #endregion
}
