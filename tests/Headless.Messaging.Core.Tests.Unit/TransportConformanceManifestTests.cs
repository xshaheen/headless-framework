// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;
using Tests.Capabilities;

namespace Tests;

public sealed class TransportConformanceManifestTests : TestBase
{
    [Fact]
    public void should_require_transport_leaves_to_opt_into_every_capability()
    {
        var capabilities = TransportCapabilities.Default;

        capabilities.SupportsOrdering.Should().BeFalse();
        capabilities.SupportsDeadLetter.Should().BeFalse();
        capabilities.SupportsPriority.Should().BeFalse();
        capabilities.SupportsDelayedDelivery.Should().BeFalse();
        capabilities.SupportsBusTransport.Should().BeFalse();
        capabilities.SupportsQueueTransport.Should().BeFalse();
        capabilities.SupportsHeaders.Should().BeFalse();
    }

    [Fact]
    public void should_require_consumer_leaves_to_opt_into_every_capability()
    {
        var capabilities = ConsumerClientCapabilities.Default;

        capabilities.SupportsFetchTopics.Should().BeFalse();
        capabilities.SupportsConcurrentProcessing.Should().BeFalse();
        capabilities.SupportsReject.Should().BeFalse();
        capabilities.SupportsGracefulShutdown.Should().BeFalse();
    }

    [Fact]
    public void should_define_the_authoritative_provider_and_scenario_roster()
    {
        var expectedProviders = new[] { "NATS", "RabbitMQ", "AWS/LocalStack", "Kafka", "Pulsar", "Azure Service Bus" };

        TransportConformanceManifest.Providers.Keys.Should().BeEquivalentTo(expectedProviders);

        foreach (var profile in TransportConformanceManifest.Providers.Values)
        {
            profile.Scenarios.Keys.Should().BeEquivalentTo(Enum.GetValues<TransportConformanceScenario>());
        }
    }

    [Fact]
    public void should_require_a_rationale_and_issue_for_unsupported_cells()
    {
        var support = new ConformanceSupport(ConformanceStatus.Unsupported, "", "");

        var errors = support.GetValidationErrors(TransportConformanceScenario.BrokerInterruptionRecovery);

        errors.Should().Contain(error => error.Contains("rationale", StringComparison.OrdinalIgnoreCase));
        errors.Should().Contain(error => error.Contains("issue", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void should_require_a_protocol_or_topology_rationale_for_not_applicable_cells()
    {
        var support = new ConformanceSupport(ConformanceStatus.NotApplicable, "", null);

        var errors = support.GetValidationErrors(TransportConformanceScenario.BusRoundTrip);

        errors.Should().ContainSingle(error => error.Contains("rationale", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void should_reject_unsupported_mandatory_cells_for_an_enabled_real_broker_leaf()
    {
        var profile = TransportConformanceProfile.CreateDisabled("Example").EnableRealBrokerLeaf();

        var errors = TransportConformanceManifest.GetValidationErrors(profile);

        errors.Should().Contain(error => error.Contains(nameof(TransportConformanceScenario.QueueRoundTrip)));
        errors.Should().Contain(error => error.Contains(nameof(TransportConformanceScenario.CommitSettlement)));
    }

    [Fact]
    public void should_default_optional_scenarios_to_unsupported()
    {
        var profile = TransportConformanceProfile.CreateDisabled("Example");

        profile
            .Scenarios[TransportConformanceScenario.BrokerInterruptionRecovery]
            .Status.Should()
            .Be(ConformanceStatus.Unsupported);
    }

    [Fact]
    public void should_keep_the_committed_manifest_valid()
    {
        TransportConformanceManifest.GetValidationErrors().Should().BeEmpty();
    }
}
