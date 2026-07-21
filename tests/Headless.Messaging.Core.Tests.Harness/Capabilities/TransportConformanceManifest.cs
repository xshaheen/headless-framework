// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Frozen;
using Headless.Messaging;
using Headless.Messaging.Configuration;

namespace Tests.Capabilities;

/// <summary>Broker-observed scenarios tracked for every messaging transport provider.</summary>
[PublicAPI]
public enum TransportConformanceScenario
{
    QueueRoundTrip,
    BusRoundTrip,
    HeaderRoundTrip,
    EmptyBodyDispatch,
    CommitSettlement,
    RejectRedelivery,
    ConsumerPauseRecovery,
    BrokerInterruptionRecovery,
    StaleSettlement,
    HandlerFailureRedelivery,
    BoundedGracefulShutdown,
}

/// <summary>
/// Test-only snapshot of the immutable production transport descriptor expected from a provider package.
/// The production descriptor remains runtime authority; the manifest only compares evidence against it.
/// </summary>
[PublicAPI]
public sealed record TransportRuntimeCapabilityExpectation(
    string Provider,
    bool SupportsBus,
    bool SupportsQueue,
    bool SupportsIndependentLaneTopology
)
{
    public static TransportRuntimeCapabilityExpectation Disabled(string provider)
    {
        return new TransportRuntimeCapabilityExpectation(provider, false, false, false);
    }

    public IReadOnlyList<string> GetMismatchErrors(MessagingProviderCapabilities actual)
    {
        var errors = new List<string>();

        if (actual.Role != MessagingProviderRole.Transport)
        {
            errors.Add($"{Provider}: production descriptor role must be Transport, not {actual.Role}.");
        }

        if (!string.Equals(actual.Provider, Provider, StringComparison.Ordinal))
        {
            errors.Add($"{Provider}: production descriptor uses provider id '{actual.Provider}'.");
        }

        var supportsBus = actual.Lanes.Contains(MessageLane.Bus);
        var supportsQueue = actual.Lanes.Contains(MessageLane.Queue);

        if (supportsBus != SupportsBus)
        {
            errors.Add($"{Provider}: production Bus support is {supportsBus}; manifest expects {SupportsBus}.");
        }

        if (supportsQueue != SupportsQueue)
        {
            errors.Add($"{Provider}: production Queue support is {supportsQueue}; manifest expects {SupportsQueue}.");
        }

        if (actual.SupportsIndependentLaneTopology != SupportsIndependentLaneTopology)
        {
            errors.Add(
                $"{Provider}: production independent-lane topology support is {actual.SupportsIndependentLaneTopology}; manifest expects {SupportsIndependentLaneTopology}."
            );
        }

        return errors;
    }
}

/// <summary>Conformance declarations for one provider's broker-backed integration leaf.</summary>
[PublicAPI]
public sealed record TransportConformanceProfile(
    string Provider,
    bool IsRealBrokerLeafEnabled,
    TransportRuntimeCapabilityExpectation ExpectedRuntimeCapabilities,
    FrozenDictionary<TransportConformanceScenario, ConformanceSupport> Scenarios
)
{
    private const string _GapIssueUrl = "https://github.com/xshaheen/headless-framework/issues/359";

    public static TransportConformanceProfile CreateDisabled(string provider)
    {
        var scenarios = Enum.GetValues<TransportConformanceScenario>()
            .ToDictionary(
                scenario => scenario,
                scenario =>
                    ConformanceSupport.Unsupported(
                        $"{provider} does not yet have executable evidence for {scenario}.",
                        _GapIssueUrl
                    )
            );

        return new TransportConformanceProfile(
            provider,
            false,
            TransportRuntimeCapabilityExpectation.Disabled(provider),
            scenarios.ToFrozenDictionary()
        );
    }

    public TransportConformanceProfile EnableRealBrokerLeaf()
    {
        return this with { IsRealBrokerLeafEnabled = true };
    }

    public TransportConformanceProfile WithRuntimeCapabilities(
        string provider,
        bool supportsBus,
        bool supportsQueue,
        bool supportsIndependentLaneTopology
    )
    {
        return this with
        {
            ExpectedRuntimeCapabilities = new TransportRuntimeCapabilityExpectation(
                provider,
                supportsBus,
                supportsQueue,
                supportsIndependentLaneTopology
            ),
        };
    }

    public TransportConformanceProfile WithScenario(TransportConformanceScenario scenario, ConformanceSupport support)
    {
        var scenarios = Scenarios.ToDictionary();
        scenarios[scenario] = support;
        return this with { Scenarios = scenarios.ToFrozenDictionary() };
    }
}

