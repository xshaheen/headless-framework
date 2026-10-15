// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure.Messaging.ServiceBus;
using Headless.Messaging.AzureServiceBus;
using Headless.Testing.Tests;

namespace Tests;

public sealed class AzureServiceBusBrokerFailureTests : TestBase
{
    [Theory]
    [InlineData(ServiceBusFailureReason.ServiceCommunicationProblem)]
    [InlineData(ServiceBusFailureReason.ServiceTimeout)]
    public void should_count_a_namespace_it_could_not_reach(ServiceBusFailureReason reason)
    {
        AzureServiceBusFailures.IsUnreachable(new ServiceBusException("down", reason)).Should().BeTrue();
    }

    [Theory]
    [InlineData(ServiceBusFailureReason.MessagingEntityNotFound)]
    [InlineData(ServiceBusFailureReason.MessageSizeExceeded)]
    [InlineData(ServiceBusFailureReason.QuotaExceeded)]
    [InlineData(ServiceBusFailureReason.ServiceBusy)]
    public void should_not_count_a_refusal(ServiceBusFailureReason reason)
    {
        AzureServiceBusFailures.IsUnreachable(new ServiceBusException("refused", reason)).Should().BeFalse();
    }
}
