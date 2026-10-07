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
}
