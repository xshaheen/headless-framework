// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Caching;
using Headless.Redis;
using Headless.Serializer;
using Headless.Testing.Tests;
using StackExchange.Redis;

namespace Tests;

/// <summary>
/// A cluster node reports its server type only once it is connected, so the cache must not settle on "standalone"
/// from a probe made while every endpoint was down. A substituted multiplexer stands in for the cluster because a live
/// cluster cannot be held disconnected for the first call; <see cref="RedisCacheClusterTests"/> covers the real cluster.
/// </summary>
public sealed class RedisCacheClusterDetectionTests : TestBase
{
    private readonly IConnectionMultiplexer _multiplexer = Substitute.For<IConnectionMultiplexer>();
    private readonly IDatabase _database = Substitute.For<IDatabase>();
    private readonly IServer _server = Substitute.For<IServer>();

    public RedisCacheClusterDetectionTests()
    {
        var endpoint = new DnsEndPoint("cluster-node", 6379);
        _multiplexer.GetDatabase(Arg.Any<int>(), Arg.Any<object?>()).Returns(_database);
        _multiplexer.GetEndPoints(Arg.Any<bool>()).Returns([endpoint]);
        _multiplexer.GetServer(endpoint, Arg.Any<object?>()).Returns(_server);
        // Each key lands in its own slot, so a cluster-aware removal sends one data/metadata pair per command.
        _multiplexer.HashSlot(Arg.Any<RedisKey>()).Returns(call => ((string)call.Arg<RedisKey>()!).Length);
        _server.ServerType.Returns(ServerType.Cluster);
        _database
            .ScriptEvaluateAsync(
                Arg.Any<string>(),
                Arg.Any<RedisKey[]>(),
                Arg.Any<RedisValue[]?>(),
                Arg.Any<CommandFlags>()
            )
            .Returns(call => RedisResult.Create((RedisValue)(call.Arg<RedisKey[]>().LongLength / 2)));
    }

    [Fact]
    public async Task should_split_removals_by_slot_when_the_cluster_connects_after_the_first_call()
    {
        using var scriptsLoader = new HeadlessRedisScriptsLoader(_multiplexer);
        using var cache = new RedisCache(
            new SystemJsonSerializer(),
            TimeProvider.System,
            new RedisCacheOptions { ConnectionMultiplexer = _multiplexer },
            scriptsLoader
        );

        // The first call runs while no node is connected yet.
        _server.IsConnected.Returns(false);
        await cache.RemoveAllAsync(["a", "bb"], AbortToken);

        // The node connects; the next call must see the cluster.
        _server.IsConnected.Returns(true);
        _database.ClearReceivedCalls();
        var removed = await cache.RemoveAllAsync(["a", "bb"], AbortToken);

        removed.Should().Be(2);
        await _database
            .Received(2)
            .ScriptEvaluateAsync(
                Arg.Any<string>(),
                Arg.Is<RedisKey[]>(keys => keys.Length == 2),
                Arg.Any<RedisValue[]?>(),
                Arg.Any<CommandFlags>()
            );
        await _database
            .DidNotReceive()
            .ScriptEvaluateAsync(
                Arg.Any<string>(),
                Arg.Is<RedisKey[]>(keys => keys.Length > 2),
                Arg.Any<RedisValue[]?>(),
                Arg.Any<CommandFlags>()
            );
    }

    [Fact]
    public async Task should_keep_one_removal_batch_when_a_connected_server_is_standalone()
    {
        _server.ServerType.Returns(ServerType.Standalone);
        _server.IsConnected.Returns(true);
        using var scriptsLoader = new HeadlessRedisScriptsLoader(_multiplexer);
        using var cache = new RedisCache(
            new SystemJsonSerializer(),
            TimeProvider.System,
            new RedisCacheOptions { ConnectionMultiplexer = _multiplexer },
            scriptsLoader
        );

        var removed = await cache.RemoveAllAsync(["a", "bb"], AbortToken);

        removed.Should().Be(2);
        await _database
            .Received(1)
            .ScriptEvaluateAsync(
                Arg.Any<string>(),
                Arg.Is<RedisKey[]>(keys => keys.Length == 4),
                Arg.Any<RedisValue[]?>(),
                Arg.Any<CommandFlags>()
            );
    }
}
