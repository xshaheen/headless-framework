// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;
using Headless.Coordination.Redis;
using Headless.Coordination.Redis.Scripts;
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
        var generationField = _GenerationField(firstIdentity.NodeId);

        (await db.HashExistsAsync(knownKey, firstIdentity.ToString())).Should().BeTrue();
        (await db.HashGetAsync(knownKey, generationField)).ToString().Should().Be("1");
        (await db.StringGetAsync(genKey)).ToString().Should().Be("1");

        // The fixture's RedisKnownNodeRetention (600ms) is clamped up to at least the harness
        // DeadThreshold + DeadRetentionWindow, so wait past the harness prune threshold for the :known prune.
        await TimeProvider.System.Delay(CoordinationFixtureExtensions.AfterPruneWait, AbortToken);
        (await first.Membership.GetLivenessSnapshotAsync(AbortToken)).Should().BeEmpty();

        (await db.HashExistsAsync(knownKey, firstIdentity.ToString())).Should().BeFalse();
        (await db.HashGetAsync(knownKey, generationField)).ToString().Should().Be("1");
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
        (await db.HashGetAsync(knownKey, generationField)).ToString().Should().Be("2");
        (await db.StringGetAsync(genKey)).ToString().Should().Be("2");
    }

    [Fact]
    public async Task should_sweep_orphaned_generation_mirror_during_cleanup()
    {
        var cluster = _Cluster();
        var db = fixture.ConnectionMultiplexer.GetDatabase();
        await using var node = await fixture.CreateNodeAsync(cluster, "node-a", AbortToken);
        var identity = await node.Membership.RegisterAsync(AbortToken);
        var knownKey = _KnownKey(cluster);
        var genKey = _GenKey(cluster, identity.NodeId);
        var generationField = _GenerationField(identity.NodeId);

        (await db.HashExistsAsync(knownKey, identity.ToString())).Should().BeTrue();
        (await db.HashGetAsync(knownKey, generationField)).ToString().Should().Be("1");
        (await db.StringGetAsync(genKey)).ToString().Should().Be("1");

        // The fixture's RedisKnownNodeRetention (600ms) is clamped up to at least the harness
        // DeadThreshold + DeadRetentionWindow, so wait past the harness prune threshold for the :known prune.
        await TimeProvider.System.Delay(CoordinationFixtureExtensions.AfterPruneWait, AbortToken);
        await node.Services.GetRequiredService<RedisMembershipStore>().CleanupAsync(AbortToken);

        (await db.HashExistsAsync(knownKey, identity.ToString())).Should().BeFalse();
        (await db.HashExistsAsync(knownKey, generationField)).Should().BeFalse();
        (await db.StringGetAsync(genKey)).ToString().Should().Be("1");
    }

    [Fact]
    public async Task should_retain_generation_mirror_when_a_newer_incarnation_is_still_active_during_cleanup()
    {
        var cluster = _Cluster();
        var db = fixture.ConnectionMultiplexer.GetDatabase();

        // Incarnation 1 is allowed to expire past the prune threshold before incarnation 2 (same
        // node-id) registers: an expired member cannot be revived by a heartbeat, it must re-register.
        await using var first = await fixture.CreateNodeAsync(cluster, "node-a", AbortToken);
        var firstIdentity = await first.Membership.RegisterAsync(AbortToken);

        await TimeProvider.System.Delay(CoordinationFixtureExtensions.AfterPruneWait, AbortToken);

        await using var second = await fixture.CreateNodeAsync(cluster, "node-a", AbortToken);
        var secondIdentity = await second.Membership.RegisterAsync(AbortToken);

        var knownKey = _KnownKey(cluster);
        var generationField = _GenerationField(firstIdentity.NodeId);

        secondIdentity.Incarnation.Value.Should().Be(2);
        (await db.HashExistsAsync(knownKey, firstIdentity.ToString())).Should().BeTrue();
        (await db.HashExistsAsync(knownKey, secondIdentity.ToString())).Should().BeTrue();
        (await db.HashGetAsync(knownKey, generationField)).ToString().Should().Be("2");

        // Incarnation 2 is inside its liveness window, so its heartbeat is accepted and it survives the prune.
        (await second.Services.GetRequiredService<IMembershipStore>().HeartbeatAsync(secondIdentity, AbortToken))
            .Should()
            .BeTrue();

        await second.Services.GetRequiredService<RedisMembershipStore>().CleanupAsync(AbortToken);

        // Incarnation 1 is pruned, but the live incarnation 2 keeps the shared node-id's mirror in place.
        (await db.HashExistsAsync(knownKey, firstIdentity.ToString()))
            .Should()
            .BeFalse();
        (await db.HashExistsAsync(knownKey, secondIdentity.ToString())).Should().BeTrue();
        (await db.HashGetAsync(knownKey, generationField)).ToString().Should().Be("2");
    }

    [Fact]
    public async Task should_not_return_retained_member_when_generation_mirror_advances_without_member_payload()
    {
        var cluster = _Cluster();
        var db = fixture.ConnectionMultiplexer.GetDatabase();
        await using var node = await fixture.CreateNodeAsync(cluster, "node-a", AbortToken);
        var identity = await node.Membership.RegisterAsync(AbortToken);
        var knownKey = _KnownKey(cluster);
        var genKey = _GenKey(cluster, identity.NodeId);
        var generationField = _GenerationField(identity.NodeId);

        _ = await db.StringIncrementAsync(genKey);
        _ = await db.HashSetAsync(knownKey, generationField, "2");

        var snapshot = await node.Membership.GetLivenessSnapshotAsync(AbortToken);
        var staleAccepted = await node
            .Services.GetRequiredService<IMembershipStore>()
            .HeartbeatAsync(identity, AbortToken);

        snapshot.Should().NotContain(x => x.Identity == identity);
        staleAccepted.Should().BeFalse();
    }

    [Fact]
    public async Task should_repair_missing_generation_mirror_during_read()
    {
        var cluster = _Cluster();
        var db = fixture.ConnectionMultiplexer.GetDatabase();
        await using var node = await fixture.CreateNodeAsync(cluster, "node-a", AbortToken);
        var identity = await node.Membership.RegisterAsync(AbortToken);
        var knownKey = _KnownKey(cluster);
        var generationField = _GenerationField(identity.NodeId);

        _ = await db.HashDeleteAsync(knownKey, generationField);

        var snapshot = await node.Membership.GetLivenessSnapshotAsync(AbortToken);

        snapshot.Should().ContainSingle(x => x.Identity == identity && x.State == NodeLivenessState.Alive);
        (await db.HashGetAsync(knownKey, generationField)).ToString().Should().Be("1");
    }

    [Fact]
    public async Task should_treat_generation_prefix_node_ids_as_members_when_payload_is_json()
    {
        var cluster = _Cluster();
        await using var node = await fixture.CreateNodeAsync(cluster, "__gen:node-a", AbortToken);
        var identity = await node.Membership.RegisterAsync(AbortToken);

        var snapshot = await node.Membership.GetLivenessSnapshotAsync(AbortToken);

        snapshot.Should().ContainSingle(x => x.Identity == identity && x.State == NodeLivenessState.Alive);
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
                RedisMembershipAllocateIncarnationScriptDefinition.Instance,
                RedisMembershipReadScriptDefinition.Instance,
                RedisMembershipLeaveScriptDefinition.Instance,
                RedisMembershipCleanupScriptDefinition.Instance,
                ExistingRedisScriptDefinition.Instance,
            ],
            AbortToken
        );

        var executed = await loader.EvaluateAsync(
            db,
            ExistingRedisScriptDefinition.Instance,
            new { key, value = "2" },
            AbortToken
        );

        ((int)executed).Should().Be(1);
        (await db.StringGetAsync(key)).ToString().Should().Be("2");

        await using var node = await fixture.CreateNodeAsync(_Cluster(), "node-a", AbortToken);
        var identity = await node.Membership.RegisterAsync(AbortToken);

        identity.Should().Be(new NodeIdentity(new NodeId("node-a"), new NodeIncarnation(1)));
    }

    [Fact]
    public async Task should_classify_targeted_node_liveness_across_states()
    {
        var cluster = _Cluster();
        await using var node = await fixture.CreateNodeAsync(cluster, "node-a", AbortToken);
        var identity = await node.Membership.RegisterAsync(AbortToken);
        var store = node.Services.GetRequiredService<IMembershipStore>();

        // Alive immediately after register.
        (await store.ReadNodeLivenessAsync(identity, AbortToken))
            .Should()
            .Be(NodeLivenessState.Alive);

        // Aged into the suspicion band -> Suspected.
        await TimeProvider.System.Delay(CoordinationFixtureExtensions.SuspectedWait, AbortToken);
        (await store.ReadNodeLivenessAsync(identity, AbortToken)).Should().Be(NodeLivenessState.Suspected);

        // Aged past the dead threshold but still inside the retention window -> Dead.
        await TimeProvider.System.Delay(
            CoordinationFixtureExtensions.DeadButRetainedWait - CoordinationFixtureExtensions.SuspectedWait,
            AbortToken
        );
        (await store.ReadNodeLivenessAsync(identity, AbortToken)).Should().Be(NodeLivenessState.Dead);
    }

    [Fact]
    public async Task should_return_dead_for_targeted_read_after_graceful_leave()
    {
        var cluster = _Cluster();
        await using var node = await fixture.CreateNodeAsync(cluster, "node-a", AbortToken);
        var identity = await node.Membership.RegisterAsync(AbortToken);
        var store = node.Services.GetRequiredService<IMembershipStore>();

        await store.LeaveAsync(identity, AbortToken);

        (await store.ReadNodeLivenessAsync(identity, AbortToken)).Should().Be(NodeLivenessState.Dead);
    }

    [Fact]
    public async Task should_return_null_for_targeted_read_of_stale_and_unregistered_identities()
    {
        var cluster = _Cluster();
        await using var first = await fixture.CreateNodeAsync(cluster, "node-a", AbortToken);
        var firstIdentity = await first.Membership.RegisterAsync(AbortToken);

        await using var second = await fixture.CreateNodeAsync(cluster, "node-a", AbortToken);
        var secondIdentity = await second.Membership.RegisterAsync(AbortToken);
        var store = second.Services.GetRequiredService<IMembershipStore>();
        var unregistered = new NodeIdentity(new NodeId("node-z"), new NodeIncarnation(1));

        (await store.ReadNodeLivenessAsync(firstIdentity, AbortToken)).Should().BeNull();
        (await store.ReadNodeLivenessAsync(unregistered, AbortToken)).Should().BeNull();
        (await store.ReadNodeLivenessAsync(secondIdentity, AbortToken)).Should().Be(NodeLivenessState.Alive);
    }

    [Fact]
    public async Task should_resolve_generation_via_gen_key_without_backfilling_mirror_on_targeted_read()
    {
        var cluster = _Cluster();
        var db = fixture.ConnectionMultiplexer.GetDatabase();
        await using var node = await fixture.CreateNodeAsync(cluster, "node-a", AbortToken);
        var identity = await node.Membership.RegisterAsync(AbortToken);
        var store = node.Services.GetRequiredService<IMembershipStore>();
        var knownKey = _KnownKey(cluster);
        var generationField = _GenerationField(identity.NodeId);

        // Drop the generation mirror: the targeted read must fall back to the authoritative gen: key.
        _ = await db.HashDeleteAsync(knownKey, generationField);

        var state = await store.ReadNodeLivenessAsync(identity, AbortToken);

        state.Should().Be(NodeLivenessState.Alive);
        // Unlike the snapshot read, the targeted read is write-free: it must NOT repair the mirror.
        (await db.HashExistsAsync(knownKey, generationField))
            .Should()
            .BeFalse();
    }

    [Fact]
    public async Task should_return_null_without_pruning_for_retention_expired_targeted_read()
    {
        var cluster = _Cluster();
        var db = fixture.ConnectionMultiplexer.GetDatabase();
        await using var node = await fixture.CreateNodeAsync(cluster, "node-a", AbortToken);
        var identity = await node.Membership.RegisterAsync(AbortToken);
        var store = node.Services.GetRequiredService<IMembershipStore>();
        var knownKey = _KnownKey(cluster);

        // Age past the operational prune window, then read the targeted path FIRST — before any snapshot or
        // cleanup tick could delete the member. The targeted path reports absence (null) without writing.
        await TimeProvider.System.Delay(CoordinationFixtureExtensions.AfterPruneWait, AbortToken);

        var state = await store.ReadNodeLivenessAsync(identity, AbortToken);

        state.Should().BeNull();
        // The retention-expired member must still be present in :known: the targeted read classifies, never prunes.
        (await db.HashExistsAsync(knownKey, identity.ToString()))
            .Should()
            .BeTrue();
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

    private static RedisValue _GenerationField(NodeId nodeId)
    {
        return "__gen:" + nodeId.Value;
    }

    // Stands in for an arbitrary non-coordination Redis script to prove the loader warms coordination
    // scripts additively alongside scripts owned by other packages.
    private sealed class ExistingRedisScriptDefinition : RedisScriptDefinition
    {
        public static ExistingRedisScriptDefinition Instance { get; } = new();

        private ExistingRedisScriptDefinition()
            : base("return redis.call('set', @key, @value) and 1 or 0") { }
    }
}
