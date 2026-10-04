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
            [Headers.RequestedDeliveryMode] = nameof(DeliveryMode.Durable),
            [Headers.ResolvedDeliveryMode] = nameof(DeliveryMode.Direct),
            [Headers.DeliveryCoordinated] = "true",
        };

        var delivery = DeliveryMetadata.Read(headers);

        delivery.RequestedDeliveryMode.Should().Be(DeliveryMode.Durable);
        delivery.ResolvedDeliveryMode.Should().Be(DeliveryMode.Direct);
        delivery.IsCoordinated.Should().BeTrue();
    }

    [Fact]
    public void should_not_project_unbounded_or_malformed_values()
    {
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Headers.RequestedDeliveryMode] = "auto",
            [Headers.ResolvedDeliveryMode] = "customer-controlled-value",
            [Headers.DeliveryCoordinated] = "always",
        };

        var delivery = DeliveryMetadata.Read(headers);

        delivery.RequestedDeliveryMode.Should().BeNull();
        delivery.ResolvedDeliveryMode.Should().BeNull();
        delivery.IsCoordinated.Should().BeNull();
    }

    [Fact]
    public void should_read_a_row_without_the_coordination_header_as_unknown_rather_than_not_coordinated()
    {
        // Rows stamped before the coordination header existed carry the delivery modes only. Reporting them as
        // "not coordinated" asserts an answer the row never recorded, so the value must stay null.
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Headers.RequestedDeliveryMode] = nameof(DeliveryMode.Durable),
            [Headers.ResolvedDeliveryMode] = nameof(DeliveryMode.Durable),
        };

        DeliveryMetadata.Read(headers).IsCoordinated.Should().BeNull();

        var stored = DeliveryMetadata.ReadStoredHeaders(headers);
        stored.ResolvedDeliveryMode.Should().Be(DeliveryMode.Durable);
        stored.IsCoordinated.Should().BeNull();
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public void should_project_the_stamped_coordination_literals(string headerValue, bool expected)
    {
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Headers.DeliveryCoordinated] = headerValue,
        };

        DeliveryMetadata.Read(headers).IsCoordinated.Should().Be(expected);
    }

    [Fact]
    public void should_stamp_the_coordination_header_true_for_a_coordinated_decision()
    {
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal);

        DeliveryMetadata.Stamp(headers, _Decision(requireCoordination: true, coordinated: true));

        headers[Headers.DeliveryCoordinated].Should().Be("true");
        DeliveryMetadata.Read(headers).IsCoordinated.Should().BeTrue();
    }

    [Fact]
    public void should_stamp_the_coordination_header_false_for_an_autonomous_decision()
    {
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal);

        DeliveryMetadata.Stamp(headers, _Decision(requireCoordination: false, coordinated: false));

        headers[Headers.DeliveryCoordinated].Should().Be("false");
        DeliveryMetadata.Read(headers).IsCoordinated.Should().BeFalse();
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

    private static DeliveryDecision _Decision(bool requireCoordination, bool coordinated)
    {
        var coordination = coordinated
#pragma warning disable CA2000 // The returned coordination carries the unit of work.
            ? DeliveryCoordination.Compatible(FakeUnitOfWorks.CreateActive(), transaction: null)
#pragma warning restore CA2000
            : DeliveryCoordination.None;

        return DeliveryDecisionResolver.Resolve(
            MessageLane.Bus,
            DeliveryMode.Durable,
            requireCoordination,
            delay: null,
            coordination,
            DateTimeOffset.UnixEpoch
        );
    }
}
