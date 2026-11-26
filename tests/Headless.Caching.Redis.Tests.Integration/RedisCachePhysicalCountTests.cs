// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Caching;
using Headless.Redis;
using Headless.Serializer;
using Headless.Testing.Tests;
using Microsoft.Extensions.Time.Testing;
using StackExchange.Redis;

namespace Tests;

public sealed class RedisCachePhysicalCountTests : TestBase
{
    private readonly IConnectionMultiplexer _multiplexer = Substitute.For<IConnectionMultiplexer>();
    private readonly IDatabase _database = Substitute.For<IDatabase>();
    private readonly IServer _server = Substitute.For<IServer>();

    public RedisCachePhysicalCountTests()
    {
        var endpoint = new DnsEndPoint("redis-primary", 6379);
        _multiplexer.GetDatabase(Arg.Any<int>(), Arg.Any<object?>()).Returns(_database);
        _multiplexer.GetEndPoints(Arg.Any<bool>()).Returns([endpoint]);
        _multiplexer.GetServer(endpoint, Arg.Any<object?>()).Returns(_server);
        _multiplexer.HashSlot(Arg.Any<RedisKey>()).Returns(-1);
        _database
            .StringGetAsync(Arg.Any<RedisKey[]>(), CommandFlags.DemandMaster)
            .Returns(Task.FromResult<RedisValue[]>(["100", "100"]));
    }

    [Fact]
    public async Task should_use_dbsize_once_only_fixed_metadata_remains_after_a_flush()
    {
        _database
            .StringGetAsync(Arg.Any<RedisKey>(), CommandFlags.DemandMaster)
            .Returns(RedisValue.Null, (RedisValue)"fixed", (RedisValue)"fixed");
        _database
            .KeyExistsAsync(Arg.Any<RedisKey>(), CommandFlags.DemandMaster)
            .Returns(call => call.Arg<RedisKey>().ToString().EndsWith("count-mode", StringComparison.Ordinal));
        _database.KeyExistsAsync("\0__remove", CommandFlags.DemandMaster).Returns(true);
        _server.DatabaseSizeAsync().Returns(4L);
        _server
            .KeysAsync(pattern: "*", pageSize: 2500)
            .Returns(new RedisKey[] { "one", "two", "\0__remove" }.ToAsyncEnumerable());

        using var scripts = new HeadlessRedisScriptsLoader(_multiplexer);
        using var cache = _CreateCache(scripts);

        (await cache.GetCountAsync(cancellationToken: AbortToken)).Should().Be(2L);
        (await cache.GetCountAsync(cancellationToken: AbortToken)).Should().Be(2L);

        _server.Received(1).KeysAsync(pattern: "*", pageSize: 2500);
        await _server.Received(1).DatabaseSizeAsync();
        await _database
            .Received(1)
            .StringSetAsync(
                Arg.Any<RedisKey>(),
                "fixed",
                expiry: TimeSpan.FromMinutes(1),
                when: When.NotExists,
                flags: CommandFlags.DemandMaster
            );
    }

    [Fact]
    public async Task should_scan_if_native_metadata_can_appear_during_dbsize()
    {
        _database
            .StringGetAsync(Arg.Any<RedisKey>(), CommandFlags.DemandMaster)
            .Returns((RedisValue)"fixed", (RedisValue)"variable");
        _server.DatabaseSizeAsync().Returns(4L);
        _server
            .KeysAsync(pattern: "*", pageSize: 2500)
            .Returns(
                new RedisKey[]
                {
                    "one",
                    "two",
                    "\0__remove",
                    "\0__native:{tag}:one",
                    "other:\0__clear",
                }.ToAsyncEnumerable()
            );

        using var scripts = new HeadlessRedisScriptsLoader(_multiplexer);
        using var cache = _CreateCache(scripts);

        (await cache.GetCountAsync(cancellationToken: AbortToken)).Should().Be(2L);

        await _server.Received(1).DatabaseSizeAsync();
        _server.Received(1).KeysAsync(pattern: "*", pageSize: 2500);
    }

    private RedisCache _CreateCache(HeadlessRedisScriptsLoader scripts)
    {
        return new RedisCache(
            new SystemJsonSerializer(),
            new FakeTimeProvider(),
            new RedisCacheOptions { ConnectionMultiplexer = _multiplexer },
            scripts
        );
    }
}
