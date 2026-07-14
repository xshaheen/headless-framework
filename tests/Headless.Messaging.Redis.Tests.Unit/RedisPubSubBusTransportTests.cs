// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Redis;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute.ExceptionExtensions;
using StackExchange.Redis;

namespace Tests;

public sealed class RedisPubSubBusTransportTests : TestBase
{
    private static readonly IOptions<RedisPubSubMessagingOptions> _Options = Options.Create(
        new RedisPubSubMessagingOptions { Configuration = ConfigurationOptions.Parse("localhost:6379") }
    );

    [Fact]
    public async Task should_return_correct_broker_address()
    {
        // given
        var connectionProvider = Substitute.For<IRedisPubSubConnectionProvider>();
        var logger = Substitute.For<ILogger<RedisPubSubBusTransport>>();
        await using var transport = new RedisPubSubBusTransport(connectionProvider, _Options, logger);

        // when
        var brokerAddress = transport.BrokerAddress;

        // then
        brokerAddress.Name.Should().Be("redis_pubsub");
        brokerAddress.Endpoint.Should().Be("localhost:6379");
    }

    [Fact]
    [SuppressMessage(
        "Reliability",
        "CA2025",
        Justification = "NSubstitute returns a borrowed mock connection; the test does not transfer ownership."
    )]
    public async Task should_publish_message_to_channel()
    {
        // given
        var connectionProvider = Substitute.For<IRedisPubSubConnectionProvider>();
        var connection = Substitute.For<IConnectionMultiplexer>();
        var subscriber = Substitute.For<ISubscriber>();
        connection.GetSubscriber().Returns(subscriber);
        connectionProvider.ConnectAsync(Arg.Any<CancellationToken>()).Returns(_ReturnConnectionAsync(connection));
        subscriber.PublishAsync(Arg.Any<RedisChannel>(), Arg.Any<RedisValue>(), Arg.Any<CommandFlags>()).Returns(1L);

        var logger = Substitute.For<ILogger<RedisPubSubBusTransport>>();
        await using var transport = new RedisPubSubBusTransport(connectionProvider, _Options, logger);

        var message = new TransportMessage(
            headers: new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [Headers.MessageName] = "OrderCreated",
                [Headers.MessageId] = "message-1",
            },
            body: """{"id":42}"""u8.ToArray()
        );

        // when
        var result = await transport.SendAsync(message, AbortToken);

        // then
        result.Succeeded.Should().BeTrue();
        await subscriber
            .Received(1)
            .PublishAsync(
                Arg.Is<RedisChannel>(channel => channel == RedisChannel.Literal("OrderCreated")),
                Arg.Any<RedisValue>(),
                Arg.Any<CommandFlags>()
            );
    }

    [Fact]
    [SuppressMessage(
        "Reliability",
        "CA2025",
        Justification = "NSubstitute returns a borrowed mock connection; the test does not transfer ownership."
    )]
    public async Task should_return_failed_when_publish_fails()
    {
        // given
        var connectionProvider = Substitute.For<IRedisPubSubConnectionProvider>();
        var connection = Substitute.For<IConnectionMultiplexer>();
        var subscriber = Substitute.For<ISubscriber>();
        connection.GetSubscriber().Returns(subscriber);
        connectionProvider.ConnectAsync(Arg.Any<CancellationToken>()).Returns(_ReturnConnectionAsync(connection));
        subscriber
            .PublishAsync(Arg.Any<RedisChannel>(), Arg.Any<RedisValue>(), Arg.Any<CommandFlags>())
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Network error"));

        var logger = Substitute.For<ILogger<RedisPubSubBusTransport>>();
        await using var transport = new RedisPubSubBusTransport(connectionProvider, _Options, logger);

        var message = new TransportMessage(
            headers: new Dictionary<string, string?>(StringComparer.Ordinal) { [Headers.MessageName] = "OrderCreated" },
            body: "test"u8.ToArray()
        );

        // when
        var result = await transport.SendAsync(message, AbortToken);

        // then
        result.Succeeded.Should().BeFalse();
        result.Exception.Should().NotBeNull();
        result.Exception!.Message.Should().Contain("Network error");
    }

    [Fact]
    public async Task should_propagate_cancellation_before_connecting()
    {
        // given
        var connectionProvider = Substitute.For<IRedisPubSubConnectionProvider>();
        var logger = Substitute.For<ILogger<RedisPubSubBusTransport>>();
        await using var transport = new RedisPubSubBusTransport(connectionProvider, _Options, logger);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var message = new TransportMessage(
            headers: new Dictionary<string, string?>(StringComparer.Ordinal) { [Headers.MessageName] = "OrderCreated" },
            body: "test"u8.ToArray()
        );

        // when
        var act = () => transport.SendAsync(message, cts.Token);

        // then
        await act.Should().ThrowAsync<OperationCanceledException>();
        await connectionProvider.DidNotReceive().ConnectAsync(Arg.Any<CancellationToken>());
    }

    private static async Task<IConnectionMultiplexer> _ReturnConnectionAsync(IConnectionMultiplexer connection)
    {
        await Task.Yield();

        return connection;
    }
}
