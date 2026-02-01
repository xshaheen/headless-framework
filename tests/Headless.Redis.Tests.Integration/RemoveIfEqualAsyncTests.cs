// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Redis;
using StackExchange.Redis;

namespace Tests;

[Collection(nameof(RedisTestFixture))]
public sealed class RemoveIfEqualAsyncTests(RedisTestFixture fixture)
{
    private IDatabase Db => fixture.ConnectionMultiplexer.GetDatabase();
    private HeadlessRedisScriptsLoader Loader => fixture.ScriptsLoader;

    private async Task _FlushAsync() => await fixture.ConnectionMultiplexer.FlushAllAsync();

    [Fact]
    public async Task should_remove_when_expected_value_matches()
    {
        // given
        await _FlushAsync();
        const string key = "test-key";
        await Db.StringSetAsync(key, "value");

        // when
        var result = await Loader.RemoveIfEqualAsync(Db, key, expectedValue: "value");

        // then
        result.Should().BeTrue();
        var exists = await Db.KeyExistsAsync(key);
        exists.Should().BeFalse();
    }

    [Fact]
    public async Task should_not_remove_when_expected_value_differs()
    {
        // given
        await _FlushAsync();
        const string key = "test-key";
        await Db.StringSetAsync(key, "value");

        // when
        var result = await Loader.RemoveIfEqualAsync(Db, key, expectedValue: "wrong");

        // then
        result.Should().BeFalse();
        var exists = await Db.KeyExistsAsync(key);
        exists.Should().BeTrue();
        var currentValue = await Db.StringGetAsync(key);
        currentValue.ToString().Should().Be("value");
    }

    [Fact]
    public async Task should_not_remove_when_key_not_exists()
    {
        // given
        await _FlushAsync();
        const string key = "nonexistent-key";

        // when
        var result = await Loader.RemoveIfEqualAsync(Db, key, expectedValue: "any-value");

        // then
        result.Should().BeFalse();
    }
}
