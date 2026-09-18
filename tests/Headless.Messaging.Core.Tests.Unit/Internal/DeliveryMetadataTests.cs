// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Internal;
using Headless.UnitOfWork;

namespace Tests.Internal;

public sealed class DeliveryMetadataTests
{
    [Fact]
    public void should_parse_only_exact_finite_delivery_modes()
    {
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Headers.RequestedDeliveryMode] = nameof(DeliveryMode.Durable),
            [Headers.ResolvedDeliveryMode] = nameof(DeliveryMode.Direct),
            [Headers.RequestedEnlistment] = nameof(TransactionEnlistment.Required),
        };

        var delivery = DeliveryMetadata.Read(headers);

        delivery.RequestedDeliveryMode.Should().Be(DeliveryMode.Durable);
        delivery.ResolvedDeliveryMode.Should().Be(DeliveryMode.Direct);
        delivery.RequestedEnlistment.Should().Be(TransactionEnlistment.Required);
    }

    [Fact]
    public void should_not_project_unbounded_or_malformed_values()
    {
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Headers.RequestedDeliveryMode] = "auto",
            [Headers.ResolvedDeliveryMode] = "customer-controlled-value",
            [Headers.RequestedEnlistment] = "always",
        };

        var delivery = DeliveryMetadata.Read(headers);

        delivery.RequestedDeliveryMode.Should().BeNull();
        delivery.ResolvedDeliveryMode.Should().BeNull();
        delivery.RequestedEnlistment.Should().BeNull();
    }

    [Fact]
    public void should_leave_the_enlistment_unrecorded_for_legacy_stored_envelopes()
    {
        // Rows stamped before the enlistment header existed carry the delivery modes only.
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Headers.RequestedDeliveryMode] = nameof(DeliveryMode.Durable),
            [Headers.ResolvedDeliveryMode] = nameof(DeliveryMode.Durable),
        };

        var delivery = DeliveryMetadata.ReadStoredHeaders(headers);

        delivery.ResolvedDeliveryMode.Should().Be(DeliveryMode.Durable);
        delivery.RequestedEnlistment.Should().BeNull();
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
