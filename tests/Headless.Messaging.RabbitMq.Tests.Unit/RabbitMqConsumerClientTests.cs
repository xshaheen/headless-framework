// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.RabbitMq;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Tests;

public sealed class RabbitMqConsumerClientTests : TestBase
{
    private readonly IConnectionChannelPool _pool;
    private readonly IConnection _connection;
    private readonly IChannel _channel;
    private readonly IOptions<RabbitMqMessagingOptions> _options;
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
        _options = Options.Create(
            new RabbitMqMessagingOptions
            {
                HostName = "localhost",
                Port = 5672,
                UserName = "test_user",
                Password = "test_pass",
            }
        );
        _serviceProvider = new ServiceCollection().BuildServiceProvider();

        _pool.Exchange.Returns("test.exchange");
        _pool.GetConnectionAsync(Arg.Any<CancellationToken>()).Returns(_connection);
        _connection
            .CreateChannelAsync(Arg.Any<CreateChannelOptions?>(), Arg.Any<CancellationToken>())
            .Returns(_channel);
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
        await client.ConnectAsync(AbortToken);

        // then
        await _connection
            .Received(1)
            .CreateChannelAsync(Arg.Any<CreateChannelOptions?>(), Arg.Any<CancellationToken>());

        await _channel
            .Received(1)
            .ExchangeDeclareAsync(
                "test.exchange.bus",
                RabbitMqMessagingOptions.ExchangeType,
                true,
                false,
                null,
                false,
                false,
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_use_consumer_prefetch_config_for_basic_qos()
    {
        // given
        await using var client = new RabbitMqConsumerClient(
            "test-group",
            1,
            _pool,
            _options,
            _serviceProvider,
            new RabbitMqConsumerConfig(20)
        );
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        // when
        await client.ListeningAsync(TimeSpan.Zero, cts.Token);

        // then
        await _channel.Received(1).BasicQosAsync(0, 20, false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_open_every_instance_client_on_its_own_connection_without_declaring_a_queue()
    {
        // given
        var ownedConnection = Substitute.For<IConnection>();
        ownedConnection
            .CreateChannelAsync(Arg.Any<CreateChannelOptions?>(), Arg.Any<CancellationToken>())
            .Returns(_channel);
        _pool.CreateNonRecoveringConnectionAsync(Arg.Any<CancellationToken>()).Returns(ownedConnection);
        await using var client = _CreateEveryInstanceClient();

        // when
        await client.ConnectAsync(AbortToken);

        // then: the topology-only client the core opens stops here, so nothing per process may exist yet
        await _pool.DidNotReceive().GetConnectionAsync(Arg.Any<CancellationToken>());
        await ownedConnection
            .Received(1)
            .CreateChannelAsync(Arg.Any<CreateChannelOptions?>(), Arg.Any<CancellationToken>());
        await _channel
            .DidNotReceiveWithAnyArgs()
            .QueueDeclareAsync(default!, default, default, default, default, default, default, AbortToken);
    }

    [Fact]
    public async Task should_bind_every_routing_key_to_one_server_named_exclusive_queue_when_every_instance_subscribes()
    {
        // given
        var ownedConnection = Substitute.For<IConnection>();
        ownedConnection
            .CreateChannelAsync(Arg.Any<CreateChannelOptions?>(), Arg.Any<CancellationToken>())
            .Returns(_channel);
        _pool.CreateNonRecoveringConnectionAsync(Arg.Any<CancellationToken>()).Returns(ownedConnection);
        _channel
            .QueueDeclareAsync(
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<IDictionary<string, object?>?>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(new QueueDeclareOk("amq.gen-every", 0, 0));
        await using var client = _CreateEveryInstanceClient();

        // when
        await client.SubscribeAsync(["orders.created", "orders.cancelled"], AbortToken);

        // then
        await _channel
            .Received(1)
            .QueueDeclareAsync(
                string.Empty,
                false, // durable
                true, // exclusive
                false, // autoDelete: pausing cancels the only consumer and must not delete the queue
                Arg.Is<IDictionary<string, object?>?>(d => d != null && d.Count == 1 && d.ContainsKey("x-message-ttl")),
                false,
                false,
                Arg.Any<CancellationToken>()
            );
        await _channel
            .Received(1)
            .QueueBindAsync(
                "amq.gen-every",
                "test.exchange.bus",
                "bus.orders.created",
                Arg.Any<IDictionary<string, object?>?>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>()
            );
        await _channel
            .Received(1)
            .QueueBindAsync(
                "amq.gen-every",
                "test.exchange.bus",
                "bus.orders.cancelled",
                Arg.Any<IDictionary<string, object?>?>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_close_its_own_connection_when_every_instance_client_disposes()
    {
        // given
        var ownedConnection = Substitute.For<IConnection>();
        ownedConnection
            .CreateChannelAsync(Arg.Any<CreateChannelOptions?>(), Arg.Any<CancellationToken>())
            .Returns(_channel);
        _pool.CreateNonRecoveringConnectionAsync(Arg.Any<CancellationToken>()).Returns(ownedConnection);
        var client = _CreateEveryInstanceClient();
        await client.ConnectAsync(AbortToken);

        // when
        await client.DisposeAsync();

        // then
        await ownedConnection
            .Received(1)
            .CloseAsync(
                Arg.Any<ushort>(),
                Arg.Any<string>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>()
            );
        await ownedConnection.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task should_declare_every_instance_queue_with_a_one_minute_ttl_whatever_the_competing_queue_arguments()
    {
        // given - competing queue arguments that must not reach a per-process queue
        var options = Options.Create(
            new RabbitMqMessagingOptions
            {
                HostName = "localhost",
                UserName = "test_user",
                Password = "test_pass",
                QueueArguments = new RabbitMqMessagingOptions.QueueArgumentsOptions
                {
                    MessageTTL = 864000000,
                    QueueMode = "lazy",
                    QueueType = "quorum",
                },
            }
        );
        _UseOwnedConnectionWithServerNamedQueue();
        await using var client = new RabbitMqConsumerClient(
            "billing.cache",
            1,
            _pool,
            options,
            _serviceProvider,
            lane: MessageLane.Bus,
            kind: ConsumerSubscriptionKind.EveryInstance
        );

        // when
        await client.SubscribeAsync(["orders.created"], AbortToken);

        // then - a stalled or paused queue never replays more than a minute of per-process state
        await _channel
            .Received(1)
            .QueueDeclareAsync(
                string.Empty,
                false,
                true,
                false,
                Arg.Is<IDictionary<string, object?>?>(d =>
                    d != null && d.Count == 1 && Equals(d["x-message-ttl"], 60_000)
                ),
                false,
                false,
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_fail_every_instance_listening_for_a_rebuild_when_its_channel_shuts_down()
    {
        // given
        _UseOwnedConnectionWithServerNamedQueue();
        var client = _CreateEveryInstanceClient();

        try
        {
            client.AttachCallbacks(onMessage: (_, _) => Task.CompletedTask, onLog: _ => { });
            await client.SubscribeAsync(["orders.created"], AbortToken);
            var listening = client.ListeningAsync(TimeSpan.FromSeconds(1), AbortToken).AsTask();
            await client.WaitUntilReadyAsync(AbortToken).AsTask().WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

            // when
            _channel.ChannelShutdownAsync += Raise.Event<AsyncEventHandler<ShutdownEventArgs>>(
                _channel,
                new ShutdownEventArgs(ShutdownInitiator.Peer, 320, "CONNECTION_FORCED")
            );

            // then
            var act = () => listening.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
            (await act.Should().ThrowAsync<BrokerConnectionException>())
                .WithInnerException<InvalidOperationException>()
                .WithMessage("*CONNECTION_FORCED*");
        }
        finally
        {
            await client.DisposeAsync();
        }
    }

    [Fact]
    public async Task should_fail_every_instance_listening_for_a_rebuild_when_the_broker_cancels_its_consumer()
    {
        // given
        _UseOwnedConnectionWithServerNamedQueue();
        var client = _CreateEveryInstanceClient();

        try
        {
            client.AttachCallbacks(onMessage: (_, _) => Task.CompletedTask, onLog: _ => { });
            await client.SubscribeAsync(["orders.created"], AbortToken);
            var listening = client.ListeningAsync(TimeSpan.FromSeconds(1), AbortToken).AsTask();
            await client.WaitUntilReadyAsync(AbortToken).AsTask().WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
            var consumer = _channel
                .ReceivedCalls()
                .First(c =>
                    string.Equals(c.GetMethodInfo().Name, nameof(IChannel.BasicConsumeAsync), StringComparison.Ordinal)
                )
                .GetArguments()
                .OfType<IAsyncBasicConsumer>()
                .Single();

            // when - an operator deleted the queue: the broker cancels the consumer and the channel stays open
            await consumer.HandleBasicCancelAsync("amq.ctag-every", AbortToken);

            // then
            var act = () => listening.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
            (await act.Should().ThrowAsync<BrokerConnectionException>())
                .WithInnerException<InvalidOperationException>()
                .WithMessage("*amq.ctag-every*");
        }
        finally
        {
            await client.DisposeAsync();
        }
    }

    private void _UseOwnedConnectionWithServerNamedQueue()
    {
        var ownedConnection = Substitute.For<IConnection>();
        ownedConnection
            .CreateChannelAsync(Arg.Any<CreateChannelOptions?>(), Arg.Any<CancellationToken>())
            .Returns(_channel);
        _pool.CreateNonRecoveringConnectionAsync(Arg.Any<CancellationToken>()).Returns(ownedConnection);
        _channel.IsClosed.Returns(false);
        _channel
            .QueueDeclareAsync(
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<IDictionary<string, object?>?>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(new QueueDeclareOk("amq.gen-every", 0, 0));
    }

    [Fact]
    public void should_refuse_every_instance_client_on_the_queue_lane()
    {
        var act = () =>
            new RabbitMqConsumerClient(
                "orders.created",
                1,
                _pool,
                _options,
                _serviceProvider,
                lane: MessageLane.Queue,
                kind: ConsumerSubscriptionKind.EveryInstance
            );

        act.Should().Throw<ArgumentException>();
    }

    private RabbitMqConsumerClient _CreateEveryInstanceClient() =>
        new(
            "billing.cache",
            1,
            _pool,
            _options,
            _serviceProvider,
            lane: MessageLane.Bus,
            kind: ConsumerSubscriptionKind.EveryInstance
        );

    [Fact]
    public async Task should_declare_queue_with_default_options()
    {
        // given
        await using var client = new RabbitMqConsumerClient("test-group", 1, _pool, _options, _serviceProvider);

        // when
        await client.ConnectAsync(AbortToken);

        // then
        await _channel
            .Received(1)
            .QueueDeclareAsync(
                "bus.test-group",
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
            new RabbitMqMessagingOptions
            {
                HostName = "localhost",
                Port = 5672,
                UserName = "test_user",
                Password = "test_pass",
                QueueArguments = new RabbitMqMessagingOptions.QueueArgumentsOptions { MessageTTL = 3600000 },
            }
        );
        await using var client = new RabbitMqConsumerClient("test-group", 1, _pool, options, _serviceProvider);

        // when
        await client.ConnectAsync(AbortToken);

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
        await client.SubscribeAsync(topics, AbortToken);

        // then
        await _channel
            .Received(1)
            .QueueBindAsync(
                "bus.test-group",
                "test.exchange.bus",
                "bus.topic1",
                null,
                false,
                Arg.Any<CancellationToken>()
            );
        await _channel
            .Received(1)
            .QueueBindAsync(
                "bus.test-group",
                "test.exchange.bus",
                "bus.topic2",
                null,
                false,
                Arg.Any<CancellationToken>()
            );
        await _channel
            .Received(1)
            .QueueBindAsync(
                "bus.test-group",
                "test.exchange.bus",
                "bus.topic3",
                null,
                false,
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_propagate_exact_token_through_connection_channel_exchange_queue_and_binding()
    {
        await using var client = new RabbitMqConsumerClient(
            "test-group",
            1,
            _pool,
            _options,
            _serviceProvider,
            lane: MessageLane.Queue
        );
        using var cts = new CancellationTokenSource();

        await client.SubscribeAsync(["orders.created"], cts.Token);

        await _pool.Received(1).GetConnectionAsync(cts.Token);
        await _connection.Received(1).CreateChannelAsync(Arg.Any<CreateChannelOptions?>(), cts.Token);
        await _channel
            .Received(1)
            .ExchangeDeclareAsync(
                "test.exchange.queue",
                ExchangeType.Direct,
                true,
                false,
                null,
                false,
                false,
                cts.Token
            );
        await _channel
            .Received(1)
            .QueueDeclareAsync(
                "queue.orders.created",
                true,
                false,
                false,
                Arg.Any<IDictionary<string, object?>>(),
                false,
                false,
                cts.Token
            );
        await _channel
            .Received(1)
            .QueueBindAsync(
                "queue.orders.created",
                "test.exchange.queue",
                "queue.orders.created",
                null,
                false,
                cts.Token
            );
    }

    [Fact]
    public void should_use_consumer_identity_queue_for_bus_intent()
    {
        RabbitMqConsumerClient.GetQueueName("workers", "orders.created", MessageLane.Bus).Should().Be("bus.workers");
    }

    [Fact]
    public void should_keep_consumer_identity_unchanged_when_it_is_a_valid_queue_name()
    {
        RabbitMqConsumerClient
            .GetQueueName("billing.invoice-projection", "orders.created", MessageLane.Bus)
            .Should()
            .Be("bus.billing.invoice-projection");
    }

    [Fact]
    public void should_use_topic_queue_for_queue_intent()
    {
        RabbitMqConsumerClient
            .GetQueueName("workers", "orders.created", MessageLane.Queue)
            .Should()
            .Be("queue.orders.created");
    }

    [Fact]
    public async Task should_reuse_existing_channel_on_multiple_connects()
    {
        // given
        await using var client = new RabbitMqConsumerClient("test-group", 1, _pool, _options, _serviceProvider);
        _channel.IsClosed.Returns(false);

        // when
        await client.ConnectAsync(AbortToken);
        await client.ConnectAsync(AbortToken);

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
        await client.ConnectAsync(AbortToken);
        _channel.IsClosed.Returns(true);
        await client.ConnectAsync(AbortToken);

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
        var act = async () => await client.ConnectAsync();

        // then
        await act.Should().ThrowAsync<TimeoutException>();
        logInvoked.Should().BeTrue();
    }

    [Fact]
    public async Task should_release_connect_semaphore_when_channel_setup_throws()
    {
        // given
        var firstChannel = Substitute.For<IChannel>();
        var secondChannel = Substitute.For<IChannel>();
        _connection
            .CreateChannelAsync(Arg.Any<CreateChannelOptions?>(), Arg.Any<CancellationToken>())
            .Returns(firstChannel, secondChannel);
        firstChannel
            .ExchangeDeclareAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<IDictionary<string, object?>>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(_ => throw new InvalidOperationException("exchange declare failed"));

        await using var client = new RabbitMqConsumerClient("test-group", 1, _pool, _options, _serviceProvider);

        // when
        await client.Invoking(x => x.ConnectAsync()).Should().ThrowAsync<InvalidOperationException>();
        await client.ConnectAsync(AbortToken).WaitAsync(TimeSpan.FromSeconds(1), AbortToken);

        // then
        await _connection
            .Received(2)
            .CreateChannelAsync(Arg.Any<CreateChannelOptions?>(), Arg.Any<CancellationToken>());
        await firstChannel.Received(1).DisposeAsync();
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
            new RabbitMqMessagingOptions
            {
                HostName = "localhost",
                Port = 5672,
                UserName = "test_user",
                Password = "test_pass",
                QueueArguments = new RabbitMqMessagingOptions.QueueArgumentsOptions { QueueMode = "lazy" },
            }
        );
        await using var client = new RabbitMqConsumerClient("test-group", 1, _pool, options, _serviceProvider);

        // when
        await client.ConnectAsync(AbortToken);

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
            new RabbitMqMessagingOptions
            {
                HostName = "localhost",
                Port = 5672,
                UserName = "test_user",
                Password = "test_pass",
                QueueArguments = new RabbitMqMessagingOptions.QueueArgumentsOptions { QueueType = "quorum" },
            }
        );
        await using var client = new RabbitMqConsumerClient("test-group", 1, _pool, options, _serviceProvider);

        // when
        await client.ConnectAsync(AbortToken);

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
            new RabbitMqMessagingOptions
            {
                HostName = "localhost",
                Port = 5672,
                UserName = "test_user",
                Password = "test_pass",
                QueueOptions = new RabbitMqMessagingOptions.QueueRabbitOptions
                {
                    Durable = false,
                    Exclusive = true,
                    AutoDelete = true,
                },
            }
        );
        await using var client = new RabbitMqConsumerClient("test-group", 1, _pool, options, _serviceProvider);

        // when
        await client.ConnectAsync(AbortToken);

        // then
        await _channel
            .Received(1)
            .QueueDeclareAsync(
                "bus.test-group",
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
        await client.ConnectAsync(AbortToken);

        // when
        await client.DisposeAsync();

        // then - should be idempotent (calling dispose again should not throw)
        await client.DisposeAsync();
    }

    // -------------------------------------------------------------------------
    // PauseAsync / ResumeAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task pause_async_is_noop_when_consumer_tag_is_null()
    {
        // _consumerTag is null before ListeningAsync — PauseAsync should be a no-op
        await using var client = new RabbitMqConsumerClient("test-group", 1, _pool, _options, _serviceProvider);
        await client.ConnectAsync(AbortToken);

        await client.PauseAsync(AbortToken);

        // BasicCancelAsync should NOT have been called
        _channel.ReceivedCalls().Should().NotContain(c => c.GetMethodInfo().Name == nameof(IChannel.BasicCancelAsync));
    }

    [Fact]
    public async Task resume_async_is_noop_when_not_paused()
    {
        await using var client = new RabbitMqConsumerClient("test-group", 1, _pool, _options, _serviceProvider);

        // not paused — ResumeAsync should be no-op, no BasicConsumeAsync call
        await client.ResumeAsync(AbortToken);

        _channel.ReceivedCalls().Should().NotContain(c => c.GetMethodInfo().Name == nameof(IChannel.BasicConsumeAsync));
    }

    [Fact]
    public async Task pause_async_is_idempotent_when_called_twice()
    {
        // given
        await using var client = new RabbitMqConsumerClient("test-group", 1, _pool, _options, _serviceProvider);
        await client.ConnectAsync(AbortToken);

        // when
        await client.PauseAsync(AbortToken);
        _channel.ClearReceivedCalls();
        await client.PauseAsync(AbortToken); // second call — should be a no-op

        // then — no broker interaction on the second call
        _channel.ReceivedCalls().Should().NotContain(c => c.GetMethodInfo().Name == nameof(IChannel.BasicCancelAsync));
    }

    [Fact]
    public async Task resume_async_is_idempotent_after_resume()
    {
        // given
        await using var client = new RabbitMqConsumerClient("test-group", 1, _pool, _options, _serviceProvider);

        await client.PauseAsync(AbortToken);
        await client.ResumeAsync(AbortToken);
        _channel.ClearReceivedCalls();

        // when — second resume should be a no-op
        await client.ResumeAsync(AbortToken);

        // then
        _channel
            .ReceivedCalls()
            .Should()
            .NotContain(c => c.GetMethodInfo().Name == nameof(IChannel.BasicConsumeAsync));
    }

    [Fact]
    public async Task pause_async_is_noop_after_disposal()
    {
        // given
        var client = new RabbitMqConsumerClient("test-group", 1, _pool, _options, _serviceProvider);
        await client.DisposeAsync();

        // when — should not throw or interact with channel
        await client.PauseAsync(AbortToken);

        // then
        _channel.ReceivedCalls().Should().NotContain(c => c.GetMethodInfo().Name == nameof(IChannel.BasicCancelAsync));
    }

    [Fact]
    public async Task resume_async_is_noop_after_disposal()
    {
        // given
        var client = new RabbitMqConsumerClient("test-group", 1, _pool, _options, _serviceProvider);
        await client.DisposeAsync();

        // when — should not throw or interact with channel
        await client.ResumeAsync(AbortToken);

        // then
        _channel
            .ReceivedCalls()
            .Should()
            .NotContain(c => c.GetMethodInfo().Name == nameof(IChannel.BasicConsumeAsync));
    }

    [Fact]
    public async Task resume_async_is_noop_when_paused_before_listening_started()
    {
        // given — the register pre-pauses a client while the circuit is open, which lands before
        // ListeningAsync has built the consumer. ConnectAsync has already populated the queue list.
        await using var client = new RabbitMqConsumerClient("test-group", 1, _pool, _options, _serviceProvider);
        await client.ConnectAsync(AbortToken);
        await client.PauseAsync(AbortToken);

        // when
        await client.ResumeAsync(AbortToken);

        // then — registering here would pass a null consumer to the broker; ListeningAsync owns the
        // registration once it passes the gate
        _channel
            .ReceivedCalls()
            .Should()
            .NotContain(c => c.GetMethodInfo().Name == nameof(IChannel.BasicConsumeAsync));
    }

    [Fact]
    public async Task should_re_register_consumers_when_resumed_after_listening_started()
    {
        // given
        await using var client = new RabbitMqConsumerClient("test-group", 1, _pool, _options, _serviceProvider);
        _channel.IsClosed.Returns(false);
        using var cts = new CancellationTokenSource();

        var listeningTask = client.ListeningAsync(TimeSpan.FromMilliseconds(10), cts.Token).AsTask();

        try
        {
            await client.WaitUntilReadyAsync(AbortToken).AsTask().WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
            _CountBasicConsumeCalls().Should().Be(1);

            // when
            await client.PauseAsync(AbortToken);
            await client.ResumeAsync(AbortToken);

            // then — the consumer built by ListeningAsync is registered again after the cancel
            _channel
                .ReceivedCalls()
                .Should()
                .Contain(c => c.GetMethodInfo().Name == nameof(IChannel.BasicCancelAsync));
            _CountBasicConsumeCalls().Should().Be(2);
        }
        finally
        {
            await cts.CancelAsync();
            await listeningTask;
        }
    }

    [Fact]
    public async Task should_wait_for_resume_when_listening_async_group_is_paused_before_startup()
    {
        // given
        await using var client = new RabbitMqConsumerClient("test-group", 1, _pool, _options, _serviceProvider);
        _channel.IsClosed.Returns(false);

        using var cts = new CancellationTokenSource();
        await client.PauseAsync(AbortToken);

        // when
        var listeningTask = client.ListeningAsync(TimeSpan.FromMilliseconds(10), cts.Token).AsTask();
        try
        {
            // then - startup remains gated while paused
            await Task.Delay(100, AbortToken);

            _channel
                .ReceivedCalls()
                .Should()
                .NotContain(c => c.GetMethodInfo().Name == nameof(IChannel.BasicConsumeAsync));

            await client.ResumeAsync(AbortToken);

            // Deterministic: _ready completes right after BasicConsumeAsync registers, so waiting
            // on it (instead of a fixed delay) guarantees the resumed startup finished and the
            // final cancel lands in the keep-alive delay, which completes gracefully.
            await client.WaitUntilReadyAsync(AbortToken).AsTask().WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

            _channel
                .ReceivedCalls()
                .Should()
                .Contain(c => c.GetMethodInfo().Name == nameof(IChannel.BasicConsumeAsync));
            _CountBasicConsumeCalls().Should().Be(1, "the resumed startup registers exactly once");
        }
        finally
        {
            await cts.CancelAsync();
            await listeningTask; // Should complete gracefully — no OperationCanceledException
        }
    }

    [Fact]
    public async Task should_not_start_consuming_when_pause_wins_after_startup_gate()
    {
        // given
        var beforeStartLock = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowStartLock = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var startDeferred = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var client = new RabbitMqConsumerClient(
            "test-group",
            1,
            _pool,
            _options,
            _serviceProvider,
            lifecycleCheckpointAsync: async checkpoint =>
            {
                if (checkpoint == RabbitMqConsumerLifecycleCheckpoint.BeforeStartLock)
                {
                    beforeStartLock.TrySetResult();
                    await allowStartLock.Task.WaitAsync(AbortToken);
                }
                else
                {
                    startDeferred.TrySetResult();
                }
            }
        );
        _channel.IsClosed.Returns(false);
        _ConfigureBasicConsume("consumer-tag");
        using var cts = new CancellationTokenSource();

        var listeningTask = client.ListeningAsync(TimeSpan.Zero, cts.Token).AsTask();

        try
        {
            await beforeStartLock.Task.WaitAsync(AbortToken);

            // when - ListeningAsync has passed the outer gate, but PauseAsync wins the lifecycle lock.
            await client.PauseAsync(AbortToken);
            allowStartLock.TrySetResult();
            await startDeferred.Task.WaitAsync(AbortToken);

            // then
            _CountBasicConsumeCalls().Should().Be(0);

            await client.ResumeAsync(AbortToken);
            await client.WaitUntilReadyAsync(AbortToken);
            _CountBasicConsumeCalls().Should().Be(1);
        }
        finally
        {
            allowStartLock.TrySetResult();
            await client.ResumeAsync(AbortToken);
            await cts.CancelAsync();
            await listeningTask;
        }
    }

    [Fact]
    public async Task should_finish_pause_after_gate_transition_when_caller_is_cancelled()
    {
        // given
        await using var client = new RabbitMqConsumerClient("test-group", 1, _pool, _options, _serviceProvider);
        _channel.IsClosed.Returns(false);
        _ConfigureBasicConsume("consumer-tag");

        using var listeningCts = new CancellationTokenSource();
        var listeningTask = client.ListeningAsync(TimeSpan.Zero, listeningCts.Token).AsTask();

        try
        {
            await client.WaitUntilReadyAsync(AbortToken);

            var cancellationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var allowCancellation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var brokerCancellationToken = new TaskCompletionSource<CancellationToken>(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            _channel
                .BasicCancelAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
                .Returns(callInfo =>
                {
                    brokerCancellationToken.TrySetResult(callInfo.ArgAt<CancellationToken>(2));
                    cancellationStarted.TrySetResult();
                    return allowCancellation.Task;
                });
            using var pauseCts = new CancellationTokenSource();

            // when
            var pauseTask = client.PauseAsync(pauseCts.Token).AsTask();
            await cancellationStarted.Task.WaitAsync(AbortToken);
            await pauseCts.CancelAsync();
            allowCancellation.TrySetResult();
            await pauseTask;

            // then
            (await brokerCancellationToken.Task.WaitAsync(AbortToken))
                .Should()
                .Be(CancellationToken.None);
            await client.ResumeAsync(AbortToken);
            _CountBasicConsumeCalls().Should().Be(2);
        }
        finally
        {
            await listeningCts.CancelAsync();
            await listeningTask;
        }
    }

    [Fact]
    public async Task should_restore_resumed_state_when_broker_cancellation_fails()
    {
        // given
        await using var client = new RabbitMqConsumerClient("test-group", 1, _pool, _options, _serviceProvider);
        _channel.IsClosed.Returns(false);
        _ConfigureBasicConsume("consumer-tag");

        using var cts = new CancellationTokenSource();
        var listeningTask = client.ListeningAsync(TimeSpan.Zero, cts.Token).AsTask();

        try
        {
            await client.WaitUntilReadyAsync(AbortToken);
            _channel
                .BasicCancelAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
                .Returns(_ => Task.FromException(new InvalidOperationException("cancel failed")));

            // when
            var act = async () => await client.PauseAsync(AbortToken);

            // then
            await act.Should().ThrowAsync<InvalidOperationException>();
            await client.ResumeAsync(AbortToken);
            _CountBasicConsumeCalls().Should().Be(1, "failed pause restores the prior resumed registration");
        }
        finally
        {
            await cts.CancelAsync();
            await listeningTask;
        }
    }

    [Fact]
    public async Task should_remain_paused_when_broker_registration_fails_during_resume()
    {
        // given
        await using var client = new RabbitMqConsumerClient("test-group", 1, _pool, _options, _serviceProvider);
        _channel.IsClosed.Returns(false);
        _ConfigureBasicConsume("consumer-tag");

        using var cts = new CancellationTokenSource();
        var listeningTask = client.ListeningAsync(TimeSpan.Zero, cts.Token).AsTask();

        try
        {
            await client.WaitUntilReadyAsync(AbortToken);
            await client.PauseAsync(AbortToken);
            _channel
                .BasicConsumeAsync(
                    Arg.Any<string>(),
                    Arg.Any<bool>(),
                    Arg.Any<string>(),
                    Arg.Any<bool>(),
                    Arg.Any<bool>(),
                    Arg.Any<IDictionary<string, object?>>(),
                    Arg.Any<IAsyncBasicConsumer>(),
                    Arg.Any<CancellationToken>()
                )
                .Returns(_ => Task.FromException<string>(new InvalidOperationException("consume failed")));

            // when
            var act = async () => await client.ResumeAsync(AbortToken);

            // then
            await act.Should().ThrowAsync<InvalidOperationException>();

            _ConfigureBasicConsume("resumed-tag");
            await client.ResumeAsync(AbortToken);
            _CountBasicConsumeCalls().Should().Be(3, "the failed resume leaves the gate available for retry");
        }
        finally
        {
            await cts.CancelAsync();
            await listeningTask;
        }
    }

    private void _ConfigureBasicConsume(string consumerTag)
    {
        _channel
            .BasicConsumeAsync(
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<IDictionary<string, object?>>(),
                Arg.Any<IAsyncBasicConsumer>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.FromResult(consumerTag));
    }

    private int _CountBasicConsumeCalls()
    {
        return _channel
            .ReceivedCalls()
            .Count(c =>
                string.Equals(c.GetMethodInfo().Name, nameof(IChannel.BasicConsumeAsync), StringComparison.Ordinal)
            );
    }
}
