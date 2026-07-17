// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Frozen;
using System.Text.RegularExpressions;
using Headless.Testing.Tests;
using Tests.Capabilities;

namespace Tests;

public sealed partial class TransportConformanceManifestTests : TestBase
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
                    ConformanceStatus.Supported when profile.Provider == "Azure Service Bus" => "S†",
                    ConformanceStatus.Supported => "S",
                    ConformanceStatus.Unsupported => "U",
                    ConformanceStatus.NotApplicable => "N/A",
                    _ => throw new ArgumentOutOfRangeException(nameof(support.Status), support.Status, null),
                };
            });

            readme.Should().Contain($"| `{scenario}` | {string.Join(" | ", cells)} |");
        }
    }

    // The following tests close the manifest<->CI-roster drift seam: the manifest is the source of
    // truth, but the workflow expected_tests rosters and matrix are hand-authored YAML. Without a
    // reconciling check a Supported cell can lose its roster line, or a real-broker leaf can lose
    // its matrix job, and the conformance gate stays green while coverage silently drops. The two
    // bridge maps below are the single place that convention lives; changing a provider or scenario
    // must update a map plus the YAML in lockstep, or these tests fail loudly.

    [Fact]
    public void should_cover_every_real_broker_leaf_in_the_ci_matrix()
    {
        var transport = File.ReadAllText(_TransportWorkflowPath());

        var matrixProviders = _ProviderMatrixRegex()
            .Matches(transport)
            .Select(match => match.Groups["name"].Value)
            .ToHashSet(StringComparer.Ordinal);

        // Azure Service Bus is gated by its own credential-protected workflow, not the matrix.
        matrixProviders.Add("Azure Service Bus");

        var realBrokerLeaves = TransportConformanceManifest
            .Providers.Values.Where(profile => profile.IsRealBrokerLeafEnabled)
            .Select(profile => profile.Provider)
            .ToHashSet(StringComparer.Ordinal);

        matrixProviders.Should().BeEquivalentTo(realBrokerLeaves);
    }

    [Fact]
    public void should_map_every_supported_scenario_to_a_gated_test()
    {
        var mappedScenarios = _ScenarioTestMethods.Select(entry => entry.Scenario).ToHashSet();

        foreach (var profile in TransportConformanceManifest.Providers.Values)
        {
            foreach (var (scenario, support) in profile.Scenarios)
            {
                if (support.Status == ConformanceStatus.Supported)
                {
                    mappedScenarios
                        .Should()
                        .Contain(
                            scenario,
                            $"{profile.Provider} marks {scenario} Supported, so it needs a gated conformance test in _ScenarioTestMethods"
                        );
                }
            }
        }
    }

    [Fact]
    public void should_gate_exactly_the_supported_scenarios_per_provider()
    {
        var workflowText = File.ReadAllText(_TransportWorkflowPath()) + "\n" + File.ReadAllText(_AzureWorkflowPath());

        foreach (var provider in TransportConformanceManifest.Providers.Keys)
        {
            var classes = _ProviderTestClasses[provider];
            var providerClassNames = new[] { classes.Consumer, classes.BrokerFault, classes.Transport }
                .Where(name => name is not null)
                .ToHashSet(StringComparer.Ordinal);

            var actualRoster = _RosterEntryRegex()
                .Matches(workflowText)
                .Where(match => providerClassNames.Contains(match.Groups["class"].Value))
                .Select(match => match.Value)
                .ToHashSet(StringComparer.Ordinal);

            actualRoster
                .Should()
                .BeEquivalentTo(
                    _ProjectExpectedRoster(provider),
                    $"the {provider} CI roster must gate exactly the tests implied by its manifest Supported cells"
                );
        }
    }

    private sealed record ProviderTestClasses(string Consumer, string? BrokerFault, string? Transport);

    private enum TestFamily
    {
        Consumer,
        BrokerFault,
        Transport,
    }

    // Manifest provider display name -> the concrete integration test classes whose methods form its
    // CI roster. A null family means the provider gates no test in that family (e.g. AWS has no
    // broker-fault or bus test).
    private static readonly FrozenDictionary<string, ProviderTestClasses> _ProviderTestClasses = new Dictionary<
        string,
        ProviderTestClasses
    >(StringComparer.Ordinal)
    {
        ["NATS"] = new("NatsConsumerClientTests", "NatsBrokerFaultTests", Transport: null),
        ["RabbitMQ"] = new("RabbitMqConsumerClientConformanceTests", "RabbitMqBrokerFaultTests", Transport: null),
        ["AWS/LocalStack"] = new("AmazonSqsConsumerClientConformanceTests", BrokerFault: null, Transport: null),
        ["Kafka"] = new("KafkaConsumerClientConformanceTests", "KafkaBrokerFaultTests", Transport: null),
        ["Pulsar"] = new("PulsarConsumerClientHarnessTests", "PulsarBrokerFaultTests", "PulsarTransportTests"),
        ["Azure Service Bus"] = new(
            "AzureServiceBusConsumerClientHarnessTests",
            "AzureServiceBusBrokerFaultTests",
            "AzureServiceBusTransportTests"
        ),
    }.ToFrozenDictionary(StringComparer.Ordinal);

    // Scenario -> the conformance test method(s) that gate it, and which test family owns the method.
    // Many-to-many by design: round-trip gates both Queue and Header, and Queue is also re-asserted by
    // the isolation test. Scenarios that are Unsupported for every provider (broker-interruption, stale
    // settlement, handler-failure redelivery) intentionally have no entry.
    private static readonly (
        TransportConformanceScenario Scenario,
        string Method,
        TestFamily Family
    )[] _ScenarioTestMethods =
    [
        (
            TransportConformanceScenario.QueueRoundTrip,
            "should_round_trip_queue_message_body_and_headers",
            TestFamily.Consumer
        ),
        (TransportConformanceScenario.QueueRoundTrip, "should_isolate_unique_destinations", TestFamily.Consumer),
        (
            TransportConformanceScenario.HeaderRoundTrip,
            "should_round_trip_queue_message_body_and_headers",
            TestFamily.Consumer
        ),
        (TransportConformanceScenario.EmptyBodyDispatch, "should_dispatch_empty_message_body", TestFamily.Consumer),
        (
            TransportConformanceScenario.CommitSettlement,
            "should_commit_real_delivery_and_prevent_redelivery",
            TestFamily.Consumer
        ),
        (
            TransportConformanceScenario.RejectRedelivery,
            "should_reject_real_delivery_and_observe_redelivery",
            TestFamily.Consumer
        ),
        (
            TransportConformanceScenario.BoundedGracefulShutdown,
            "should_shutdown_idle_consumer_within_bound",
            TestFamily.Consumer
        ),
        (
            TransportConformanceScenario.BoundedGracefulShutdown,
            "should_bound_shutdown_while_handler_is_active",
            TestFamily.Consumer
        ),
        (
            TransportConformanceScenario.ConsumerPauseRecovery,
            "should_resume_delivery_once_after_consumer_pause",
            TestFamily.BrokerFault
        ),
        (
            TransportConformanceScenario.BusRoundTrip,
            "should_fan_out_bus_delivery_to_distinct_subscriptions",
            TestFamily.Transport
        ),
    ];

    private static IReadOnlySet<string> _ProjectExpectedRoster(string provider)
    {
        var classes = _ProviderTestClasses[provider];
        var profile = TransportConformanceManifest.Providers[provider];
        var roster = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var (scenario, method, family) in _ScenarioTestMethods)
        {
            if (profile.Scenarios[scenario].Status != ConformanceStatus.Supported)
            {
                continue;
            }

            var testClass = family switch
            {
                TestFamily.Consumer => classes.Consumer,
                TestFamily.BrokerFault => classes.BrokerFault,
                TestFamily.Transport => classes.Transport,
                _ => throw new InvalidOperationException($"Unknown test family {family}."),
            };

            if (testClass is null)
            {
                throw new InvalidOperationException(
                    $"{provider} supports {scenario} but declares no {family} test class in _ProviderTestClasses."
                );
            }

            roster.Add($"Tests.{testClass}.{method}");
        }

        return roster;
    }

    private static string _TransportWorkflowPath()
    {
        return Path.Combine(_FindRepositoryRoot(), ".github", "workflows", "messaging-transport-conformance.yml");
    }

    private static string _AzureWorkflowPath()
    {
        return Path.Combine(_FindRepositoryRoot(), ".github", "workflows", "azure-service-bus-conformance.yml");
    }

    [GeneratedRegex(@"^\s*provider:\s*(?<name>\S.*?)\s*$", RegexOptions.Multiline, matchTimeoutMilliseconds: 1000)]
    private static partial Regex _ProviderMatrixRegex();

    [GeneratedRegex(
        @"Tests\.(?<class>[A-Za-z0-9_]+)\.(?<method>[A-Za-z0-9_]+)",
        RegexOptions.None,
        matchTimeoutMilliseconds: 1000
    )]
    private static partial Regex _RosterEntryRegex();

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
}
