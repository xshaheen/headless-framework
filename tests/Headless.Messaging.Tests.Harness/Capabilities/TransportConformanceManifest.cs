// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;

namespace Tests.Capabilities;

/// <summary>Broker-observed scenarios tracked for every messaging transport provider.</summary>
[PublicAPI]
public enum TransportConformanceScenario
{
    RoutingAffinityMappingOrRejection,
    QueueRoundTrip,
    BusRoundTrip,
    HeaderRoundTrip,
    EmptyBodyDispatch,
    CommitSettlement,
    RejectRedelivery,
    ConsumerPauseRecovery,
    BrokerInterruptionRecovery,
    ConnectionDropRecovery,
    PublishDuringOutage,
    StaleSettlement,
    HandlerFailureRedelivery,
    BoundedGracefulShutdown,
    BusConsumerIdentityFanOut,
    BusReplicaCompetition,
    QueueOwnership,
    SameNameLaneIsolation,
    StartupRejectionBeforeSideEffects,
    MalformedEnvelopeTerminalSettlement,
    RequestReplyRoundTrip,
    RequestReplyCallerIsolation,
    RequestReplyForeignAddressRefusal,
    RequestReplyCallerCleanup,
    RequestReplyStartupRejection,
    RawPayloadConsumption,
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

/// <summary>Provider-native evidence bound used to rule out delayed poison-message redelivery.</summary>
[PublicAPI]
public sealed record TransportMalformedEnvelopeBound(
    string TerminalInvariant,
    int MaximumDeliveryCount,
    TimeSpan ObservationWindow,
    bool IncludesBrokerRestart
)
{
    public IReadOnlyList<string> GetValidationErrors()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(TerminalInvariant))
        {
            errors.Add("the malformed-envelope bound requires a provider-native terminal invariant.");
        }

        if (MaximumDeliveryCount <= 0)
        {
            errors.Add("the malformed-envelope bound requires a positive maximum delivery count.");
        }

        if (ObservationWindow <= TimeSpan.Zero)
        {
            errors.Add("the malformed-envelope bound requires a positive observation window.");
        }

