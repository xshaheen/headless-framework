// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Testing.Tests;

namespace Tests;

public sealed class TransportInterfaceTests : TestBase
{
    [Fact]
    public void should_extend_shared_transport_contract_when_bus_transport()
    {
        // then
        typeof(ITransport).IsAssignableFrom(typeof(IBusTransport)).Should().BeTrue();
    }

    [Fact]
    public void should_extend_shared_transport_contract_when_queue_transport()
    {
        // then
        typeof(ITransport).IsAssignableFrom(typeof(IQueueTransport)).Should().BeTrue();
    }
}
