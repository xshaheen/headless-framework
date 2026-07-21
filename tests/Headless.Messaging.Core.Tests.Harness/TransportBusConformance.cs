// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Transport;
using MessagingHeaders = Headless.Messaging.Headers;

namespace Tests;

/// <summary>Broker-observed Bus fan-out proof shared by provider integration leaves.</summary>
[PublicAPI]
public static class TransportBusConformance
{
    public static async Task AssertFanOutAsync(
        TransportConsumerConformanceSession first,
        TransportConsumerConformanceSession second,
        CancellationToken cancellationToken
    )
    {
        second.Destination.Should().Be(first.Destination, "Bus fan-out sessions must share one broker destination");
        await first.StartAsync(cancellationToken: cancellationToken);
        await second.StartAsync(cancellationToken: cancellationToken);

        var expectedId = Guid.NewGuid().ToString("N");
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [MessagingHeaders.MessageId] = expectedId,
            [MessagingHeaders.MessageName] = first.Destination,
            [MessagingHeaders.Intent] = nameof(IntentType.Bus),
            ["x-headless-conformance"] = "bus-fanout",
        };

        var result = await first.PublishAsync(
            new TransportMessage(headers, "broker-observed-bus-fanout"u8.ToArray()),
            cancellationToken
        );
        result.Succeeded.Should().BeTrue();

        var firstDelivery = await first.ReceiveAsync(TimeSpan.FromSeconds(20), cancellationToken);
        var secondDelivery = await second.ReceiveAsync(TimeSpan.FromSeconds(20), cancellationToken);

        foreach (var delivery in new[] { firstDelivery, secondDelivery })
        {
            delivery.Message.Id.Should().Be(expectedId);
            delivery.Message.Name.Should().Be(first.Destination);
            delivery.Message.Headers[MessagingHeaders.Intent].Should().Be(nameof(IntentType.Bus));
            delivery.Message.Headers["x-headless-conformance"].Should().Be("bus-fanout");
            delivery.SettlementValue.Should().NotBeNull();
        }

        await first.Consumer.CommitAsync(firstDelivery.SettlementValue, cancellationToken);
        await second.Consumer.CommitAsync(secondDelivery.SettlementValue, cancellationToken);
    }
}
