// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Redis;
using Headless.Testing.Tests;
using StackExchange.Redis;

namespace Tests;

[Collection(nameof(RedisTestFixture))]
public sealed class HeadlessConnectionMultiplexerExtensionsTests(RedisTestFixture fixture) : TestBase
{
    private ConnectionMultiplexer Multiplexer => fixture.ConnectionMultiplexer;
    private IDatabase Db => Multiplexer.GetDatabase();

    [Fact]
    public async Task should_remove_all_keys_when_fixture_flushes_all()
    {
        // given
        await Db.StringSetAsync("flush-key1", "value1");
        await Multiplexer.GetDatabase(1).StringSetAsync("flush-key2", "value2");

        // when
        await fixture.FlushAllAsync(AbortToken);

        // then - every database is flushed, not only the default one
        (await Multiplexer.CountAllKeysAsync(AbortToken))
            .Should()
            .Be(0);
        (await Multiplexer.GetDatabase(1).KeyExistsAsync("flush-key2")).Should().BeFalse();
    }

    [Fact]
    public async Task should_return_total_key_count_when_count_all_keys_async()
    {
        // given - ensure clean state
        await fixture.FlushAllAsync(AbortToken);

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
