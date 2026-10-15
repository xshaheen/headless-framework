// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Pulsar;
using Headless.Testing.Tests;

namespace Tests;

public sealed class PulsarBrokerFailureTests : TestBase
{
    [Fact]
    public void should_count_a_lost_connection_or_a_broker_that_does_not_answer()
    {
        PulsarTransport
            .IsBrokerUnreachable(new global::Pulsar.Client.Api.ConnectException("refused"))
            .Should()
            .BeTrue();
        PulsarTransport
            .IsBrokerUnreachable(new global::Pulsar.Client.Api.TimeoutException("timed out"))
            .Should()
            .BeTrue();
    }

    [Fact]
    public void should_not_count_a_closed_producer_or_a_failed_lookup()
    {
        PulsarTransport
            .IsBrokerUnreachable(new global::Pulsar.Client.Api.AlreadyClosedException("closed"))
            .Should()
            .BeFalse();
        PulsarTransport
            .IsBrokerUnreachable(new global::Pulsar.Client.Api.LookupException("no topic"))
            .Should()
            .BeFalse();
    }
}
