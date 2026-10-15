// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net.Sockets;
using Headless.Messaging.RabbitMq;
using Headless.Testing.Tests;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;

namespace Tests;

public sealed class RabbitMqBrokerFailureTests : TestBase
{
    [Fact]
    public void should_count_a_lost_or_refused_connection()
    {
        RabbitMqTransport.IsBrokerUnreachable(new BrokerUnreachableException(new SocketException())).Should().BeTrue();
        RabbitMqTransport
            .IsBrokerUnreachable(
                new AlreadyClosedException(new ShutdownEventArgs(ShutdownInitiator.Library, 0, "lost"))
            )
            .Should()
            .BeTrue();
        RabbitMqTransport
            .IsBrokerUnreachable(
                new AlreadyClosedException(
                    new ShutdownEventArgs(ShutdownInitiator.Peer, Constants.ConnectionForced, "forced")
                )
            )
            .Should()
            .BeTrue();
    }

    [Fact]
    public void should_not_count_a_channel_the_broker_closed_over_the_message()
    {
        // 404 NOT_FOUND: the exchange does not exist, so the broker answered.
        RabbitMqTransport
            .IsBrokerUnreachable(
                new AlreadyClosedException(new ShutdownEventArgs(ShutdownInitiator.Peer, 404, "NOT_FOUND"))
            )
            .Should()
            .BeFalse();
        RabbitMqTransport.IsBrokerUnreachable(new InvalidOperationException("nack")).Should().BeFalse();
    }
}