/// <summary>Authoritative provider/scenario roster for broker-backed messaging conformance tests.</summary>
[PublicAPI]
public static class TransportConformanceManifest
{
    private static readonly TransportConformanceScenario[] _MandatoryBaselineScenarios =
    [
        TransportConformanceScenario.QueueRoundTrip,
        TransportConformanceScenario.HeaderRoundTrip,
        TransportConformanceScenario.CommitSettlement,
        TransportConformanceScenario.RejectRedelivery,
    ];

    public static FrozenDictionary<string, TransportConformanceProfile> Providers { get; } =
        new Dictionary<string, TransportConformanceProfile>(StringComparer.Ordinal)
        {
            ["NATS"] = TransportConformanceProfile
                .CreateDisabled("NATS")
                .WithRuntimeCapabilities(
                    "NATS JetStream",
                    supportsBus: true,
                    supportsQueue: true,
                    supportsIndependentLaneTopology: false
                )
                .WithScenario(TransportConformanceScenario.QueueRoundTrip, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.BusRoundTrip, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.HeaderRoundTrip, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.EmptyBodyDispatch, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.CommitSettlement, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.RejectRedelivery, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.ConsumerPauseRecovery, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.BoundedGracefulShutdown, ConformanceSupport.Supported)
                .EnableRealBrokerLeaf(),
            ["RabbitMQ"] = TransportConformanceProfile
                .CreateDisabled("RabbitMQ")
                .WithRuntimeCapabilities(
                    "RabbitMQ",
                    supportsBus: true,
                    supportsQueue: true,
                    supportsIndependentLaneTopology: false
                )
                .WithScenario(TransportConformanceScenario.QueueRoundTrip, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.BusRoundTrip, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.HeaderRoundTrip, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.EmptyBodyDispatch, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.CommitSettlement, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.RejectRedelivery, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.ConsumerPauseRecovery, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.BoundedGracefulShutdown, ConformanceSupport.Supported)
                .EnableRealBrokerLeaf(),
            ["AWS/LocalStack"] = TransportConformanceProfile
                .CreateDisabled("AWS/LocalStack")
                .WithRuntimeCapabilities(
                    "Amazon SQS",
                    supportsBus: true,
                    supportsQueue: true,
                    supportsIndependentLaneTopology: true
                )
                .WithScenario(TransportConformanceScenario.QueueRoundTrip, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.BusRoundTrip, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.HeaderRoundTrip, ConformanceSupport.Supported)
                .WithScenario(
                    TransportConformanceScenario.EmptyBodyDispatch,
                    ConformanceSupport.NotApplicable(
                        "Amazon SNS rejects empty message bodies at the protocol boundary."
                    )
                )
                .WithScenario(TransportConformanceScenario.CommitSettlement, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.RejectRedelivery, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.BoundedGracefulShutdown, ConformanceSupport.Supported)
                .EnableRealBrokerLeaf(),
            ["Kafka"] = TransportConformanceProfile
                .CreateDisabled("Kafka")
                .WithRuntimeCapabilities(
                    "Kafka",
                    supportsBus: false,
                    supportsQueue: true,
                    supportsIndependentLaneTopology: false
                )
                .WithScenario(TransportConformanceScenario.QueueRoundTrip, ConformanceSupport.Supported)
                .WithScenario(
                    TransportConformanceScenario.BusRoundTrip,
                    ConformanceSupport.NotApplicable(
                        "The current Kafka transport contract is queue/consumer-group based and has no fanout bus topology."
                    )
                )
                .WithScenario(TransportConformanceScenario.HeaderRoundTrip, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.CommitSettlement, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.RejectRedelivery, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.ConsumerPauseRecovery, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.BoundedGracefulShutdown, ConformanceSupport.Supported)
                .EnableRealBrokerLeaf(),
            ["Pulsar"] = TransportConformanceProfile
                .CreateDisabled("Pulsar")
                .WithRuntimeCapabilities(
                    "Apache Pulsar",
                    supportsBus: true,
                    supportsQueue: true,
                    supportsIndependentLaneTopology: false
                )
                .WithScenario(TransportConformanceScenario.QueueRoundTrip, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.BusRoundTrip, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.HeaderRoundTrip, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.CommitSettlement, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.RejectRedelivery, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.ConsumerPauseRecovery, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.BoundedGracefulShutdown, ConformanceSupport.Supported)
                .EnableRealBrokerLeaf(),
            ["Azure Service Bus"] = TransportConformanceProfile
                .CreateDisabled("Azure Service Bus")
                .WithRuntimeCapabilities(
                    "Azure Service Bus",
                    supportsBus: true,
                    supportsQueue: true,
                    supportsIndependentLaneTopology: true
                )
                .WithScenario(TransportConformanceScenario.QueueRoundTrip, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.BusRoundTrip, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.HeaderRoundTrip, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.CommitSettlement, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.RejectRedelivery, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.ConsumerPauseRecovery, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.BoundedGracefulShutdown, ConformanceSupport.Supported)
                .EnableRealBrokerLeaf(),
        }.ToFrozenDictionary(StringComparer.Ordinal);

