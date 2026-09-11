// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Configuration;
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
        var expectedProviders = new[]
        {
            "NATS",
            "RabbitMQ",
            "AWS/LocalStack",
            "Kafka",
            "Pulsar",
            "Azure Service Bus",
            "InMemory",
            "Redis",
        };

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

    [Fact]
    public void should_reject_supported_scenario_without_executable_test_binding()
    {
        var profile = TransportConformanceProfile
            .CreateDisabled("Example")
            .WithScenario(TransportConformanceScenario.QueueRoundTrip, ConformanceSupport.Supported);

        var errors = TransportConformanceTestBindings.GetValidationErrors(profile, []);

        errors.Should().ContainSingle(error => error.Contains("QueueRoundTrip", StringComparison.Ordinal));
    }

    [Fact]
    public void should_reject_binding_to_non_test_method()
    {
        var profile = TransportConformanceProfile
            .CreateDisabled("Example")
            .WithScenario(TransportConformanceScenario.QueueRoundTrip, ConformanceSupport.Supported);
        var binding = new TransportConformanceTestBinding(
            TransportConformanceScenario.QueueRoundTrip,
            typeof(EvidenceTarget),
            nameof(EvidenceTarget.not_a_test)
        );

        var errors = TransportConformanceTestBindings.GetValidationErrors(profile, [binding]);

        errors.Should().ContainSingle(error => error.Contains("not an xUnit test", StringComparison.Ordinal));
    }

    [Fact]
    public void should_accept_binding_to_discoverable_test()
    {
        var profile = TransportConformanceProfile
            .CreateDisabled("Example")
            .WithScenario(TransportConformanceScenario.QueueRoundTrip, ConformanceSupport.Supported);
        var binding = new TransportConformanceTestBinding(
            TransportConformanceScenario.QueueRoundTrip,
            typeof(TransportConformanceManifestTests),
            nameof(should_keep_the_committed_manifest_valid)
        );

        TransportConformanceTestBindings.GetValidationErrors(profile, [binding]).Should().BeEmpty();
    }

    [Fact]
    public async Task should_execute_bound_supported_scenario()
    {
        var profile = TransportConformanceProfile
            .CreateDisabled("Example")
            .WithScenario(TransportConformanceScenario.QueueRoundTrip, ConformanceSupport.Supported);
        var target = new EvidenceTarget();
        var binding = new TransportConformanceTestBinding(
            TransportConformanceScenario.QueueRoundTrip,
            typeof(EvidenceTarget),
            nameof(EvidenceTarget.executable_test)
        );

        await TransportConformanceTestBindings.ExecuteSupportedScenariosAsync(profile, [binding], _ => target);

        target.Executed.Should().BeTrue();
    }

    [Fact]
    public void should_reject_binding_to_unconditionally_skipped_test()
    {
        var profile = TransportConformanceProfile
            .CreateDisabled("Example")
            .WithScenario(TransportConformanceScenario.QueueRoundTrip, ConformanceSupport.Supported);
        var binding = new TransportConformanceTestBinding(
            TransportConformanceScenario.QueueRoundTrip,
            typeof(EvidenceTarget),
            nameof(EvidenceTarget.skipped_test)
        );

        var errors = TransportConformanceTestBindings.GetValidationErrors(profile, [binding]);

        errors.Should().ContainSingle(error => error.Contains("unconditionally skipped", StringComparison.Ordinal));
    }

    [Fact]
    public void should_require_bounded_malformed_envelope_evidence()
    {
        var profile = TransportConformanceProfile.CreateDisabled("Example") with { MalformedEnvelopeBound = null };

        var errors = TransportConformanceManifest.GetValidationErrors(profile);

        errors
            .Should()
            .Contain(error => error.Contains("malformed-envelope bound", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void should_require_restart_in_malformed_envelope_observation_window()
    {
        var profile = TransportConformanceProfile.CreateDisabled("Example") with
        {
            MalformedEnvelopeBound = new TransportMalformedEnvelopeBound(
                "native terminal disposition",
                MaximumDeliveryCount: 1,
                ObservationWindow: TimeSpan.FromSeconds(1),
                IncludesBrokerRestart: false
            ),
        };

        var errors = TransportConformanceManifest.GetValidationErrors(profile);

        errors.Should().Contain(error => error.Contains("restart", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void should_track_lane_isolation_and_cutover_as_explicit_scenarios()
    {
        Enum.GetValues<TransportConformanceScenario>()
            .Should()
            .Contain([
                TransportConformanceScenario.BusSubscriberGroupFanOut,
                TransportConformanceScenario.BusReplicaCompetition,
                TransportConformanceScenario.QueueOwnership,
                TransportConformanceScenario.SameNameLaneIsolation,
                TransportConformanceScenario.StartupRejectionBeforeSideEffects,
                TransportConformanceScenario.MalformedEnvelopeTerminalSettlement,
            ]);
    }

    [Fact]
    public void should_compare_test_manifest_snapshot_with_production_descriptor()
    {
        var expected = TransportConformanceManifest.Providers["NATS"].ExpectedRuntimeCapabilities;
        var matching = MessagingProviderCapabilities.Transport(
            "NATS JetStream",
            [MessageLane.Bus, MessageLane.Queue],
            supportsIndependentLaneTopology: true
        );
        var drifted = MessagingProviderCapabilities.Transport(
            "NATS JetStream",
            [MessageLane.Bus],
            supportsIndependentLaneTopology: false
        );

        expected.GetMismatchErrors(matching).Should().BeEmpty();
        expected
            .GetMismatchErrors(drifted)
            .Should()
            .Contain(error => error.Contains("Queue", StringComparison.Ordinal));
        expected
            .GetMismatchErrors(drifted)
            .Should()
            .Contain(error => error.Contains("independent-lane", StringComparison.Ordinal));
    }

    [Fact]
    public void should_keep_test_manifest_out_of_runtime_capability_authority()
    {
        typeof(TransportConformanceManifest)
            .Assembly.Should()
            .NotBeSameAs(typeof(MessagingProviderCapabilities).Assembly);
        typeof(MessagingCapabilityModel)
            .Assembly.GetReferencedAssemblies()
            .Should()
            .NotContain(reference =>
                string.Equals(
                    reference.Name,
                    typeof(TransportConformanceManifest).Assembly.GetName().Name,
                    StringComparison.Ordinal
                )
            );
    }

    [Fact]
    public void should_keep_readme_matrix_aligned_with_manifest_roster()
    {
        var readme = File.ReadAllText(
            Path.Combine(_FindRepositoryRoot(), "tests", "Headless.Messaging.Core.Tests.Harness", "README.md")
        );

        var providerHeader =
            $"| Manifest scenario | {string.Join(" | ", TransportConformanceManifest.Providers.Keys)} |";
        readme.Should().Contain(providerHeader);

        foreach (var scenario in Enum.GetValues<TransportConformanceScenario>())
        {
            var cells = TransportConformanceManifest.Providers.Values.Select(profile =>
            {
                var support = profile.Scenarios[scenario];
                return support.Status switch
                {
                    ConformanceStatus.Supported
                        when string.Equals(profile.Provider, "Azure Service Bus", StringComparison.Ordinal) => "S†",
                    ConformanceStatus.Supported => "S",
                    ConformanceStatus.Unsupported => "U",
                    ConformanceStatus.NotApplicable => "N/A",
                    _ => throw new InvalidOperationException("Unknown conformance status."),
                };
            });

            readme.Should().Contain($"| `{scenario}` | {string.Join(" | ", cells)} |");
        }
    }

    private static string _FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "headless-framework.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate repository root from test output directory.");
    }

    private sealed class EvidenceTarget
    {
        public bool Executed { get; private set; }

        public void not_a_test() { }

        [EvidenceTarget.FactAttribute]
        public async Task executable_test()
        {
            await Task.Yield();
            Executed = true;
        }

        [EvidenceTarget.FactAttribute(Skip = "not executable")]
        public void skipped_test() { }

        [AttributeUsage(AttributeTargets.Method)]
        private sealed class FactAttribute : Attribute
        {
            public string? Skip { get; set; }
        }
    }
}
