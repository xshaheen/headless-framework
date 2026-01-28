// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Redis;
using StackExchange.Redis;

namespace Tests;

[Collection(nameof(RedisTestFixture))]
public sealed class ConnectionMultiplexerExtensionsTests(RedisTestFixture fixture)
{
    private ConnectionMultiplexer Multiplexer => fixture.ConnectionMultiplexer;
    private IDatabase Db => Multiplexer.GetDatabase();

    [Fact]
    public async Task FlushAllAsync_should_remove_all_keys()
    {
        // given - ensure clean state
        await Multiplexer.FlushAllAsync();

        await Db.StringSetAsync("key1", "value1");
        await Db.StringSetAsync("key2", "value2");
        await Db.StringSetAsync("key3", "value3");

        // when
        await Multiplexer.FlushAllAsync();

        // then
        var count = await Multiplexer.CountAllKeysAsync();
        count.Should().Be(0);
    }

    [Fact]
    public async Task FlushAllAsync_should_handle_empty_database()
    {
        // given - ensure clean state
        await Multiplexer.FlushAllAsync();

        // when / then - no exception
        var act = async () => await Multiplexer.FlushAllAsync();
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task CountAllKeysAsync_should_return_total_key_count()
    {
        // given - ensure clean state
        await Multiplexer.FlushAllAsync();

        await Db.StringSetAsync("count-key1", "value1");
        await Db.StringSetAsync("count-key2", "value2");
        await Db.StringSetAsync("count-key3", "value3");
        await Db.StringSetAsync("count-key4", "value4");
        await Db.StringSetAsync("count-key5", "value5");

        // when
        var count = await Multiplexer.CountAllKeysAsync();

        // then
        count.Should().Be(5);
    }

    [Fact]
    public async Task CountAllKeysAsync_should_return_zero_for_empty_database()
    {
        // given - ensure clean state
        await Multiplexer.FlushAllAsync();

        // when
        var count = await Multiplexer.CountAllKeysAsync();

        // then
        count.Should().Be(0);
    }
}
