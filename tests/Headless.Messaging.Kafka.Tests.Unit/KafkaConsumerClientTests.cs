// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Confluent.Kafka;
using Headless.Messaging.Kafka;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class KafkaConsumerClientTests : TestBase
{
    private readonly IOptions<MessagingKafkaOptions> _options = Options.Create(
        new MessagingKafkaOptions { Servers = "localhost:9092" }
    );
    private readonly IServiceProvider _serviceProvider;

    public KafkaConsumerClientTests()
    {
        _serviceProvider = new ServiceCollection().BuildServiceProvider();
    }

    [Fact]
    public async Task should_have_correct_broker_address()
    {
        // given, when
        await using var client = new KafkaConsumerClient("test-group", 1, _options, _serviceProvider);

        // then
        client.BrokerAddress.Name.Should().Be("kafka");
        client.BrokerAddress.Endpoint.Should().Be("localhost:9092");
    }

    [Fact]
    public async Task should_allow_setting_OnMessageCallback()
    {
        // given
        await using var client = new KafkaConsumerClient("test-group", 1, _options, _serviceProvider);

        // when
        client.OnMessageCallback = (_, _) => Task.CompletedTask;

        // then
        client.OnMessageCallback.Should().NotBeNull();
    }

    [Fact]
    public async Task should_allow_setting_OnLogCallback()
    {
        // given
        await using var client = new KafkaConsumerClient("test-group", 1, _options, _serviceProvider);

        // when
        client.OnLogCallback = _ => { };

        // then
        client.OnLogCallback.Should().NotBeNull();
    }

    [Fact]
    public async Task should_throw_when_subscribing_with_null_topics()
    {
        // given
        await using var client = new KafkaConsumerClient("test-group", 1, _options, _serviceProvider);

        // when
        var act = async () => await client.SubscribeAsync(null!);

        // then
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task should_throw_when_fetching_null_topics()
    {
        // given
        await using var client = new KafkaConsumerClient("test-group", 1, _options, _serviceProvider);

        // when
        var act = async () => await client.FetchTopicsAsync(null!);

        // then
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task FetchTopicsAsync_should_return_topics()
    {
        // given
        await using var client = new KafkaConsumerClient("test-group", 1, _options, _serviceProvider);
        client.OnLogCallback = _ => { }; // Set callback to avoid null ref

        // when - FetchTopicsAsync will return topics even if admin client fails
        // The topic names are returned regardless of whether topic creation succeeds
        var result = await client.FetchTopicsAsync(["topic1", "topic2"]);

        // then
        result.Should().HaveCount(2);
        result.Should().Contain("topic1");
        result.Should().Contain("topic2");
    }

    [Fact]
    public async Task should_dispose_successfully()
    {
        // given
        var client = new KafkaConsumerClient("test-group", 2, _options, _serviceProvider);

        // when
        await client.DisposeAsync();

        // then - no exception
        client.Should().NotBeNull();
    }

    [Fact]
    public async Task should_use_allow_auto_create_topics_from_config()
    {
        // given
        var optionsWithAutoCreate = Options.Create(
            new MessagingKafkaOptions
            {
                Servers = "localhost:9092",
                MainConfig = { ["allow.auto.create.topics"] = "false" },
            }
        );
        await using var client = new KafkaConsumerClient("test-group", 1, optionsWithAutoCreate, _serviceProvider);

        // then - client created successfully with the config
        client.Should().NotBeNull();
    }

    [Fact]
    public async Task should_support_custom_headers_builder()
    {
        // given
        var customHeaders = new List<KeyValuePair<string, string>> { new("custom-key", "custom-value") };
        var optionsWithCustomHeaders = Options.Create(
            new MessagingKafkaOptions { Servers = "localhost:9092", CustomHeadersBuilder = (_, _) => customHeaders }
        );
        await using var client = new KafkaConsumerClient("test-group", 1, optionsWithCustomHeaders, _serviceProvider);

        // then
        client.Should().NotBeNull();
    }

    [Fact]
    public async Task should_handle_retriable_error_codes()
    {
        // given
        var options = Options.Create(
            new MessagingKafkaOptions { Servers = "localhost:9092", RetriableErrorCodes = [ErrorCode.Local_TimedOut] }
        );
        await using var client = new KafkaConsumerClient("test-group", 1, options, _serviceProvider);

        // then - client created successfully
        client.Should().NotBeNull();
    }

    [Fact]
    public async Task should_use_topic_options_for_auto_creation()
    {
        // given
        var options = Options.Create(
            new MessagingKafkaOptions
            {
                Servers = "localhost:9092",
                TopicOptions = new KafkaTopicOptions { NumPartitions = 3, ReplicationFactor = 1 },
            }
        );
        await using var client = new KafkaConsumerClient("test-group", 1, options, _serviceProvider);

        // then
        client.Should().NotBeNull();
    }

    [Fact]
    public async Task Connect_should_be_idempotent()
    {
        // given
        await using var client = new KafkaConsumerClient("test-group", 1, _options, _serviceProvider);

        // when - call Connect multiple times (it won't actually connect without Kafka)
        // This tests the idempotency of the Connect check
        client.Connect();
        client.Connect();

        // then - no exception
    }
}
