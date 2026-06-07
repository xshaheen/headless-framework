// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;
using Headless.Redis;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace Tests;

[Collection(nameof(RedisMembershipFixture))]
public sealed class RedisMembershipLuaTests(RedisMembershipFixture fixture) : TestBase
{
    [Fact]
    public async Task should_prune_known_state_without_deleting_generation_counter()
    {
        var cluster = _Cluster();
        var db = fixture.ConnectionMultiplexer.GetDatabase();
        await using var first = await fixture.CreateNodeAsync(cluster, "node-a", AbortToken);
        var firstIdentity = await first.Membership.RegisterAsync(AbortToken);
        var knownKey = _KnownKey(cluster);
        var liveKey = _LiveKey(cluster);
        var genKey = _GenKey(cluster, firstIdentity.NodeId);

        (await db.HashExistsAsync(knownKey, firstIdentity.ToString())).Should().BeTrue();
        (await db.StringGetAsync(genKey)).ToString().Should().Be("1");

        await TimeProvider.System.Delay(TimeSpan.FromMilliseconds(750), AbortToken);
        (await first.Membership.GetLivenessSnapshotAsync(AbortToken)).Should().BeEmpty();

        (await db.HashExistsAsync(knownKey, firstIdentity.ToString())).Should().BeFalse();
        (await db.SortedSetScoreAsync(liveKey, firstIdentity.ToString())).Should().BeNull();
        (await db.StringGetAsync(genKey)).ToString().Should().Be("1");

        await using var second = await fixture.CreateNodeAsync(cluster, "node-a", AbortToken);
        var secondIdentity = await second.Membership.RegisterAsync(AbortToken);
        var store = second.Services.GetRequiredService<IMembershipStore>();

        var staleAccepted = await store.HeartbeatAsync(firstIdentity, AbortToken);
        var live = await second.Membership.GetLiveNodesAsync(AbortToken);

        secondIdentity.Incarnation.Value.Should().Be(2);
        staleAccepted.Should().BeFalse();
        live.Should().Equal([secondIdentity]);
        (await db.StringGetAsync(genKey)).ToString().Should().Be("2");
    }

    [Fact]
    public async Task should_load_coordination_scripts_additively_with_existing_redis_scripts()
    {
        var db = fixture.ConnectionMultiplexer.GetDatabase();
        using var loader = new HeadlessRedisScriptsLoader(fixture.ConnectionMultiplexer);
        var key = (RedisKey)("script-additive:" + Guid.NewGuid().ToString("N"));

        await loader.LoadAsync(
            [
                RedisMembershipHeartbeatScriptDefinition.Instance,
                RedisMembershipReadScriptDefinition.Instance,
                RedisMembershipLeaveScriptDefinition.Instance,
                RedisMembershipCleanupScriptDefinition.Instance,
                IncrementWithExpireScriptDefinition.Instance,
                ReplaceIfEqualScriptDefinition.Instance,
            ],
            AbortToken
        );

        var incremented = await loader.EvaluateAsync(
            db,
            IncrementWithExpireScriptDefinition.Instance,
            new { key, value = 1L, expires = 30_000L },
            AbortToken
        );
        var replaced = await loader.EvaluateAsync(
            db,
            ReplaceIfEqualScriptDefinition.Instance,
            new { key, expected = "1", value = "2", expires = 30_000L },
            AbortToken
        );

        incremented.ToString().Should().Be("1");
        ((int)replaced).Should().Be(1);
        (await db.StringGetAsync(key)).ToString().Should().Be("2");

        await using var node = await fixture.CreateNodeAsync(_Cluster(), "node-a", AbortToken);
        var identity = await node.Membership.RegisterAsync(AbortToken);

        identity.Should().Be(new NodeIdentity(new NodeId("node-a"), new NodeIncarnation(1)));
    }

    private static string _Cluster()
    {
        return "native-" + Guid.NewGuid().ToString("N");
    }

    private static RedisKey _KnownKey(string cluster)
    {
        return $"coordination:{{{cluster}}}:known";
    }

    private static RedisKey _LiveKey(string cluster)
    {
        return $"coordination:{{{cluster}}}:live";
    }

    private static RedisKey _GenKey(string cluster, NodeId nodeId)
    {
        return $"coordination:{{{cluster}}}:gen:{nodeId.Value}";
    }
}
