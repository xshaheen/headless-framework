// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net.Sockets;
using Headless.Messaging.Pulsar;
using Headless.Testing.Tests;

namespace Tests;

public sealed class PulsarBrokerFailureTests : TestBase
{
    [Fact]
    public void should_count_a_send_that_timed_out_or_a_lost_connection()
    {
        PulsarTransport.IsBrokerUnreachable(new TimeoutException("timed out")).Should().BeTrue();
        PulsarTransport.IsBrokerUnreachable(new SocketException((int)SocketError.ConnectionRefused)).Should().BeTrue();
    }

    [Fact]
    public void should_count_a_producer_faulted_for_a_reason_other_than_message_size()
    {
        PulsarTransport.IsBrokerUnreachable(new DotPulsar.Exceptions.ProducerFaultedException()).Should().BeTrue();
        PulsarTransport
            .IsBrokerUnreachable(
                new DotPulsar.Exceptions.ProducerFaultedException(
                    new DotPulsar.Exceptions.AuthenticationException("denied")
                )
            )
            .Should()
            .BeTrue();
    }

    [Fact]
    public void should_not_count_a_producer_faulted_by_message_size_or_a_rejected_message()
    {
        PulsarTransport
            .IsBrokerUnreachable(
                new DotPulsar.Exceptions.ProducerFaultedException(
                    new DotPulsar.Exceptions.TooLargeMessageException(10, 5)
                )
            )
            .Should()
            .BeFalse();
        PulsarTransport
            .IsBrokerUnreachable(new DotPulsar.Exceptions.TooLargeMessageException(10, 5))
            .Should()
            .BeFalse();
    }
}
