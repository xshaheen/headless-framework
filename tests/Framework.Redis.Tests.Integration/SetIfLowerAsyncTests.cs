// Copyright (c) Mahmoud Shaheen. All rights reserved.

using AwesomeAssertions;
using Framework.Redis;
using StackExchange.Redis;

namespace Tests;

[Collection(nameof(RedisTestFixture))]
public sealed class SetIfLowerAsyncTests(RedisTestFixture fixture)
{
    private IDatabase Db => fixture.ConnectionMultiplexer.GetDatabase();
    private HeadlessRedisScriptsLoader Loader => fixture.ScriptsLoader;

    private async Task FlushAsync() => await fixture.ConnectionMultiplexer.FlushAllAsync();

    #region Long overload tests

    [Fact]
    public async Task should_set_when_value_is_lower_long()
    {
        // given
        await FlushAsync();
        var key = "min-value";
        await Db.StringSetAsync(key, "10");

        // when
        var result = await Loader.SetIfLowerAsync(Db, key, 5L);

        // then
        result.Should().Be(5); // delta = 10 - 5
        var value = await Db.StringGetAsync(key);
        ((long)value).Should().Be(5);
    }

    [Fact]
    public async Task should_not_set_when_value_is_higher_long()
    {
        // given
        await FlushAsync();
        var key = "min-value";
        await Db.StringSetAsync(key, "10");

        // when
        var result = await Loader.SetIfLowerAsync(Db, key, 15L);

        // then
        result.Should().Be(0);
        var value = await Db.StringGetAsync(key);
        ((long)value).Should().Be(10);
    }

    [Fact]
    public async Task should_set_when_key_not_exists_long()
    {
        // given
        await FlushAsync();
        var key = "min-value-nonexistent";

        // when
        var result = await Loader.SetIfLowerAsync(Db, key, 10L);

        // then
        result.Should().Be(10);
        var value = await Db.StringGetAsync(key);
        ((long)value).Should().Be(10);
    }

    [Fact]
    public async Task should_return_difference_when_set_long()
    {
        // given
        await FlushAsync();
        var key = "min-value";
        await Db.StringSetAsync(key, "12");

        // when
        var result = await Loader.SetIfLowerAsync(Db, key, 7L);

        // then
        result.Should().Be(5); // 12 - 7 = 5
        var value = await Db.StringGetAsync(key);
        ((long)value).Should().Be(7);
    }

    [Fact]
    public async Task should_set_ttl_when_provided_long()
    {
        // given
        await FlushAsync();
        var key = "min-value-with-ttl";
        var ttl = TimeSpan.FromSeconds(60);

        // when
        var result = await Loader.SetIfLowerAsync(Db, key, 10L, ttl);

        // then
        result.Should().Be(10);
        var actualTtl = await Db.KeyTimeToLiveAsync(key);
        actualTtl.Should().NotBeNull();
        actualTtl!.Value.TotalSeconds.Should().BeGreaterThan(50);
        actualTtl.Value.TotalSeconds.Should().BeLessThanOrEqualTo(60);
    }

    #endregion

    #region Double overload tests

    [Fact]
    public async Task should_set_when_value_is_lower_double()
    {
        // given
        await FlushAsync();
        var key = "min-value-double";
        await Db.StringSetAsync(key, "10.5");

        // when
        var result = await Loader.SetIfLowerAsync(Db, key, 5.5);

        // then
        result.Should().Be(5.0); // delta = 10.5 - 5.5
        var value = await Db.StringGetAsync(key);
        ((double)value).Should().Be(5.5);
    }

    [Fact]
    public async Task should_not_set_when_value_is_higher_double()
    {
        // given
        await FlushAsync();
        var key = "min-value-double";
        await Db.StringSetAsync(key, "10.5");

        // when
        var result = await Loader.SetIfLowerAsync(Db, key, 15.5);

        // then
        result.Should().Be(0);
        var value = await Db.StringGetAsync(key);
        ((double)value).Should().Be(10.5);
    }

    [Fact]
    public async Task should_set_when_key_not_exists_double()
    {
        // given
        await FlushAsync();
        var key = "min-value-double-nonexistent";

        // when
        var result = await Loader.SetIfLowerAsync(Db, key, 10.5);

        // then
        result.Should().Be(10.5);
        var value = await Db.StringGetAsync(key);
        ((double)value).Should().Be(10.5);
    }

    [Fact]
    public async Task should_return_difference_when_set_double()
    {
        // given
        await FlushAsync();
        var key = "min-value-double";
        await Db.StringSetAsync(key, "12.5");

        // when
        var result = await Loader.SetIfLowerAsync(Db, key, 7.5);

        // then
        result.Should().Be(5.0); // 12.5 - 7.5 = 5.0
        var value = await Db.StringGetAsync(key);
        ((double)value).Should().Be(7.5);
    }

    #endregion
}
