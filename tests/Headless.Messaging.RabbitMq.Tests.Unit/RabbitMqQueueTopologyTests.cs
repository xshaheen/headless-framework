// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.RabbitMq;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Tests;

public sealed class RabbitMqQueueTopologyTests : TestBase
{
    private const string _LaneExchange = "test.exchange.queue";
    private const string _Queue = "queue.orders";

    [Fact]
    public void should_build_dead_letter_arguments_routed_by_queue_name_when_dead_lettering_enabled()
    {
        var options = _CreateOptions();
        options.QueueArguments.EnableDeadLettering = true;
        options.QueueArguments.QueueType = "quorum";
        options.QueueArguments.DeliveryLimit = 5;

        var arguments = RabbitMqQueueTopology.BuildQueueArguments(options, _LaneExchange, _Queue);

        arguments.Should().Contain("x-dead-letter-exchange", "test.exchange.queue.dlx");
        arguments.Should().Contain("x-dead-letter-routing-key", _Queue);
        arguments.Should().Contain("x-delivery-limit", 5);
        arguments.Should().Contain("x-queue-type", "quorum");
        arguments.Should().Contain("x-message-ttl", options.QueueArguments.MessageTTL);
    }

    [Fact]
    public void should_build_no_dead_letter_arguments_by_default()
    {
        var arguments = RabbitMqQueueTopology.BuildQueueArguments(_CreateOptions(), _LaneExchange, _Queue);

        arguments.Keys.Should().BeEquivalentTo(["x-message-ttl"]);
    }

    [Fact]
    public async Task should_declare_dead_letter_exchange_and_queue_before_the_queue_when_enabled()
    {
        var options = _CreateOptions();
        options.QueueArguments.EnableDeadLettering = true;
        var channel = Substitute.For<IChannel>();

        await RabbitMqQueueTopology.DeclareQueueAsync(
            channel,
            options,
            _LaneExchange,
            _Queue,
            "queue.orders",
            AbortToken
        );

        Received.InOrder(() =>
        {
            _ = channel.ExchangeDeclareAsync(
                "test.exchange.queue.dlx",
                ExchangeType.Direct,
                true,
                false,
                null,
                false,
                false,
                AbortToken
            );
            _ = channel.QueueDeclareAsync(
                "queue.orders.dlq",
                true,
                false,
                false,
                Arg.Is<IDictionary<string, object?>>(a => a.Count == 0),
                false,
                false,
                AbortToken
            );
            _ = channel.QueueBindAsync("queue.orders.dlq", "test.exchange.queue.dlx", _Queue, null, false, AbortToken);
            _ = channel.QueueDeclareAsync(
                _Queue,
                true,
                false,
                false,
                Arg.Is<IDictionary<string, object?>>(a => a.ContainsKey("x-dead-letter-exchange")),
                false,
                false,
                AbortToken
            );
            _ = channel.QueueBindAsync(_Queue, _LaneExchange, "queue.orders", null, false, AbortToken);
        });
    }

    [Fact]
    public async Task should_only_declare_passively_and_never_bind_when_auto_provision_is_off()
    {
        var options = _CreateOptions();
        options.AutoProvision = false;
        options.QueueArguments.EnableDeadLettering = true;
        var channel = Substitute.For<IChannel>();

        await RabbitMqQueueTopology.DeclareExchangeAsync(channel, options, _LaneExchange, "direct", AbortToken);
        await RabbitMqQueueTopology.DeclareQueueAsync(
            channel,
            options,
            _LaneExchange,
            _Queue,
            "queue.orders",
            AbortToken
        );
        await RabbitMqQueueTopology.BindQueueAsync(channel, options, _LaneExchange, _Queue, "queue.orders", AbortToken);

        await channel.Received(1).ExchangeDeclarePassiveAsync(_LaneExchange, AbortToken);
        await channel.Received(1).QueueDeclarePassiveAsync(_Queue, AbortToken);
        channel
            .ReceivedCalls()
            .Select(call => call.GetMethodInfo().Name)
            .Should()
            .NotContain([
                nameof(IChannel.ExchangeDeclareAsync),
                nameof(IChannel.QueueDeclareAsync),
                nameof(IChannel.QueueBindAsync),
            ]);
    }

