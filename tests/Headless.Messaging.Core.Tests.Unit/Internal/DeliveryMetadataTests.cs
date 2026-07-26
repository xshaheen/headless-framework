// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Internal;

namespace Tests.Internal;

public sealed class DeliveryMetadataTests
{
    [Fact]
    public void should_parse_only_exact_finite_delivery_modes()
    {
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Headers.RequestedDeliveryMode] = nameof(DeliveryMode.Auto),
            [Headers.ResolvedDeliveryMode] = nameof(DeliveryMode.TransportDirect),
        };

        var delivery = DeliveryMetadata.Read(headers);

        delivery.RequestedDeliveryMode.Should().Be(DeliveryMode.Auto);
        delivery.ResolvedDeliveryMode.Should().Be(DeliveryMode.TransportDirect);
    }

    [Fact]
    public void should_not_project_unbounded_or_malformed_values()
    {
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Headers.RequestedDeliveryMode] = "auto",
            [Headers.ResolvedDeliveryMode] = "customer-controlled-value",
        };

        var delivery = DeliveryMetadata.Read(headers);

        delivery.RequestedDeliveryMode.Should().BeNull();
        delivery.ResolvedDeliveryMode.Should().BeNull();
    }

    [Fact]
    public void should_derive_durable_only_for_readable_legacy_stored_envelopes()
    {
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal);

        DeliveryMetadata.Read(headers).Should().Be(default(DeliveryMetadataValues));
        DeliveryMetadata.ReadStoredHeaders(headers).Should().Be(new DeliveryMetadataValues(null, DeliveryMode.Durable));
    }

    [Fact]
    public void should_not_treat_malformed_stored_metadata_as_legacy()
    {
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Headers.RequestedDeliveryMode] = "auto",
            [Headers.ResolvedDeliveryMode] = "customer-controlled-value",
        };

        DeliveryMetadata.ReadStoredHeaders(headers).Should().Be(default(DeliveryMetadataValues));
    }
}