    public static IReadOnlyList<string> GetValidationErrors()
    {
        return Providers.Values.SelectMany(GetValidationErrors).ToList();
    }

    public static IReadOnlyList<string> GetValidationErrors(TransportConformanceProfile profile)
    {
        var errors = new List<string>();
        var expectedScenarios = Enum.GetValues<TransportConformanceScenario>();

        if (string.IsNullOrWhiteSpace(profile.Provider))
        {
            errors.Add("Conformance profiles require a provider name.");
        }

        var missingScenarios = expectedScenarios.Except(profile.Scenarios.Keys).ToList();
        var unknownScenarios = profile.Scenarios.Keys.Except(expectedScenarios).ToList();

        foreach (var scenario in missingScenarios)
        {
            errors.Add($"{profile.Provider} is missing the {scenario} conformance cell.");
        }

        foreach (var scenario in unknownScenarios)
        {
            errors.Add($"{profile.Provider} declares the unknown {scenario} conformance cell.");
        }

        foreach (var (scenario, support) in profile.Scenarios)
        {
            errors.AddRange(support.GetValidationErrors(scenario).Select(error => $"{profile.Provider}: {error}"));
        }

        _ValidateLaneRoundTrip(
            profile,
            TransportConformanceScenario.BusRoundTrip,
            profile.ExpectedRuntimeCapabilities.SupportsBus,
            "Bus",
            errors
        );
        _ValidateLaneRoundTrip(
            profile,
            TransportConformanceScenario.QueueRoundTrip,
            profile.ExpectedRuntimeCapabilities.SupportsQueue,
            "Queue",
            errors
        );

        if (profile.IsRealBrokerLeafEnabled)
        {
            foreach (var scenario in _MandatoryBaselineScenarios)
            {
                if (
                    !profile.Scenarios.TryGetValue(scenario, out var support)
                    || support.Status != ConformanceStatus.Supported
                )
                {
                    errors.Add($"{profile.Provider}: enabled real-broker leaves must support {scenario}.");
                }
            }
        }

        return errors;
    }

    private static void _ValidateLaneRoundTrip(
        TransportConformanceProfile profile,
        TransportConformanceScenario scenario,
        bool runtimeSupportsLane,
        string lane,
        ICollection<string> errors
    )
    {
        if (!profile.Scenarios.TryGetValue(scenario, out var support))
        {
            return;
        }

        if (runtimeSupportsLane && support.Status != ConformanceStatus.Supported)
        {
            errors.Add($"{profile.Provider}: production {lane} support requires executable {scenario} evidence.");
        }
        else if (!runtimeSupportsLane && support.Status == ConformanceStatus.Supported)
        {
            errors.Add($"{profile.Provider}: {scenario} cannot be Supported when production does not support {lane}.");
        }
    }
}
