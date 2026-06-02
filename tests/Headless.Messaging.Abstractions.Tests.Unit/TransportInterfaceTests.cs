// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Testing.Tests;

namespace Tests;

public sealed class TransportInterfaceTests : TestBase
{
    [Fact]
    public void bus_transport_should_extend_shared_transport_contract()
    {
        // then
        typeof(ITransport).IsAssignableFrom(typeof(IBusTransport)).Should().BeTrue();
    }

    [Fact]
    public void queue_transport_should_extend_shared_transport_contract()
    {
        // then
        typeof(ITransport).IsAssignableFrom(typeof(IQueueTransport)).Should().BeTrue();
    }
}
