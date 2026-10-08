// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Confluent.Kafka;
using Headless.Messaging.Kafka;
using Headless.Testing.Tests;

namespace Tests;

public sealed class KafkaMessagingOptionsTests : TestBase
{
    [Fact]
    public void should_require_servers_to_be_set()
    {
        // given, when
        var options = new KafkaMessagingOptions { Servers = "localhost:9092" };

        // then
        options.Servers.Should().Be("localhost:9092");
    }

    [Fact]
    public void should_have_default_connection_pool_size_of_10()
    {
        // given, when
        var options = new KafkaMessagingOptions { Servers = "localhost:9092" };

        // then
        options.ConnectionPoolSize.Should().Be(10);
    }

    [Fact]
    public void should_have_default_topic_options()
    {
        // given, when
        var options = new KafkaMessagingOptions { Servers = "localhost:9092" };

        // then
        options.TopicOptions.Should().NotBeNull();
        options.TopicOptions.NumPartitions.Should().Be(-1);
        options.TopicOptions.ReplicationFactor.Should().Be(-1);
    }

    [Fact]
    public void should_have_default_retriable_error_codes()
    {
        // given, when
        var options = new KafkaMessagingOptions { Servers = "localhost:9092" };

        // then
        options.RetriableErrorCodes.Should().NotBeEmpty();
        options.RetriableErrorCodes.Should().Contain((int)ErrorCode.GroupLoadInProgress);
        options.RetriableErrorCodes.Should().Contain((int)ErrorCode.Local_Retry);
        options.RetriableErrorCodes.Should().Contain((int)ErrorCode.Local_TimedOut);
        options.RetriableErrorCodes.Should().Contain((int)ErrorCode.RequestTimedOut);
        options.RetriableErrorCodes.Should().Contain((int)ErrorCode.LeaderNotAvailable);
        options.RetriableErrorCodes.Should().Contain((int)ErrorCode.NotLeaderForPartition);
        options.RetriableErrorCodes.Should().Contain((int)ErrorCode.RebalanceInProgress);
        options.RetriableErrorCodes.Should().Contain((int)ErrorCode.NotCoordinatorForGroup);
        options.RetriableErrorCodes.Should().Contain((int)ErrorCode.NetworkException);
        options.RetriableErrorCodes.Should().Contain((int)ErrorCode.GroupCoordinatorNotAvailable);
    }
}
