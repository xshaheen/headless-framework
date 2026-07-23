// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Redis;
using Headless.Redis.Testing;
using Headless.Testing.Tests;
using StackExchange.Redis;

namespace Tests;

[Collection(nameof(RedisTestFixture))]
public sealed class HeadlessConnectionMultiplexerExtensionsTests(RedisTestFixture fixture) : TestBase
{
    private ConnectionMultiplexer Multiplexer => fixture.ConnectionMultiplexer;
    private IDatabase Db => Multiplexer.GetDatabase();

    [Fact]
    public async Task should_remove_all_keys_when_flush_all_async()
    {
        // given - ensure clean state
        await Multiplexer.FlushAllAsync();

        await Db.StringSetAsync("key1", "value1");
        await Db.StringSetAsync("key2", "value2");
        await Db.StringSetAsync("key3", "value3");

        // when
        await Multiplexer.FlushAllAsync();

        // then
        var count = await Multiplexer.CountAllKeysAsync(AbortToken);
        count.Should().Be(0);
    }

    [Fact]
    public async Task should_handle_empty_database_when_flush_all_async()
    {
        // given - ensure clean state
        await Multiplexer.FlushAllAsync();

        // when / then - no exception
        var act = async () => await Multiplexer.FlushAllAsync();
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task should_return_total_key_count_when_count_all_keys_async()
    {
        // given - ensure clean state
        await Multiplexer.FlushAllAsync();

        await Db.StringSetAsync("count-key1", "value1");
        await Db.StringSetAsync("count-key2", "value2");
        await Db.StringSetAsync("count-key3", "value3");
        await Db.StringSetAsync("count-key4", "value4");
        await Db.StringSetAsync("count-key5", "value5");

        // when
        var count = await Multiplexer.CountAllKeysAsync(AbortToken);

        // then
        count.Should().Be(5);
    }

    [Fact]
    public async Task should_return_zero_for_empty_database_when_count_all_keys_async()
    {
        // given - ensure clean state
        await Multiplexer.FlushAllAsync();

        // when
        var count = await Multiplexer.CountAllKeysAsync(AbortToken);

        // then
        count.Should().Be(0);
    }

    [Fact]
    public async Task should_honor_pre_canceled_token_when_counting_all_keys()
    {
        // given
        using var cancellationTokenSource = new CancellationTokenSource();
        await cancellationTokenSource.CancelAsync();

        // when
        var act = async () => await Multiplexer.CountAllKeysAsync(cancellationTokenSource.Token);

        // then
        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
