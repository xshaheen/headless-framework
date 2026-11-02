// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text;
using Headless.Blobs;
using Headless.Blobs.Redis;
using Headless.Serializer;
using Headless.Testing.Tests;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Tests;

/// <summary>
/// Redis Cluster refuses a script whose keys hash to different slots (CROSSSLOT). These tests record every script the
/// storage sends and check that its keys share one slot under the Redis Cluster hash-tag rule.
/// </summary>
public sealed class RedisBlobStorageClusterSlotTests : TestBase
{
    private readonly IDatabase _database = Substitute.For<IDatabase>();
    private readonly IConnectionMultiplexer _multiplexer = Substitute.For<IConnectionMultiplexer>();
    private readonly List<RedisKey[]> _scriptKeys = [];

    public RedisBlobStorageClusterSlotTests()
    {
        _multiplexer.GetDatabase(Arg.Any<int>(), Arg.Any<object?>()).Returns(_database);
        _database
            .ScriptEvaluateAsync(
                Arg.Any<string>(),
                Arg.Any<RedisKey[]?>(),
                Arg.Any<RedisValue[]?>(),
                Arg.Any<CommandFlags>()
            )
            .Returns(call =>
            {
                var keys = call.ArgAt<RedisKey[]?>(1) ?? [];
                _scriptKeys.Add(keys);

                // The cross-slot read returns the source's { blob, info } pair; every other script reports success.
                return call.ArgAt<string>(0).Contains("return { blobData", StringComparison.Ordinal)
                    ? RedisResult.Create([RedisResult.Create((RedisValue)"blob"), RedisResult.Create((RedisValue)"{}")])
                    : RedisResult.Create(1);
            });
        _database.HashExistsAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<CommandFlags>()).Returns(false);
    }

    [Fact]
    public async Task should_keep_every_write_script_in_one_cluster_slot_when_the_container_is_the_same()
    {
        // given
        await using var storage = _CreateStorage();
        var source = new BlobLocation("reports", "a.txt");
        var destination = new BlobLocation("reports", "b.txt");

        // when
        await using (var content = new MemoryStream(Encoding.UTF8.GetBytes("data")))
        {
            await storage.UploadAsync(source, content, cancellationToken: AbortToken);
        }

        await storage.CopyAsync(source, destination, AbortToken);
        await storage.MoveAsync(destination, new BlobLocation("reports", "c.txt"), AbortToken);
        await storage.DeleteAsync(source, AbortToken);

        // then
        _scriptKeys.Should().HaveCount(4);
        _scriptKeys.Should().AllSatisfy(keys => keys.Select(_ClusterHashInput).Distinct().Should().ContainSingle());
    }

    [Fact]
    public async Task should_split_a_cross_container_move_into_single_slot_scripts_when_the_slots_differ()
    {
        // given: the cluster places each container in its own slot
        _multiplexer.HashSlot(Arg.Any<RedisKey>()).Returns(call => _ClusterHashInput(call.ArgAt<RedisKey>(0)).Length);
        await using var storage = _CreateStorage();

        // when
        var moved = await storage.MoveAsync(
            new BlobLocation("inbox", "a.txt"),
            new BlobLocation("archive-2026", "a.txt"),
            AbortToken
        );

        // then: read the source, write the destination, delete the source — one slot each
        moved.Should().BeTrue();
        _scriptKeys.Should().HaveCount(3);
        _scriptKeys.Should().AllSatisfy(keys => keys.Select(_ClusterHashInput).Distinct().Should().ContainSingle());
        _scriptKeys.Select(keys => _ClusterHashInput(keys[0])).Should().Equal("inbox", "archive-2026", "inbox");
    }

    [Fact]
    public async Task should_split_a_cross_container_copy_into_single_slot_scripts_when_the_slots_differ()
    {
        // given
        _multiplexer.HashSlot(Arg.Any<RedisKey>()).Returns(call => _ClusterHashInput(call.ArgAt<RedisKey>(0)).Length);
        await using var storage = _CreateStorage();

        // when
        var copied = await storage.CopyAsync(
            new BlobLocation("inbox", "a.txt"),
            new BlobLocation("archive-2026", "a.txt"),
            AbortToken
        );

        // then
        copied.Should().BeTrue();
        _scriptKeys.Select(keys => _ClusterHashInput(keys[0])).Should().Equal("inbox", "archive-2026");
        _scriptKeys.Should().AllSatisfy(keys => keys.Select(_ClusterHashInput).Distinct().Should().ContainSingle());
    }

    [Theory]
    [InlineData("a{b")]
    [InlineData("}ab")]
    public async Task should_reject_a_container_whose_name_would_break_the_hash_tag(string container)
    {
        // given
        await using var storage = _CreateStorage();

        // when
        var act = async () => await storage.DeleteAsync(new BlobLocation(container, "a.txt"), AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentException>();
    }

    private RedisBlobStorage _CreateStorage()
    {
        return new RedisBlobStorage(
            Options.Create(new RedisBlobStorageOptions { ConnectionMultiplexer = _multiplexer }),
            new SystemJsonSerializer(),
            new CrossOsNamingNormalizer()
        );
    }

    /// <summary>
    /// The part of a key Redis Cluster hashes: the text between the first <c>{</c> and the next <c>}</c> when that
    /// text is non-empty, otherwise the whole key.
    /// </summary>
    private static string _ClusterHashInput(RedisKey key)
    {
        var value = key.ToString();
        var open = value.IndexOf('{', StringComparison.Ordinal);

        if (open < 0)
        {
            return value;
        }

        var close = value.IndexOf('}', open + 1);

        return close > open + 1 ? value[(open + 1)..close] : value;
    }
}
