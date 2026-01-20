// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Messages;
using Framework.Messages.Messages;
using Framework.Messages.Transport;
using Framework.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Tests;

public sealed class RabbitMQConsumerClientTests : TestBase
{
    private readonly IConnectionChannelPool _pool;
    private readonly IConnection _connection;
    private readonly IChannel _channel;
    private readonly IOptions<RabbitMQOptions> _options;
    private readonly IServiceProvider _serviceProvider;

    public RabbitMQConsumerClientTests()
    {
        _pool = Substitute.For<IConnectionChannelPool>();
        _connection = Substitute.For<IConnection>();
        _channel = Substitute.For<IChannel>();
        _options = Options.Create(new RabbitMQOptions { HostName = "localhost", Port = 5672 });
        _serviceProvider = new ServiceCollection().BuildServiceProvider();

        _pool.Exchange.Returns("test.exchange");
        _pool.GetConnection().Returns(_connection);
        _connection.CreateChannelAsync(Arg.Any<CancellationToken>()).Returns(_channel);
    }

    [Fact]
    public void should_have_correct_broker_address()
    {
        // Given, When
        using var client = new RabbitMQConsumerClient("test-group", 1, _pool, _options, _serviceProvider);

        // Then
        client.BrokerAddress.Name.Should().Be("rabbitmq");
        client.BrokerAddress.Endpoint.Should().Be("localhost:5672");
    }

    [Fact]
    public async Task should_create_channel_on_connect()
    {
        // Given
        using var client = new RabbitMQConsumerClient("test-group", 1, _pool, _options, _serviceProvider);

        // When
        await client.ConnectAsync();

        // Then
        await _connection.Received(1).CreateChannelAsync(Arg.Any<CancellationToken>());
        await _channel
            .Received(1)
            .ExchangeDeclareAsync("test.exchange", RabbitMQOptions.ExchangeType, true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_declare_queue_with_default_options()
    {
        // Given
        using var client = new RabbitMQConsumerClient("test-group", 1, _pool, _options, _serviceProvider);

        // When
        await client.ConnectAsync();

        // Then
        await _channel
            .Received(1)
            .QueueDeclareAsync(
                "test-group",
                true, // durable
                false, // exclusive
                false, // autoDelete
                Arg.Is<Dictionary<string, object?>>(d => d.ContainsKey("x-message-ttl")),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_declare_queue_with_custom_ttl()
    {
        // Given
        var options = Options.Create(
            new RabbitMQOptions
            {
                HostName = "localhost",
                Port = 5672,
                QueueArguments = new RabbitMQOptions.QueueArgumentsOptions { MessageTTL = 3600000 },
            }
        );
        using var client = new RabbitMQConsumerClient("test-group", 1, _pool, options, _serviceProvider);

        // When
        await client.ConnectAsync();

        // Then
        await _channel
            .Received(1)
            .QueueDeclareAsync(
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Is<Dictionary<string, object?>>(d => (int)d["x-message-ttl"]! == 3600000),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_bind_topics_on_subscribe()
    {
        // Given
        using var client = new RabbitMQConsumerClient("test-group", 1, _pool, _options, _serviceProvider);
        var topics = new[] { "topic1", "topic2", "topic3" };

        // When
        await client.SubscribeAsync(topics);

        // Then
        await _channel
            .Received(1)
            .QueueBindAsync(
                "test-group",
                "test.exchange",
                "topic1",
                Arg.Any<Dictionary<string, object?>?>(),
                Arg.Any<CancellationToken>()
            );
        await _channel
            .Received(1)
            .QueueBindAsync(
                "test-group",
                "test.exchange",
                "topic2",
                Arg.Any<Dictionary<string, object?>?>(),
                Arg.Any<CancellationToken>()
            );
        await _channel
            .Received(1)
            .QueueBindAsync(
                "test-group",
                "test.exchange",
                "topic3",
                Arg.Any<Dictionary<string, object?>?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_reuse_existing_channel_on_multiple_connects()
    {
        // Given
        using var client = new RabbitMQConsumerClient("test-group", 1, _pool, _options, _serviceProvider);
        _channel.IsClosed.Returns(false);

        // When
        await client.ConnectAsync();
        await client.ConnectAsync();

        // Then
        await _connection.Received(1).CreateChannelAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_create_new_channel_when_closed()
    {
        // Given
        using var client = new RabbitMQConsumerClient("test-group", 1, _pool, _options, _serviceProvider);

        // When
        _channel.IsClosed.Returns(false);
        await client.ConnectAsync();
        _channel.IsClosed.Returns(true);
        await client.ConnectAsync();

        // Then
        await _connection.Received(2).CreateChannelAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_handle_queue_declare_timeout()
    {
        // Given
        var logInvoked = false;
        using var client = new RabbitMQConsumerClient("test-group", 1, _pool, _options, _serviceProvider)
        {
            OnLogCallback = args =>
            {
                logInvoked = true;
                args.LogType.Should().Be(MqLogType.ConsumerShutdown);
                args.Reason.Should().Contain(nameof(IChannel.QueueDeclareAsync));
            },
        };

        _channel
            .QueueDeclareAsync(
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<Dictionary<string, object?>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns<Task<QueueDeclareOk>>(_ => throw new TimeoutException("Queue declare timeout"));

        // When
        await client.ConnectAsync();

        // Then
        logInvoked.Should().BeTrue();
    }

    [Fact]
    public void should_throw_when_subscribing_with_null_topics()
    {
        // Given
        using var client = new RabbitMQConsumerClient("test-group", 1, _pool, _options, _serviceProvider);

        // When
        var act = async () => await client.SubscribeAsync(null!);

        // Then
        act.Should().ThrowAsync<ArgumentNullException>();
    }
}
