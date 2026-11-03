// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Blobs;
using Headless.Blobs.Redis;
using Headless.Serializer;
using Headless.Testing.Tests;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using StackExchange.Redis;

namespace Tests;

public sealed class RedisBlobStorageRetryTests : TestBase
{
    private readonly IDatabase _database = Substitute.For<IDatabase>();
    private readonly IConnectionMultiplexer _multiplexer = Substitute.For<IConnectionMultiplexer>();

    [Fact]
    public async Task should_retry_a_transient_redis_failure_without_advancing_the_app_clock()
    {
        // given — the storage's app clock is a FakeTimeProvider this test never advances
        var database = Substitute.For<IDatabase>();
        var calls = 0;
        database
            .HashExistsAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<CommandFlags>())
            .Returns(_ =>
                ++calls == 1
                    ? Task.FromException<bool>(
                        new RedisConnectionException(
                            ConnectionFailureType.SocketFailure,
                            CommandFlags.None,
                            "transient",
                            innerException: null,
                            CommandStatus.Unknown
                        )
                    )
                    : Task.FromResult(true)
            );
        var multiplexer = Substitute.For<IConnectionMultiplexer>();
        multiplexer.GetDatabase(Arg.Any<int>(), Arg.Any<object?>()).Returns(database);
        await using var storage = new RedisBlobStorage(
            Options.Create(new RedisBlobStorageOptions { ConnectionMultiplexer = multiplexer }),
            new SystemJsonSerializer(),
            new CrossOsNamingNormalizer(),
            new FakeTimeProvider()
        );

