// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.AzureServiceBus;

namespace Tests;

public sealed class ServiceBusProcessorFacadeTests
{
    [Fact]
    public void should_throw_when_both_processors_are_null()
    {
        // when
        var act = () => new ServiceBusProcessorFacade();

        // then
        act.Should()
            .ThrowExactly<ArgumentNullException>()
            .WithMessage("*Either serviceBusProcessor or serviceBusSessionProcessor must be provided*");
    }
}
