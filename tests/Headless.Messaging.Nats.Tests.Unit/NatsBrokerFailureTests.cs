// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Nats;
using Headless.Testing.Tests;
using NATS.Client.Core;

namespace Tests;

public sealed class NatsBrokerFailureTests : TestBase
{
    [Fact]
    public void should_count_a_lost_connection_or_a_silent_server()
    {
        NatsTransport.IsBrokerUnreachable(new NatsConnectionFailedException("lost")).Should().BeTrue();
        NatsTransport.IsBrokerUnreachable(new NatsTimeoutException()).Should().BeTrue();
    }

    [Fact]
    public void should_not_count_a_payload_the_server_refused()
    {
        NatsTransport.IsBrokerUnreachable(new NatsPayloadTooLargeException("too large")).Should().BeFalse();
        NatsTransport.IsBrokerUnreachable(new NatsNoRespondersException()).Should().BeFalse();
    }
}