    [Fact]
    public async Task should_declare_and_bind_publish_queue_once_per_process()
    {
        var (pool, channel) = _CreatePool(_CreateOptions());
        await using var _ = pool;

        await pool.EnsureQueueForPublishAsync("orders", AbortToken);
        await pool.EnsureQueueForPublishAsync("orders", AbortToken);

        await channel
            .Received(1)
            .QueueDeclareAsync(
                "queue.orders",
                true,
                false,
                false,
                Arg.Any<IDictionary<string, object?>>(),
                false,
                false,
                Arg.Any<CancellationToken>()
            );
        await channel
            .Received(1)
            .QueueBindAsync(
                "queue.orders",
                "test.exchange.queue",
                "queue.orders",
                null,
                false,
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_declare_publish_queue_again_after_a_failed_attempt()
    {
        var (pool, channel) = _CreatePool(_CreateOptions());
        await using var _ = pool;
        var attempts = 0;
        channel
            .QueueDeclareAsync(
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<IDictionary<string, object?>>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(_ =>
                ++attempts == 1
                    ? Task.FromException<QueueDeclareOk>(new InvalidOperationException("declare refused"))
                    : Task.FromResult(new QueueDeclareOk("queue.orders", 0, 0))
            );

        var first = async () => await pool.EnsureQueueForPublishAsync("orders", AbortToken);
        await first.Should().ThrowAsync<InvalidOperationException>();

        await pool.EnsureQueueForPublishAsync("orders", AbortToken);

        attempts.Should().Be(2);
    }

    [Fact]
    public async Task should_declare_publish_queue_again_after_it_is_forgotten()
    {
        var (pool, channel) = _CreatePool(_CreateOptions());
        await using var _ = pool;

        await pool.EnsureQueueForPublishAsync("orders", AbortToken);
        pool.ForgetQueueForPublish("orders");
        await pool.EnsureQueueForPublishAsync("orders", AbortToken);

        await channel
            .Received(2)
            .QueueDeclareAsync(
                "queue.orders",
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<IDictionary<string, object?>>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_declare_lane_exchanges_passively_when_auto_provision_is_off()
    {
        var options = _CreateOptions();
        options.AutoProvision = false;
        var (pool, channel) = _CreatePool(options);
        await using var _ = pool;

        var rented = await ((IConnectionChannelPool)pool).Rent(AbortToken);

        await channel.Received(1).ExchangeDeclarePassiveAsync("test.exchange.bus", AbortToken);
        await channel.Received(1).ExchangeDeclarePassiveAsync("test.exchange.queue", AbortToken);
        ((IConnectionChannelPool)pool).Return(rented);
    }

    private static RabbitMqMessagingOptions _CreateOptions()
    {
        return new RabbitMqMessagingOptions
        {
            HostName = "localhost",
            Port = 5672,
            UserName = "test_user",
            Password = "test_pass",
            ExchangeName = "test.exchange",
        };
    }

    private static (ConnectionChannelPool Pool, IChannel Channel) _CreatePool(RabbitMqMessagingOptions options)
    {
        var connection = Substitute.For<IConnection>();
        var channel = Substitute.For<IChannel>();
        connection.IsOpen.Returns(true);
        connection.CreateChannelAsync(Arg.Any<CreateChannelOptions?>(), Arg.Any<CancellationToken>()).Returns(channel);

        var pool = new ConnectionChannelPool(
            NullLogger<ConnectionChannelPool>.Instance,
            Options.Create(new MessagingOptions { Version = "v1" }),
            Options.Create(options),
#pragma warning disable CA2025 // The pool awaits the completed factory task before it owns and disposes the connection.
            _ => Task.FromResult(connection)
#pragma warning restore CA2025
        );

        return (pool, channel);
    }
}