        if (!IncludesBrokerRestart)
        {
            errors.Add("the malformed-envelope bound must include broker restart observation.");
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
    FrozenDictionary<TransportConformanceScenario, ConformanceSupport> Scenarios,
    TransportMalformedEnvelopeBound? MalformedEnvelopeBound
)
{
    /// <summary>The umbrella issue a cell links while no run has proven it yet.</summary>
    internal const string GapIssueUrl = "https://github.com/xshaheen/headless-framework/issues/359";

    /// <summary>The open issue that tracks the network-fault scenarios no provider has proven yet.</summary>
    internal const string NetworkFaultGapIssueUrl = "https://github.com/xshaheen/headless-framework/issues/1247";

    public static TransportConformanceProfile CreateDisabled(string provider)
    {
        var scenarios = Enum.GetValues<TransportConformanceScenario>()
            .ToDictionary(
                scenario => scenario,
                scenario =>
                    ConformanceSupport.Unsupported(
                        $"{provider} does not yet have executable evidence for {scenario}.",
                        scenario
                            is TransportConformanceScenario.ConnectionDropRecovery
                                or TransportConformanceScenario.PublishDuringOutage
                            ? NetworkFaultGapIssueUrl
                            : GapIssueUrl
                    )
            );

        return new TransportConformanceProfile(
            provider,
            false,
            TransportRuntimeCapabilityExpectation.Disabled(provider),
            scenarios.ToFrozenDictionary(),
            null
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

    /// <summary>
    /// Sets every request/reply cell a provider that declares the capability proves by running the shared suite; the
    /// startup rejection cell is the opposite case and is set on its own.
    /// </summary>
    public TransportConformanceProfile WithRequestReplyScenarios(ConformanceSupport support)
    {
        return WithScenario(TransportConformanceScenario.RequestReplyRoundTrip, support)
            .WithScenario(TransportConformanceScenario.RequestReplyCallerIsolation, support)
            .WithScenario(TransportConformanceScenario.RequestReplyForeignAddressRefusal, support)
            .WithScenario(TransportConformanceScenario.RequestReplyCallerCleanup, support);
    }

    public TransportConformanceProfile WithMalformedEnvelopeBound(
        string terminalInvariant,
        int maximumDeliveryCount,
        TimeSpan observationWindow
    )
    {
        return this with
        {
            MalformedEnvelopeBound = new TransportMalformedEnvelopeBound(
                terminalInvariant,
                maximumDeliveryCount,
                observationWindow,
                IncludesBrokerRestart: true
            ),
        };
    }
}

/// <summary>Authoritative provider/scenario roster for broker-backed messaging conformance tests.</summary>
[PublicAPI]
public static class TransportConformanceManifest
{
    private const string _RequestReplyIssueUrl = "https://github.com/xshaheen/headless-framework/issues/222";

    private static readonly TransportConformanceScenario[] _MandatoryBaselineScenarios =
    [
        TransportConformanceScenario.RoutingAffinityMappingOrRejection,
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
                .WithScenario(TransportConformanceScenario.ConnectionDropRecovery, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.PublishDuringOutage, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.RawPayloadConsumption, ConformanceSupport.Supported)
                .WithScenario(
                    TransportConformanceScenario.RoutingAffinityMappingOrRejection,
                    ConformanceSupport.Supported
                )
                .WithRuntimeCapabilities(
                    "NATS JetStream",
                    supportsBus: true,
                    supportsQueue: true,
                    supportsIndependentLaneTopology: true
                )
                .WithMalformedEnvelopeBound("JetStream terminal ACK", 1, TimeSpan.FromSeconds(3))
                .WithScenario(TransportConformanceScenario.QueueRoundTrip, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.BusRoundTrip, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.HeaderRoundTrip, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.EmptyBodyDispatch, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.CommitSettlement, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.RejectRedelivery, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.ConsumerPauseRecovery, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.BrokerInterruptionRecovery, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.StaleSettlement, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.HandlerFailureRedelivery, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.BoundedGracefulShutdown, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.BusConsumerIdentityFanOut, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.BusReplicaCompetition, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.QueueOwnership, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.SameNameLaneIsolation, ConformanceSupport.Supported)
                .WithScenario(
                    TransportConformanceScenario.MalformedEnvelopeTerminalSettlement,
                    ConformanceSupport.Supported
                )
                .WithRequestReplyScenarios(ConformanceSupport.Supported)
                .WithScenario(
                    TransportConformanceScenario.RequestReplyStartupRejection,
                    ConformanceSupport.NotApplicable("NATS supports request/reply, so startup accepts it.")
                )
                .EnableRealBrokerLeaf(),
            ["RabbitMQ"] = TransportConformanceProfile
                .CreateDisabled("RabbitMQ")
                .WithScenario(TransportConformanceScenario.ConnectionDropRecovery, ConformanceSupport.Supported)
                .WithScenario(
                    TransportConformanceScenario.PublishDuringOutage,
                    ConformanceSupport.Unsupported(
                        "A publish while the client recovers from a dropped connection replaces the shared connection and cancels the recovery, so the consumer is cancelled and never registers again.",
                        "https://github.com/xshaheen/headless-framework/issues/1243"
                    )
                )
                .WithScenario(TransportConformanceScenario.RawPayloadConsumption, ConformanceSupport.Supported)
                .WithScenario(
                    TransportConformanceScenario.RoutingAffinityMappingOrRejection,
                    ConformanceSupport.Supported
                )
                .WithRuntimeCapabilities(
                    "RabbitMQ",
                    supportsBus: true,
                    supportsQueue: true,
                    supportsIndependentLaneTopology: true
                )
                .WithMalformedEnvelopeBound("basic.reject with requeue disabled", 1, TimeSpan.FromSeconds(3))
                .WithScenario(TransportConformanceScenario.QueueRoundTrip, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.BusRoundTrip, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.HeaderRoundTrip, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.EmptyBodyDispatch, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.CommitSettlement, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.RejectRedelivery, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.ConsumerPauseRecovery, ConformanceSupport.Supported)
                .WithScenario(
                    TransportConformanceScenario.BrokerInterruptionRecovery,
                    ConformanceSupport.Unsupported(
                        "A publish while the client recovers from a broker restart replaces the shared connection, and the consumer on it never receives again.",
                        "https://github.com/xshaheen/headless-framework/issues/1243"
                    )
                )
                .WithScenario(
                    TransportConformanceScenario.StaleSettlement,
                    ConformanceSupport.Unsupported(
                        "Settling a delivery tag a second time closes the channel with PRECONDITION_FAILED, and the competing consumer stops receiving.",
                        "https://github.com/xshaheen/headless-framework/issues/1267"
                    )
                )
                .WithScenario(
                    TransportConformanceScenario.HandlerFailureRedelivery,
                    ConformanceSupport.Unsupported(
                        "A delivery whose callback throws stays unacknowledged on the open channel, so it is never redelivered and the prefetch slot stays taken.",
                        "https://github.com/xshaheen/headless-framework/issues/1268"
                    )
                )
                .WithScenario(TransportConformanceScenario.BoundedGracefulShutdown, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.BusConsumerIdentityFanOut, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.BusReplicaCompetition, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.QueueOwnership, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.SameNameLaneIsolation, ConformanceSupport.Supported)
                .WithScenario(
                    TransportConformanceScenario.MalformedEnvelopeTerminalSettlement,
                    ConformanceSupport.Supported
                )
                .WithRequestReplyScenarios(ConformanceSupport.Supported)
                .WithScenario(
                    TransportConformanceScenario.RequestReplyStartupRejection,
                    ConformanceSupport.NotApplicable("RabbitMQ supports request/reply, so startup accepts it.")
                )
                .EnableRealBrokerLeaf(),
            ["AWS/LocalStack"] = TransportConformanceProfile
                .CreateDisabled("AWS/LocalStack")
                .WithScenario(TransportConformanceScenario.RawPayloadConsumption, ConformanceSupport.Supported)
                .WithScenario(
                    TransportConformanceScenario.RoutingAffinityMappingOrRejection,
                    ConformanceSupport.Supported
                )
                .WithRuntimeCapabilities(
                    "Amazon SQS",
                    supportsBus: true,
                    supportsQueue: true,
                    supportsIndependentLaneTopology: true
                )
                .WithMalformedEnvelopeBound("SQS deletion after terminal classification", 1, TimeSpan.FromSeconds(5))
                .WithScenario(TransportConformanceScenario.BrokerInterruptionRecovery, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.StaleSettlement, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.HandlerFailureRedelivery, ConformanceSupport.Supported)
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
                .WithScenario(TransportConformanceScenario.BusConsumerIdentityFanOut, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.BusReplicaCompetition, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.QueueOwnership, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.SameNameLaneIsolation, ConformanceSupport.Supported)
                .WithScenario(
                    TransportConformanceScenario.MalformedEnvelopeTerminalSettlement,
                    ConformanceSupport.Supported
                )
                .WithRequestReplyScenarios(
                    ConformanceSupport.NotApplicable(
                        "Amazon SQS has no .NET temporary-queue client and bills per-process queue churn, so the provider rejects request/reply at startup."
                    )
                )
                .WithScenario(TransportConformanceScenario.RequestReplyStartupRejection, ConformanceSupport.Supported)
                .EnableRealBrokerLeaf(),
            ["Kafka"] = TransportConformanceProfile
                .CreateDisabled("Kafka")
                .WithScenario(TransportConformanceScenario.RawPayloadConsumption, ConformanceSupport.Supported)
                .WithScenario(
                    TransportConformanceScenario.RoutingAffinityMappingOrRejection,
                    ConformanceSupport.Supported
                )
                .WithRuntimeCapabilities(
                    "Kafka",
                    supportsBus: false,
                    supportsQueue: true,
                    supportsIndependentLaneTopology: false
                )
                .WithMalformedEnvelopeBound(
                    "consumer offset committed past the rejected record",
                    1,
                    TimeSpan.FromSeconds(10)
                )
                .WithScenario(TransportConformanceScenario.QueueRoundTrip, ConformanceSupport.Supported)
                .WithScenario(
                    TransportConformanceScenario.BusRoundTrip,
                    ConformanceSupport.NotApplicable(
                        "The current Kafka transport contract is queue/consumer-group based and has no fanout bus topology."
                    )
                )
                .WithScenario(
                    TransportConformanceScenario.BusConsumerIdentityFanOut,
                    ConformanceSupport.NotApplicable("Kafka is a Queue-only transport.")
                )
                .WithScenario(
                    TransportConformanceScenario.BusReplicaCompetition,
                    ConformanceSupport.NotApplicable("Kafka is a Queue-only transport.")
                )
                .WithScenario(
                    TransportConformanceScenario.SameNameLaneIsolation,
                    ConformanceSupport.NotApplicable(
                        "Kafka rejects Bus registration rather than creating Bus topology."
                    )
                )
                .WithScenario(TransportConformanceScenario.QueueOwnership, ConformanceSupport.Supported)
                .WithScenario(
                    TransportConformanceScenario.StartupRejectionBeforeSideEffects,
                    ConformanceSupport.Supported
                )
                .WithScenario(
                    TransportConformanceScenario.MalformedEnvelopeTerminalSettlement,
                    ConformanceSupport.Supported
                )
                .WithScenario(TransportConformanceScenario.HeaderRoundTrip, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.CommitSettlement, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.RejectRedelivery, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.ConsumerPauseRecovery, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.BrokerInterruptionRecovery, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.StaleSettlement, ConformanceSupport.Supported)
                .WithScenario(
                    TransportConformanceScenario.HandlerFailureRedelivery,
                    ConformanceSupport.Unsupported(
                        "A record whose callback throws is neither committed nor sought back to, so it is never redelivered.",
                        "https://github.com/xshaheen/headless-framework/issues/1268"
                    )
                )
                .WithScenario(TransportConformanceScenario.BoundedGracefulShutdown, ConformanceSupport.Supported)
                .WithRequestReplyScenarios(
                    ConformanceSupport.NotApplicable(
                        "Kafka cannot address one process short of a partition per instance, so the provider rejects request/reply at startup."
                    )
                )
                .WithScenario(TransportConformanceScenario.RequestReplyStartupRejection, ConformanceSupport.Supported)
                .EnableRealBrokerLeaf(),
            ["Pulsar"] = TransportConformanceProfile
                .CreateDisabled("Pulsar")
                .WithScenario(TransportConformanceScenario.RawPayloadConsumption, ConformanceSupport.Supported)
                .WithScenario(
                    TransportConformanceScenario.RoutingAffinityMappingOrRejection,
                    ConformanceSupport.Supported
                )
                .WithRuntimeCapabilities(
                    "Apache Pulsar",
                    supportsBus: true,
                    supportsQueue: true,
                    supportsIndependentLaneTopology: true
                )
                .WithMalformedEnvelopeBound("Pulsar terminal acknowledgement", 1, TimeSpan.FromSeconds(10))
                .WithScenario(
                    TransportConformanceScenario.BrokerInterruptionRecovery,
                    ConformanceSupport.Unsupported(
                        "Delivery resumes after a broker restart, but stopping the consumer afterwards can overrun the shutdown timeout, so the scenario fails intermittently.",
                        "https://github.com/xshaheen/headless-framework/issues/1269"
                    )
                )
                .WithScenario(TransportConformanceScenario.StaleSettlement, ConformanceSupport.Supported)
                .WithScenario(
                    TransportConformanceScenario.HandlerFailureRedelivery,
                    ConformanceSupport.Unsupported(
                        "A message whose callback throws is neither acknowledged nor negatively acknowledged, so the live subscription never redelivers it.",
                        "https://github.com/xshaheen/headless-framework/issues/1268"
                    )
                )
                .WithScenario(TransportConformanceScenario.QueueRoundTrip, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.BusRoundTrip, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.HeaderRoundTrip, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.CommitSettlement, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.RejectRedelivery, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.ConsumerPauseRecovery, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.BoundedGracefulShutdown, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.BusConsumerIdentityFanOut, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.BusReplicaCompetition, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.QueueOwnership, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.SameNameLaneIsolation, ConformanceSupport.Supported)
                .WithScenario(
                    TransportConformanceScenario.MalformedEnvelopeTerminalSettlement,
                    ConformanceSupport.Supported
                )
                .WithRequestReplyScenarios(
                    ConformanceSupport.Unsupported(
                        "The Pulsar reply channel is not built yet, so the provider rejects request/reply at startup.",
                        _RequestReplyIssueUrl
                    )
                )
                .WithScenario(TransportConformanceScenario.RequestReplyStartupRejection, ConformanceSupport.Supported)
                .EnableRealBrokerLeaf(),
            ["Azure Service Bus"] = TransportConformanceProfile
                .CreateDisabled("Azure Service Bus")
                .WithScenario(TransportConformanceScenario.RawPayloadConsumption, ConformanceSupport.Supported)
                .WithScenario(
                    TransportConformanceScenario.RoutingAffinityMappingOrRejection,
                    ConformanceSupport.Supported
                )
                .WithRuntimeCapabilities(
                    "Azure Service Bus",
                    supportsBus: true,
                    supportsQueue: true,
                    supportsIndependentLaneTopology: true
                )
                .WithMalformedEnvelopeBound("Service Bus dead-letter settlement", 1, TimeSpan.FromSeconds(10))
                .WithScenario(TransportConformanceScenario.QueueRoundTrip, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.BusRoundTrip, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.HeaderRoundTrip, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.CommitSettlement, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.RejectRedelivery, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.ConsumerPauseRecovery, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.BoundedGracefulShutdown, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.BusConsumerIdentityFanOut, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.BusReplicaCompetition, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.QueueOwnership, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.SameNameLaneIsolation, ConformanceSupport.Supported)
                .WithScenario(
                    TransportConformanceScenario.BrokerInterruptionRecovery,
                    ConformanceSupport.Unsupported(
                        "The suite runs against a managed Azure Service Bus namespace, which a test cannot restart.",
                        TransportConformanceProfile.GapIssueUrl
                    )
                )
                .WithScenario(
                    TransportConformanceScenario.StaleSettlement,
                    ConformanceSupport.Unsupported(
                        "Needs the protected Azure workflow to execute; no run has proven it yet.",
                        TransportConformanceProfile.GapIssueUrl
                    )
                )
                .WithScenario(
                    TransportConformanceScenario.HandlerFailureRedelivery,
                    ConformanceSupport.Unsupported(
                        "Needs the protected Azure workflow to execute; no run has proven it yet.",
                        TransportConformanceProfile.GapIssueUrl
                    )
                )
                .WithRequestReplyScenarios(
                    ConformanceSupport.Unsupported(
                        "The Azure Service Bus reply channel is not built yet, so the provider rejects request/reply at startup.",
                        _RequestReplyIssueUrl
                    )
                )
                .WithScenario(TransportConformanceScenario.RequestReplyStartupRejection, ConformanceSupport.Supported)
                .EnableRealBrokerLeaf(),
            ["InMemory"] = TransportConformanceProfile
                .CreateDisabled("InMemory")
                .WithScenario(
                    TransportConformanceScenario.RawPayloadConsumption,
                    ConformanceSupport.NotApplicable(
                        "The in-memory transport carries only messages Headless publishes; no foreign producer can reach it."
                    )
                )
                .WithScenario(
                    TransportConformanceScenario.RoutingAffinityMappingOrRejection,
                    ConformanceSupport.Supported
                )
                .WithRuntimeCapabilities(
                    "InMemory",
                    supportsBus: true,
                    supportsQueue: true,
                    supportsIndependentLaneTopology: true
                )
                .WithMalformedEnvelopeBound("invalid in-process delivery discarded", 1, TimeSpan.FromSeconds(1))
                .WithScenario(TransportConformanceScenario.QueueRoundTrip, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.BusRoundTrip, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.HeaderRoundTrip, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.EmptyBodyDispatch, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.CommitSettlement, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.RejectRedelivery, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.BoundedGracefulShutdown, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.BusConsumerIdentityFanOut, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.BusReplicaCompetition, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.QueueOwnership, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.SameNameLaneIsolation, ConformanceSupport.Supported)
                .WithScenario(
                    TransportConformanceScenario.MalformedEnvelopeTerminalSettlement,
                    ConformanceSupport.NotApplicable(
                        "InMemory transports TransportMessage instances directly and has no transport envelope to parse."
                    )
                )
                .WithRequestReplyScenarios(ConformanceSupport.Supported)
                .WithScenario(
                    TransportConformanceScenario.RequestReplyStartupRejection,
                    ConformanceSupport.NotApplicable("InMemory supports request/reply, so startup accepts it.")
                ),
            ["Redis"] = TransportConformanceProfile
                .CreateDisabled("Redis")
                .WithScenario(TransportConformanceScenario.ConnectionDropRecovery, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.PublishDuringOutage, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.RawPayloadConsumption, ConformanceSupport.Supported)
                .WithScenario(
                    TransportConformanceScenario.RoutingAffinityMappingOrRejection,
                    ConformanceSupport.Supported
                )
                .WithRuntimeCapabilities(
                    "Redis",
                    supportsBus: true,
                    supportsQueue: true,
                    supportsIndependentLaneTopology: true
                )
                .WithMalformedEnvelopeBound("Redis Stream entry acknowledged", 1, TimeSpan.FromSeconds(5))
                .WithScenario(TransportConformanceScenario.QueueRoundTrip, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.BusRoundTrip, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.HeaderRoundTrip, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.EmptyBodyDispatch, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.CommitSettlement, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.RejectRedelivery, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.BrokerInterruptionRecovery, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.StaleSettlement, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.HandlerFailureRedelivery, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.BoundedGracefulShutdown, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.BusConsumerIdentityFanOut, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.BusReplicaCompetition, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.QueueOwnership, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.SameNameLaneIsolation, ConformanceSupport.Supported)
                .WithScenario(
                    TransportConformanceScenario.MalformedEnvelopeTerminalSettlement,
                    ConformanceSupport.Supported
                )
                .WithRequestReplyScenarios(ConformanceSupport.Supported)
                .WithScenario(
                    TransportConformanceScenario.RequestReplyStartupRejection,
                    ConformanceSupport.NotApplicable("Redis supports request/reply, so startup accepts it.")
                )
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

        if (profile.MalformedEnvelopeBound is null)
        {
            errors.Add($"{profile.Provider}: a malformed-envelope bound is required.");
        }
        else
        {
            errors.AddRange(
                profile.MalformedEnvelopeBound.GetValidationErrors().Select(error => $"{profile.Provider}: {error}")
            );
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
