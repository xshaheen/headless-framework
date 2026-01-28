// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Redis;
using StackExchange.Redis;

namespace Tests;

[Collection(nameof(RedisTestFixture))]
public sealed class IncrementAsyncTests(RedisTestFixture fixture)
{
    private IDatabase Db => fixture.ConnectionMultiplexer.GetDatabase();
    private HeadlessRedisScriptsLoader Loader => fixture.ScriptsLoader;

    private async Task FlushAsync() => await fixture.ConnectionMultiplexer.FlushAllAsync();

    #region Long tests

    [Fact]
    public async Task should_increment_existing_value_long()
    {
        // given
        await FlushAsync();
        var key = "counter-long";
        await Db.StringSetAsync(key, "10");

        // when
        var result = await Loader.IncrementAsync(Db, key, 5L, TimeSpan.FromMinutes(5));

        // then
        result.Should().Be(15);
    }

    [Fact]
    public async Task should_create_key_with_value_when_not_exists_long()
    {
        // given
        await FlushAsync();
        var key = "new-counter-long";

        // when
        var result = await Loader.IncrementAsync(Db, key, 10L, TimeSpan.FromMinutes(5));

        // then
        result.Should().Be(10);
        var storedValue = await Db.StringGetAsync(key);
        storedValue.ToString().Should().Be("10");
    }

    [Fact]
    public async Task should_set_ttl_on_increment_long()
    {
        // given
        await FlushAsync();
        var key = "counter-with-ttl-long";
        var ttl = TimeSpan.FromMinutes(5);

        // when
        await Loader.IncrementAsync(Db, key, 1L, ttl);

        // then
        var actualTtl = await Db.KeyTimeToLiveAsync(key);
        actualTtl.Should().NotBeNull();
        actualTtl!.Value.TotalSeconds.Should().BeGreaterThan(0);
        actualTtl.Value.TotalMinutes.Should().BeLessThanOrEqualTo(5);
    }

    [Fact]
    public async Task should_handle_negative_increment_long()
    {
        // given
        await FlushAsync();
        var key = "counter-negative-long";
        await Db.StringSetAsync(key, "10");

        // when
        var result = await Loader.IncrementAsync(Db, key, -3L, TimeSpan.FromMinutes(5));

        // then
        result.Should().Be(7);
    }

    #endregion

    #region Double tests

    [Fact]
    public async Task should_increment_existing_value_double()
    {
        // given
        await FlushAsync();
        var key = "counter-double";
        await Db.StringSetAsync(key, "10.5");

        // when
        var result = await Loader.IncrementAsync(Db, key, 2.5, TimeSpan.FromMinutes(5));

        // then
        result.Should().Be(13.0);
    }

    [Fact]
    public async Task should_create_key_with_value_when_not_exists_double()
    {
        // given
        await FlushAsync();
        var key = "new-counter-double";

        // when
        var result = await Loader.IncrementAsync(Db, key, 5.5, TimeSpan.FromMinutes(5));

        // then
        result.Should().Be(5.5);
        var storedValue = await Db.StringGetAsync(key);
        storedValue.ToString().Should().Be("5.5");
    }

    [Fact]
    public async Task should_set_ttl_on_increment_double()
    {
        // given
        await FlushAsync();
        var key = "counter-with-ttl-double";
        var ttl = TimeSpan.FromMinutes(5);

        // when
        await Loader.IncrementAsync(Db, key, 1.5, ttl);

        // then
        var actualTtl = await Db.KeyTimeToLiveAsync(key);
        actualTtl.Should().NotBeNull();
        actualTtl!.Value.TotalSeconds.Should().BeGreaterThan(0);
        actualTtl.Value.TotalMinutes.Should().BeLessThanOrEqualTo(5);
    }

    [Fact]
    public async Task should_handle_negative_increment_double()
    {
        // given
        await FlushAsync();
        var key = "counter-negative-double";
        await Db.StringSetAsync(key, "10.0");

        // when
        var result = await Loader.IncrementAsync(Db, key, -3.5, TimeSpan.FromMinutes(5));

        // then
        result.Should().Be(6.5);
    }

    #endregion
}
