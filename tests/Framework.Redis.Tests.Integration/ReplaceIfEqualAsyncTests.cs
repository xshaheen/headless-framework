// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Redis;
using StackExchange.Redis;

namespace Tests;

[Collection(nameof(RedisTestFixture))]
public sealed class ReplaceIfEqualAsyncTests(RedisTestFixture fixture)
{
    private IDatabase Db => fixture.ConnectionMultiplexer.GetDatabase();
    private HeadlessRedisScriptsLoader Loader => fixture.ScriptsLoader;

    private async Task FlushAsync() => await fixture.ConnectionMultiplexer.FlushAllAsync();

    [Fact]
    public async Task should_replace_when_expected_value_matches()
    {
        // given
        await FlushAsync();
        var key = "test-key";
        await Db.StringSetAsync(key, "old-value");

        // when
        var result = await Loader.ReplaceIfEqualAsync(Db, key, expectedValue: "old-value", newValue: "new-value");

        // then
        result.Should().BeTrue();
        var value = await Db.StringGetAsync(key);
        value.ToString().Should().Be("new-value");
    }

    [Fact]
    public async Task should_replace_when_key_not_exists()
    {
        // given
        await FlushAsync();
        var key = "non-existent-key";

        // when
        var result = await Loader.ReplaceIfEqualAsync(Db, key, expectedValue: null, newValue: "new-value");

        // then
        result.Should().BeTrue();
        var value = await Db.StringGetAsync(key);
        value.ToString().Should().Be("new-value");
    }

    [Fact]
    public async Task should_not_replace_when_expected_value_differs()
    {
        // given
        await FlushAsync();
        var key = "test-key";
        await Db.StringSetAsync(key, "old-value");

        // when
        var result = await Loader.ReplaceIfEqualAsync(Db, key, expectedValue: "wrong-value", newValue: "new-value");

        // then
        result.Should().BeFalse();
        var value = await Db.StringGetAsync(key);
        value.ToString().Should().Be("old-value");
    }

    [Fact]
    public async Task should_set_ttl_when_provided()
    {
        // given
        await FlushAsync();
        var key = "test-key-with-ttl";
        await Db.StringSetAsync(key, "old-value");
        var ttl = TimeSpan.FromMinutes(5);

        // when
        var result = await Loader.ReplaceIfEqualAsync(
            Db,
            key,
            expectedValue: "old-value",
            newValue: "new-value",
            newTtl: ttl
        );

        // then
        result.Should().BeTrue();
        var keyTtl = await Db.KeyTimeToLiveAsync(key);
        keyTtl.Should().NotBeNull();
        keyTtl!.Value.Should().BeCloseTo(ttl, precision: TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task should_not_set_ttl_when_null()
    {
        // given
        await FlushAsync();
        var key = "test-key-no-ttl";
        await Db.StringSetAsync(key, "old-value");

        // when
        var result = await Loader.ReplaceIfEqualAsync(
            Db,
            key,
            expectedValue: "old-value",
            newValue: "new-value",
            newTtl: null
        );

        // then
        result.Should().BeTrue();
        var keyTtl = await Db.KeyTimeToLiveAsync(key);
        keyTtl.Should().BeNull(); // no expiry means TTL is null (or -1 in raw Redis)
    }
}
