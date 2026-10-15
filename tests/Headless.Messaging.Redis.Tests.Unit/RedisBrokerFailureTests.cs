// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Redis;
using Headless.Testing.Tests;
using StackExchange.Redis;

namespace Tests;

public sealed class RedisBrokerFailureTests : TestBase
{
    [Fact]
    public void should_count_a_lost_connection_or_a_timeout()
    {
        RedisBrokerFailures
            .IsUnreachable(
                new RedisConnectionException(
                    ConnectionFailureType.UnableToConnect,
                    CommandFlags.None,
                    "down",
                    null,
                    CommandStatus.Unknown
                )
            )
            .Should()
            .BeTrue();
        RedisBrokerFailures
            .IsUnreachable(new RedisTimeoutException(CommandFlags.None, "slow", CommandStatus.Sent))
            .Should()
            .BeTrue();
        RedisBrokerFailures
            .IsUnreachable(new RedisServerException(RedisErrorKind.Loading, CommandFlags.None, "LOADING"))
            .Should()
            .BeTrue();
    }

    [Fact]
    public void should_not_count_an_error_the_server_answered_with()
    {
        RedisBrokerFailures
            .IsUnreachable(new RedisServerException(RedisErrorKind.WrongType, CommandFlags.None, "WRONGTYPE"))
            .Should()
            .BeFalse();
    }
}
