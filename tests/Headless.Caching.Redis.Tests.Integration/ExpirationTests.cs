// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

public sealed class ExpirationTests(RedisCacheFixture fixture) : RedisCacheTestBase(fixture)
{
    [Fact]
    public async Task should_get_expiration()
    {
        // given
        await FlushAsync();
        var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        var expiration = TimeSpan.FromMinutes(5);
        await cache.UpsertAsync(key, "value", expiration, AbortToken);

        // when
        var result = await cache.GetExpirationAsync(key, AbortToken);

        // then
        result.Should().NotBeNull();
        result!.Value.TotalMinutes.Should().BeGreaterThan(4);
        result.Value.TotalMinutes.Should().BeLessThanOrEqualTo(5);
    }

    [Fact]
    public async Task should_return_null_expiration_when_key_not_exists()
    {
        // given
        await FlushAsync();
        var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);

        // when
        var result = await cache.GetExpirationAsync(key, AbortToken);

        // then
        result.Should().BeNull();
    }

    [Fact]
    public async Task should_return_null_expiration_when_no_expiration_set()
    {
        // given
        await FlushAsync();
        var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        await cache.UpsertAsync(key, "value", expiration: null, AbortToken);

        // when
        var result = await cache.GetExpirationAsync(key, AbortToken);

        // then
        result.Should().BeNull();
    }

    [Fact]
    public async Task should_expire_key_after_timeout()
    {
        // given
        await FlushAsync();
        var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        await cache.UpsertAsync(key, "value", TimeSpan.FromMilliseconds(100), AbortToken);

        // when
        await Task.Delay(200, AbortToken);

        // then
        var exists = await cache.ExistsAsync(key, AbortToken);
        exists.Should().BeFalse();
    }

    [Fact]
    public async Task should_update_expiration_on_upsert()
    {
        // given
        await FlushAsync();
        var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        await cache.UpsertAsync(key, "value", TimeSpan.FromMinutes(1), AbortToken);

        // when
        await cache.UpsertAsync(key, "new value", TimeSpan.FromMinutes(10), AbortToken);

        // then
        var expiration = await cache.GetExpirationAsync(key, AbortToken);
        expiration.Should().NotBeNull();
        expiration!.Value.TotalMinutes.Should().BeGreaterThan(5);
    }
}
