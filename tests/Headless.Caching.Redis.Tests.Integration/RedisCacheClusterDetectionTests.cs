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
/// from a probe made while every endpoint was down. A substituted multiplexer stands in for the cluster because the
/// fixture runs a single Redis node.
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
        // Each key lands in its own slot, so a cluster-aware removal issues one DEL per key.
        _multiplexer.HashSlot(Arg.Any<RedisKey>()).Returns(call => ((string)call.Arg<RedisKey>()!).Length);
        _server.ServerType.Returns(ServerType.Cluster);
        _database
            .KeyDeleteAsync(Arg.Any<RedisKey[]>(), Arg.Any<CommandFlags>())
            .Returns(call => (long)call.Arg<RedisKey[]>().Length);
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
            .KeyDeleteAsync(Arg.Is<RedisKey[]>(keys => keys.Length == 1), Arg.Any<CommandFlags>());
        await _database
            .DidNotReceive()
            .KeyDeleteAsync(Arg.Is<RedisKey[]>(keys => keys.Length > 1), Arg.Any<CommandFlags>());
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
            .KeyDeleteAsync(Arg.Is<RedisKey[]>(keys => keys.Length == 2), Arg.Any<CommandFlags>());
    }
}