        // when — the back-off waits real time, so the call completes on its own
        var exists = await storage
            .ExistsAsync(new BlobLocation("container", "file.txt"), AbortToken)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(30), AbortToken);

        // then
        exists.Should().BeTrue();
        calls.Should().Be(2);
    }

    [Fact]
    public async Task should_not_retry_a_move_whose_script_timed_out_after_it_was_sent()
    {
        // given — the server ran the move but the reply timed out; a re-run would find the source gone and answer 0
        var scripts = new List<string>();
        _database
            .ScriptEvaluateAsync(
                Arg.Any<string>(),
                Arg.Any<RedisKey[]?>(),
                Arg.Any<RedisValue[]?>(),
                Arg.Any<CommandFlags>()
            )
            .Returns(call =>
            {
                scripts.Add(call.ArgAt<string>(0));

                return scripts.Count == 1
                    ? Task.FromException<RedisResult>(_Timeout(CommandStatus.Sent))
                    : Task.FromResult(RedisResult.Create(0));
            });
        await using var storage = _CreateStorage();

        // when
        var act = async () =>
            await storage.MoveAsync(new BlobLocation("c", "a.txt"), new BlobLocation("c", "b.txt"), AbortToken);

        // then — the caller sees the ambiguous failure instead of a false "source not found"
        await act.Should().ThrowAsync<RedisTimeoutException>();
        scripts.Should().ContainSingle();
    }

    [Fact]
    public async Task should_not_retry_a_delete_whose_script_lost_its_connection_after_it_was_sent()
    {
        // given
        var calls = 0;
        _database
            .ScriptEvaluateAsync(
                Arg.Any<string>(),
                Arg.Any<RedisKey[]?>(),
                Arg.Any<RedisValue[]?>(),
                Arg.Any<CommandFlags>()
            )
            .Returns(_ =>
                ++calls == 1
                    ? Task.FromException<RedisResult>(_ConnectionLost(CommandStatus.Sent))
                    : Task.FromResult(RedisResult.Create(0))
            );
        await using var storage = _CreateStorage();

        // when
        var act = async () => await storage.DeleteAsync(new BlobLocation("c", "a.txt"), AbortToken);

        // then
        await act.Should().ThrowAsync<RedisConnectionException>();
        calls.Should().Be(1);
    }

    [Fact]
    public async Task should_retry_a_move_the_client_never_sent()
    {
        // given — the command was still queued when the connection failed, so the server never ran it
        var calls = 0;
        _database
            .ScriptEvaluateAsync(
                Arg.Any<string>(),
                Arg.Any<RedisKey[]?>(),
                Arg.Any<RedisValue[]?>(),
                Arg.Any<CommandFlags>()
            )
            .Returns(_ =>
                ++calls == 1
                    ? Task.FromException<RedisResult>(_Timeout(CommandStatus.WaitingInBacklog))
                    : Task.FromResult(RedisResult.Create(1))
            );
        await using var storage = _CreateStorage();

        // when
        var moved = await storage
            .MoveAsync(new BlobLocation("c", "a.txt"), new BlobLocation("c", "b.txt"), AbortToken)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(30), AbortToken);

        // then
        moved.Should().BeTrue();
        calls.Should().Be(2);
    }

    [Fact]
    public async Task should_not_retry_a_cross_slot_move_write_that_timed_out_after_it_was_sent()
    {
        // given — each container in its own slot; the destination write lands, then its reply times out
        _multiplexer.HashSlot(Arg.Any<RedisKey>()).Returns(call => call.ArgAt<RedisKey>(0).ToString().Length);
        var writes = 0;
        _database
            .ScriptEvaluateAsync(
                Arg.Any<string>(),
                Arg.Any<RedisKey[]?>(),
                Arg.Any<RedisValue[]?>(),
                Arg.Any<CommandFlags>()
            )
            .Returns(call =>
                _IsReadScript(call.ArgAt<string>(0)) ? Task.FromResult(_SourcePair())
                : ++writes == 1 ? Task.FromException<RedisResult>(_Timeout(CommandStatus.Sent))
                : Task.FromResult(RedisResult.Create(-1))
            );
        await using var storage = _CreateStorage();

        // when
        var act = async () =>
            await storage.MoveAsync(
                new BlobLocation("inbox", "a.txt"),
                new BlobLocation("archive-2026", "a.txt"),
                AbortToken
            );

        // then — no re-run reports the destination it just wrote as "occupied"
        await act.Should().ThrowAsync<RedisTimeoutException>();
        writes.Should().Be(1);
    }

    [Fact]
    public async Task should_retry_a_cross_slot_copy_write_that_timed_out_after_it_was_sent()
    {
        // given — an overwriting write re-run over its own effect still answers success
        _multiplexer.HashSlot(Arg.Any<RedisKey>()).Returns(call => call.ArgAt<RedisKey>(0).ToString().Length);
        var writes = 0;
        _database
            .ScriptEvaluateAsync(
                Arg.Any<string>(),
                Arg.Any<RedisKey[]?>(),
                Arg.Any<RedisValue[]?>(),
                Arg.Any<CommandFlags>()
            )
            .Returns(call =>
                _IsReadScript(call.ArgAt<string>(0)) ? Task.FromResult(_SourcePair())
                : ++writes == 1 ? Task.FromException<RedisResult>(_Timeout(CommandStatus.Sent))
                : Task.FromResult(RedisResult.Create(1))
            );
        await using var storage = _CreateStorage();

        // when
        var copied = await storage
            .CopyAsync(new BlobLocation("inbox", "a.txt"), new BlobLocation("archive-2026", "a.txt"), AbortToken)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(30), AbortToken);

        // then
        copied.Should().BeTrue();
        writes.Should().Be(2);
    }

    private RedisBlobStorage _CreateStorage()
    {
        _multiplexer.GetDatabase(Arg.Any<int>(), Arg.Any<object?>()).Returns(_database);

        return new RedisBlobStorage(
            Options.Create(new RedisBlobStorageOptions { ConnectionMultiplexer = _multiplexer }),
            new SystemJsonSerializer(),
            new CrossOsNamingNormalizer(),
            new FakeTimeProvider()
        );
    }

    private static RedisTimeoutException _Timeout(CommandStatus status) => new(CommandFlags.None, "timed out", status);

    private static RedisConnectionException _ConnectionLost(CommandStatus status) =>
        new(ConnectionFailureType.SocketClosed, CommandFlags.None, "connection lost", innerException: null, status);

    private static bool _IsReadScript(string script) => script.Contains("return { blobData", StringComparison.Ordinal);

    private static RedisResult _SourcePair() =>
        RedisResult.Create([RedisResult.Create((RedisValue)"blob"), RedisResult.Create((RedisValue)"{}")]);
}
