// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Testing.Tests;

namespace Tests;

public sealed class MediumMessageTests : TestBase
{
    [Theory]
    [InlineData(null)]
    [InlineData("order-42")]
    public async Task should_preserve_routing_affinity_through_durable_and_transport_serialization(string? key)
    {
        var serializer = new Headless.Messaging.JsonUtf8Serializer(
            Microsoft.Extensions.Options.Options.Create(new Headless.Messaging.MessagingOptions())
        );
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (key is not null)
        {
            headers[Headers.RoutingAffinityKey] = key;
        }

        var message = new Message(headers, new { Value = "payload" });
        var content = serializer.Serialize(message);
        var durable = new MediumMessage
        {
            StorageId = Guid.NewGuid(),
            Origin = serializer.Deserialize(content)!,
            Content = content,
            Lane = MessageLane.Queue,
        };
        var transport = await serializer.SerializeToTransportMessageAsync(durable.Origin, AbortToken);

        durable.RoutingAffinityKey.Should().Be(key);
        transport.RoutingAffinityKey.Should().Be(key);
        transport.Headers.ContainsKey(Headers.RoutingAffinityKey).Should().Be(key is not null);
    }
}
