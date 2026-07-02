// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Redis;
using StackExchange.Redis;

namespace Tests;

[Collection<RedisTestFixture>]
public sealed class RemoveIfEqualAsyncTests(RedisTestFixture fixture)
{
    private IDatabase Db => fixture.ConnectionMultiplexer.GetDatabase();
    private HeadlessRedisScriptsLoader Loader => fixture.ScriptsLoader;

    // Unique per-test keys keep these tests isolated within the shared (parallel) Redis collection
    // without a global FLUSHALL that would clobber concurrent storage tests.
    private static RedisKey _NewKey() => (RedisKey)("remove-if-equal:" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task should_remove_when_expected_value_matches()
    {
        // given
        var key = _NewKey();
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
        var key = _NewKey();
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
        var key = _NewKey();

        // when
        var result = await Loader.RemoveIfEqualAsync(Db, key, expectedValue: "any-value");

        // then
        result.Should().BeFalse();
    }
}
