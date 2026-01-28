// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.RabbitMq;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Tests;

// ReSharper disable AccessToDisposedClosure
public sealed class RabbitMqConsumerClientTests : TestBase
{
    private readonly IConnectionChannelPool _pool;
    private readonly IConnection _connection;
    private readonly IChannel _channel;
    private readonly IOptions<RabbitMqOptions> _options;
    private readonly IServiceProvider _serviceProvider;

    protected override async ValueTask DisposeAsyncCore()
    {
        await _connection.DisposeAsync();
        await _channel.DisposeAsync();
        await base.DisposeAsyncCore();
    }

    public RabbitMqConsumerClientTests()
    {
        _pool = Substitute.For<IConnectionChannelPool>();
        _connection = Substitute.For<IConnection>();
        _channel = Substitute.For<IChannel>();
        _options = Options.Create(new RabbitMqOptions { HostName = "localhost", Port = 5672 });
        _serviceProvider = new ServiceCollection().BuildServiceProvider();

        _pool.Exchange.Returns("test.exchange");
        _pool.GetConnectionAsync().Returns(_connection);
        _connection.CreateChannelAsync(Arg.Any<CreateChannelOptions?>()).Returns(_channel);
    }

    [Fact]
    public async Task should_have_correct_broker_address()
    {
        // given, When
        await using var client = new RabbitMqConsumerClient("test-group", 1, _pool, _options, _serviceProvider);

        // then
        client.BrokerAddress.Name.Should().Be("rabbitmq");
        client.BrokerAddress.Endpoint.Should().Be("localhost:5672");
    }

