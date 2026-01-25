// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Confluent.Kafka;
using Framework.Testing.Tests;
using Headless.Messaging.Kafka;

namespace Tests;

public sealed class MessagingKafkaOptionsTests : TestBase
{
    [Fact]
    public void should_require_servers_to_be_set()
    {
        // given, when
        var options = new MessagingKafkaOptions { Servers = "localhost:9092" };

        // then
        options.Servers.Should().Be("localhost:9092");
    }

    [Fact]
    public void should_have_default_connection_pool_size_of_10()
    {
        // given, when
        var options = new MessagingKafkaOptions { Servers = "localhost:9092" };

        // then
        options.ConnectionPoolSize.Should().Be(10);
    }

    [Fact]
    public void should_have_empty_main_config_by_default()
    {
        // given, when
        var options = new MessagingKafkaOptions { Servers = "localhost:9092" };

        // then
        options.MainConfig.Should().BeEmpty();
    }

    [Fact]
    public void should_allow_custom_connection_pool_size()
    {
        // given, when
        var options = new MessagingKafkaOptions
        {
            Servers = "localhost:9092",
            ConnectionPoolSize = 25,
        };

        // then
        options.ConnectionPoolSize.Should().Be(25);
    }

    [Fact]
    public void should_have_default_topic_options()
    {
        // given, when
        var options = new MessagingKafkaOptions { Servers = "localhost:9092" };

        // then
        options.TopicOptions.Should().NotBeNull();
        options.TopicOptions.NumPartitions.Should().Be(-1);
        options.TopicOptions.ReplicationFactor.Should().Be(-1);
    }

    [Fact]
    public void should_allow_custom_topic_options()
    {
        // given, when
        var options = new MessagingKafkaOptions
        {
            Servers = "localhost:9092",
            TopicOptions = new KafkaTopicOptions
            {
                NumPartitions = 3,
                ReplicationFactor = 2,
            },
        };

        // then
        options.TopicOptions.NumPartitions.Should().Be(3);
        options.TopicOptions.ReplicationFactor.Should().Be(2);
    }

    [Fact]
    public void should_have_default_retriable_error_codes()
    {
        // given, when
        var options = new MessagingKafkaOptions { Servers = "localhost:9092" };

        // then
        options.RetriableErrorCodes.Should().NotBeEmpty();
        options.RetriableErrorCodes.Should().Contain(ErrorCode.GroupLoadInProgress);
        options.RetriableErrorCodes.Should().Contain(ErrorCode.Local_Retry);
        options.RetriableErrorCodes.Should().Contain(ErrorCode.Local_TimedOut);
        options.RetriableErrorCodes.Should().Contain(ErrorCode.RequestTimedOut);
        options.RetriableErrorCodes.Should().Contain(ErrorCode.LeaderNotAvailable);
        options.RetriableErrorCodes.Should().Contain(ErrorCode.NotLeaderForPartition);
        options.RetriableErrorCodes.Should().Contain(ErrorCode.RebalanceInProgress);
        options.RetriableErrorCodes.Should().Contain(ErrorCode.NotCoordinatorForGroup);
        options.RetriableErrorCodes.Should().Contain(ErrorCode.NetworkException);
        options.RetriableErrorCodes.Should().Contain(ErrorCode.GroupCoordinatorNotAvailable);
    }

    [Fact]
    public void should_allow_custom_retriable_error_codes()
    {
        // given, when
        var options = new MessagingKafkaOptions
        {
            Servers = "localhost:9092",
            RetriableErrorCodes = [ErrorCode.Local_TimedOut, ErrorCode.RequestTimedOut],
        };

        // then
        options.RetriableErrorCodes.Should().HaveCount(2);
        options.RetriableErrorCodes.Should().Contain(ErrorCode.Local_TimedOut);
        options.RetriableErrorCodes.Should().Contain(ErrorCode.RequestTimedOut);
    }

    [Fact]
    public void should_allow_null_custom_headers_builder()
    {
        // given, when
        var options = new MessagingKafkaOptions { Servers = "localhost:9092" };

        // then
        options.CustomHeadersBuilder.Should().BeNull();
    }

    [Fact]
    public void should_allow_custom_headers_builder()
    {
        // given
        static List<KeyValuePair<string, string>> builder(
            ConsumeResult<string, byte[]> result,
            IServiceProvider provider
        ) => [new("custom-header", "custom-value")];

        // when
        var options = new MessagingKafkaOptions
        {
            Servers = "localhost:9092",
            CustomHeadersBuilder = builder,
        };

        // then
        options.CustomHeadersBuilder.Should().NotBeNull();
    }

    [Fact]
    public void should_allow_adding_main_config_entries()
    {
        // given
        var options = new MessagingKafkaOptions { Servers = "localhost:9092" };

        // when
        options.MainConfig["security.protocol"] = "SASL_SSL";
        options.MainConfig["sasl.mechanism"] = "PLAIN";

        // then
        options.MainConfig.Should().HaveCount(2);
        options.MainConfig["security.protocol"].Should().Be("SASL_SSL");
        options.MainConfig["sasl.mechanism"].Should().Be("PLAIN");
    }

    [Fact]
    public void GetDefaultRetriableErrorCodes_should_return_expected_codes()
    {
        // given, when
        var codes = MessagingKafkaOptions.GetDefaultRetriableErrorCodes();

        // then
        codes.Should().HaveCount(10);
        codes.Should().Contain(ErrorCode.GroupLoadInProgress);
        codes.Should().Contain(ErrorCode.GroupCoordinatorNotAvailable);
    }
}
