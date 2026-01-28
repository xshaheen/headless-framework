// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Messages;
using Headless.Messaging.RedisStreams;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Tests;

/// <summary>
/// Unit tests for <see cref="RedisConsumerClient"/>.
/// </summary>
public sealed class RedisConsumerClientTests : TestBase
{
    private readonly IRedisStreamManager _mockStreamManager;
    private readonly IOptions<MessagingRedisOptions> _options;

    public RedisConsumerClientTests()
    {
        _mockStreamManager = Substitute.For<IRedisStreamManager>();
        _options = Options.Create(
            new MessagingRedisOptions { Configuration = ConfigurationOptions.Parse("localhost:6379") }
        );
    }

    [Fact]
    public void should_return_correct_broker_address()
    {
        // given
        var logger = LoggerFactory.CreateLogger<RedisConsumerClient>();
        var client = new RedisConsumerClient("test-group", 1, _mockStreamManager, _options, logger);

        // when
        var address = client.BrokerAddress;

        // then
        address.Name.Should().Be("redis");
        address.Endpoint.Should().Be("localhost:6379");
    }

    [Fact]
    public async Task should_create_consumer_group_when_subscribing()
    {
        // given
        var logger = LoggerFactory.CreateLogger<RedisConsumerClient>();
        await using var client = new RedisConsumerClient("my-group", 1, _mockStreamManager, _options, logger);

        var topics = new[] { "topic-1", "topic-2" };

        // when
        await client.SubscribeAsync(topics);

        // then
        await _mockStreamManager.Received(1).CreateStreamWithConsumerGroupAsync("topic-1", "my-group");
        await _mockStreamManager.Received(1).CreateStreamWithConsumerGroupAsync("topic-2", "my-group");
    }

    [Fact]
    public async Task should_throw_when_subscribing_to_null_topics()
    {
        // given
        var logger = LoggerFactory.CreateLogger<RedisConsumerClient>();
        await using var client = new RedisConsumerClient("test-group", 1, _mockStreamManager, _options, logger);

        // when & then
        var action = async () => await client.SubscribeAsync(null!);
        await action.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task should_acknowledge_message_on_commit()
    {
        // given
        var logger = LoggerFactory.CreateLogger<RedisConsumerClient>();
        await using var client = new RedisConsumerClient("test-group", 1, _mockStreamManager, _options, logger);

        var sender = ("test-stream", "test-group", "1234567-0");

        // when
        await client.CommitAsync(sender);

        // then
        await _mockStreamManager.Received(1).Ack("test-stream", "test-group", "1234567-0");
    }

    [Fact]
    public async Task should_complete_reject_without_error()
    {
        // given
        var logger = LoggerFactory.CreateLogger<RedisConsumerClient>();
        await using var client = new RedisConsumerClient("test-group", 1, _mockStreamManager, _options, logger);

        // when & then - reject should complete without error
        var action = async () => await client.RejectAsync(null);
        await action.Should().NotThrowAsync();
    }

    [Fact]
    public async Task should_dispose_without_error()
    {
        // given
        var logger = LoggerFactory.CreateLogger<RedisConsumerClient>();
        var client = new RedisConsumerClient("test-group", 1, _mockStreamManager, _options, logger);

        // when & then
        var action = async () => await client.DisposeAsync();
        await action.Should().NotThrowAsync();
    }

    [Fact]
    public async Task should_allow_setting_callbacks()
    {
        // given
        var logger = LoggerFactory.CreateLogger<RedisConsumerClient>();
        await using var client = new RedisConsumerClient("test-group", 1, _mockStreamManager, _options, logger);

        Func<TransportMessage, object?, Task> messageCallback = (_, _) => Task.CompletedTask;
        Action<LogMessageEventArgs> logCallback = _ => { };

        // when
        client.OnMessageCallback = messageCallback;
        client.OnLogCallback = logCallback;

        // then
        client.OnMessageCallback.Should().BeSameAs(messageCallback);
        client.OnLogCallback.Should().BeSameAs(logCallback);
    }
}