    [Fact]
    public async Task should_create_channel_on_connect()
    {
        // given
        await using var client = new RabbitMqConsumerClient("test-group", 1, _pool, _options, _serviceProvider);

        // when
        await client.ConnectAsync();

        // then
        await _connection
            .Received(1)
            .CreateChannelAsync(Arg.Any<CreateChannelOptions?>(), Arg.Any<CancellationToken>());

        await _channel
            .Received(1)
            .ExchangeDeclareAsync(
                "test.exchange",
                RabbitMqOptions.ExchangeType,
                true,
                false,
                null,
                false,
                false,
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_declare_queue_with_default_options()
    {
        // given
        await using var client = new RabbitMqConsumerClient("test-group", 1, _pool, _options, _serviceProvider);

        // when
        await client.ConnectAsync();

        // then
        await _channel
            .Received(1)
            .QueueDeclareAsync(
                "test-group",
                true, // durable
                false, // exclusive
                false, // autoDelete
                Arg.Is<Dictionary<string, object?>>(d => d.ContainsKey("x-message-ttl")),
                cancellationToken: Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_declare_queue_with_custom_ttl()
    {
        // given
        var options = Options.Create(
            new RabbitMqOptions
            {
                HostName = "localhost",
                Port = 5672,
                QueueArguments = new RabbitMqOptions.QueueArgumentsOptions { MessageTTL = 3600000 },
            }
        );
        await using var client = new RabbitMqConsumerClient("test-group", 1, _pool, options, _serviceProvider);

        // when
        await client.ConnectAsync();

        // then
        await _channel
            .Received(1)
            .QueueDeclareAsync(
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Is<Dictionary<string, object?>>(d => (int)d["x-message-ttl"]! == 3600000),
                false,
                false,
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_bind_topics_on_subscribe()
    {
        // given
        await using var client = new RabbitMqConsumerClient("test-group", 1, _pool, _options, _serviceProvider);
        var topics = new[] { "topic1", "topic2", "topic3" };

        // when
        await client.SubscribeAsync(topics);

        // then
        await _channel
            .Received(1)
            .QueueBindAsync("test-group", "test.exchange", "topic1", null, false, Arg.Any<CancellationToken>());
        await _channel
            .Received(1)
            .QueueBindAsync("test-group", "test.exchange", "topic2", null, false, Arg.Any<CancellationToken>());
        await _channel
            .Received(1)
            .QueueBindAsync("test-group", "test.exchange", "topic3", null, false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_reuse_existing_channel_on_multiple_connects()
    {
        // given
        await using var client = new RabbitMqConsumerClient("test-group", 1, _pool, _options, _serviceProvider);
        _channel.IsClosed.Returns(false);

        // when
        await client.ConnectAsync();
        await client.ConnectAsync();

        // then
        await _connection
            .Received(1)
            .CreateChannelAsync(Arg.Any<CreateChannelOptions?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_create_new_channel_when_closed()
    {
        // given
        await using var client = new RabbitMqConsumerClient("test-group", 1, _pool, _options, _serviceProvider);

        // when
        _channel.IsClosed.Returns(false);
        await client.ConnectAsync();
        _channel.IsClosed.Returns(true);
        await client.ConnectAsync();

        // then
        await _connection
            .Received(2)
            .CreateChannelAsync(Arg.Any<CreateChannelOptions?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_handle_queue_declare_timeout()
    {
        // given
        var logInvoked = false;
        await using var client = new RabbitMqConsumerClient("test-group", 1, _pool, _options, _serviceProvider);

        client.OnLogCallback = args =>
        {
            logInvoked = true;
            args.LogType.Should().Be(MqLogType.ConsumerShutdown);
            args.Reason.Should().Contain(nameof(IChannel.QueueDeclareAsync));
        };

        _channel
            .QueueDeclareAsync(
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<Dictionary<string, object?>>(),
                false,
                false,
                Arg.Any<CancellationToken>()
            )
            .Returns<Task<QueueDeclareOk>>(_ => throw new TimeoutException("Queue declare timeout"));

        // when
        await client.ConnectAsync();

        // then
        logInvoked.Should().BeTrue();
    }

    [Fact]
    public async Task should_throw_when_subscribing_with_null_topics()
    {
        // given
        await using var client = new RabbitMqConsumerClient("test-group", 1, _pool, _options, _serviceProvider);

        // when
        var act = async () => await client.SubscribeAsync(null!);

        // then
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task should_declare_queue_with_queue_mode_when_specified()
    {
        // given
        var options = Options.Create(
            new RabbitMqOptions
            {
                HostName = "localhost",
                Port = 5672,
                QueueArguments = new RabbitMqOptions.QueueArgumentsOptions { QueueMode = "lazy" },
            }
        );
        await using var client = new RabbitMqConsumerClient("test-group", 1, _pool, options, _serviceProvider);

        // when
        await client.ConnectAsync();

        // then
        await _channel
            .Received(1)
            .QueueDeclareAsync(
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Is<Dictionary<string, object?>>(d =>
                    d.ContainsKey("x-queue-mode") && (string)d["x-queue-mode"]! == "lazy"
                ),
                false,
                false,
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_declare_queue_with_queue_type_when_specified()
    {
        // given
        var options = Options.Create(
            new RabbitMqOptions
            {
                HostName = "localhost",
                Port = 5672,
                QueueArguments = new RabbitMqOptions.QueueArgumentsOptions { QueueType = "quorum" },
            }
        );
        await using var client = new RabbitMqConsumerClient("test-group", 1, _pool, options, _serviceProvider);

        // when
        await client.ConnectAsync();

        // then
        await _channel
            .Received(1)
            .QueueDeclareAsync(
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Is<Dictionary<string, object?>>(d =>
                    d.ContainsKey("x-queue-type") && (string)d["x-queue-type"]! == "quorum"
                ),
                false,
                false,
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_use_custom_queue_options()
    {
        // given
        var options = Options.Create(
            new RabbitMqOptions
            {
                HostName = "localhost",
                Port = 5672,
                QueueOptions = new RabbitMqOptions.QueueRabbitOptions
                {
                    Durable = false,
                    Exclusive = true,
                    AutoDelete = true,
                },
            }
        );
        await using var client = new RabbitMqConsumerClient("test-group", 1, _pool, options, _serviceProvider);

        // when
        await client.ConnectAsync();

        // then
        await _channel
            .Received(1)
            .QueueDeclareAsync(
                "test-group",
                false, // durable
                true, // exclusive
                true, // autoDelete
                Arg.Any<Dictionary<string, object?>>(),
                false,
                false,
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_dispose_channel_and_semaphore()
    {
        // given
        var client = new RabbitMqConsumerClient("test-group", 1, _pool, _options, _serviceProvider);
        await client.ConnectAsync();

        // when
        await client.DisposeAsync();

        // then - should be idempotent (calling dispose again should not throw)
        await client.DisposeAsync();
    }
}
