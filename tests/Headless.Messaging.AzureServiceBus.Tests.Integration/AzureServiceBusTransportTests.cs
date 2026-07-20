// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Testing.Tests;
using MessagingHeaders = Headless.Messaging.Headers;

namespace Tests;

[Collection("AzureServiceBus")]
public sealed class AzureServiceBusTransportTests(AzureServiceBusFixture fixture) : TestBase
{
    [Fact]
    public async Task should_fan_out_bus_delivery_to_distinct_subscriptions()
    {
        var topicName = await fixture.CreateTopicAsync(AbortToken);
        var messageName = $"message-{Guid.NewGuid():N}";
        await using var first = await fixture.CreateBusSessionAsync(topicName, messageName, AbortToken);
        await using var second = await fixture.CreateBusSessionAsync(topicName, messageName, AbortToken);
        await first.StartAsync(cancellationToken: AbortToken);
        await second.StartAsync(cancellationToken: AbortToken);

        var expectedId = Guid.NewGuid().ToString("N");
        var result = await first.PublishAsync(_CreateMessage(messageName, expectedId), AbortToken);
        result.Succeeded.Should().BeTrue();

        var firstDelivery = await first.ReceiveAsync(TimeSpan.FromSeconds(20), AbortToken);
        var secondDelivery = await second.ReceiveAsync(TimeSpan.FromSeconds(20), AbortToken);
        firstDelivery.Message.Id.Should().Be(expectedId);
        secondDelivery.Message.Id.Should().Be(expectedId);
        firstDelivery.Message.Headers["x-headless-conformance"].Should().Be("azure-bus-fanout");
        secondDelivery.Message.Headers["x-headless-conformance"].Should().Be("azure-bus-fanout");
        await first.Consumer.CommitAsync(firstDelivery.SettlementValue, AbortToken);
        await second.Consumer.CommitAsync(secondDelivery.SettlementValue, AbortToken);
    }

    private static TransportMessage _CreateMessage(string messageName, string messageId)
    {
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [MessagingHeaders.MessageId] = messageId,
            [MessagingHeaders.MessageName] = messageName,
            ["x-headless-conformance"] = "azure-bus-fanout",
        };

        return new TransportMessage(headers, "azure-bus-probe"u8.ToArray());
    }
}
