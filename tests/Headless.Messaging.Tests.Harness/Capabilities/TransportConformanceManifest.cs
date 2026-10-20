// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Tests.Simulation;

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
    HeaderBuilderFailureRedelivery,
    DeadLetterSingleCopy,
    StandardHeaderRoundTrip,
    HandlerConcurrencyCap,
    PerKeyOrdering,
    BodySizeLimit,
    MessageExpiry,
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
    /// <summary>The open issue that tracks messaging scenarios no run has proven yet.</summary>
    internal const string GapIssueUrl = "https://github.com/xshaheen/headless-framework/issues/1277";

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
    /// <summary>The open issue that builds the Pulsar reply channel request/reply needs.</summary>
    private const string _PulsarReplyChannelIssueUrl = "https://github.com/xshaheen/headless-framework/issues/1071";

    /// <summary>The open issue that builds the Azure Service Bus reply channel request/reply needs.</summary>
    private const string _AzureServiceBusReplyChannelIssueUrl =
        "https://github.com/xshaheen/headless-framework/issues/1070";

    /// <summary>The open issue that tracks settling a custom headers builder failure the same way on every provider.</summary>
    private const string _HeaderBuilderIssueUrl = "https://github.com/xshaheen/headless-framework/issues/1199";

    /// <summary>The open issue that tracks a delivery budget for the brokers that have none, Kafka and Redis.</summary>
    private const string _DeliveryBudgetIssueUrl = "https://github.com/xshaheen/headless-framework/issues/1288";

    /// <summary>An Azure Service Bus cell that only the protected, credential-gated workflow can prove.</summary>
    private static readonly ConformanceSupport _AzureWorkflowGap = ConformanceSupport.Unsupported(
        "Needs the protected Azure workflow to execute; no run has proven it yet.",
        TransportConformanceProfile.GapIssueUrl
    );

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
                .WithScenario(TransportConformanceScenario.HeaderBuilderFailureRedelivery, ConformanceSupport.Supported)
                .WithScenario(
                    TransportConformanceScenario.DeadLetterSingleCopy,
                    ConformanceSupport.NotApplicable(
                        "JetStream has no dead-letter destination: a dead-letter terminates the delivery, and the server's MSG_TERMINATED advisory is the only record of it."
                    )
                )
                .WithScenario(TransportConformanceScenario.StandardHeaderRoundTrip, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.HandlerConcurrencyCap, ConformanceSupport.Supported)
                .WithScenario(
                    TransportConformanceScenario.PerKeyOrdering,
                    ConformanceSupport.NotApplicable(
                        "NATS rejects a routing affinity key at publish, so it orders no messages by key."
                    )
                )
                .WithScenario(TransportConformanceScenario.BodySizeLimit, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.MessageExpiry, ConformanceSupport.Supported)
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
                .WithScenario(TransportConformanceScenario.HeaderBuilderFailureRedelivery, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.DeadLetterSingleCopy, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.StandardHeaderRoundTrip, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.HandlerConcurrencyCap, ConformanceSupport.Supported)
                .WithScenario(
                    TransportConformanceScenario.PerKeyOrdering,
                    ConformanceSupport.NotApplicable(
                        "RabbitMQ rejects a routing affinity key at publish, so it orders no messages by key."
                    )
                )
                .WithScenario(
                    TransportConformanceScenario.BodySizeLimit,
                    ConformanceSupport.Unsupported(
                        "A body over the client's 64 MiB inbound limit is accepted by the broker, then closes the consumer's connection on delivery, so the consumer stops and the publish is reported as failed although the message was kept.",
                        "https://github.com/xshaheen/headless-framework/issues/1280"
                    )
                )
                .WithScenario(TransportConformanceScenario.MessageExpiry, ConformanceSupport.Supported)
                .EnableRealBrokerLeaf(),
            ["AWS/LocalStack"] = TransportConformanceProfile
                .CreateDisabled("AWS/LocalStack")
                .WithScenario(TransportConformanceScenario.ConnectionDropRecovery, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.PublishDuringOutage, ConformanceSupport.Supported)
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
                .WithScenario(TransportConformanceScenario.ConsumerPauseRecovery, ConformanceSupport.Supported)
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
                .WithScenario(TransportConformanceScenario.HeaderBuilderFailureRedelivery, ConformanceSupport.Supported)
                .WithScenario(
                    TransportConformanceScenario.DeadLetterSingleCopy,
                    ConformanceSupport.NotApplicable(
                        "SQS moves a message to a dead-letter queue only through a queue's redrive policy, so a dead-letter deletes the message as a commit does."
                    )
                )
                .WithScenario(TransportConformanceScenario.StandardHeaderRoundTrip, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.HandlerConcurrencyCap, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.PerKeyOrdering, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.BodySizeLimit, ConformanceSupport.Supported)
                .WithScenario(
                    TransportConformanceScenario.MessageExpiry,
                    ConformanceSupport.NotApplicable(
                        "SQS expires a message only through its queue's retention period, at least 60 seconds, which the provider leaves at the queue default."
                    )
                )
                .EnableRealBrokerLeaf(),
            ["Kafka"] = TransportConformanceProfile
                .CreateDisabled("Kafka")
                .WithScenario(TransportConformanceScenario.ConnectionDropRecovery, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.PublishDuringOutage, ConformanceSupport.Supported)
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
                .WithScenario(
                    TransportConformanceScenario.HeaderBuilderFailureRedelivery,
                    ConformanceSupport.Unsupported(
                        "A header builder failure seeks back to the record, which is redelivered without end: Kafka has no delivery budget and the provider adds none.",
                        _HeaderBuilderIssueUrl
                    )
                )
                .WithScenario(
                    TransportConformanceScenario.DeadLetterSingleCopy,
                    ConformanceSupport.NotApplicable(
                        "Kafka has no dead-letter destination, so a dead-letter commits the record's offset as a commit does."
                    )
                )
                .WithScenario(TransportConformanceScenario.StandardHeaderRoundTrip, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.HandlerConcurrencyCap, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.PerKeyOrdering, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.BodySizeLimit, ConformanceSupport.Supported)
                .WithScenario(
                    TransportConformanceScenario.MessageExpiry,
                    ConformanceSupport.NotApplicable(
                        "Kafka retention deletes whole log segments on a topic-wide schedule; it is not a per-message time-to-live, and the provider sets none."
                    )
                )
                .EnableRealBrokerLeaf(),
            ["Pulsar"] = TransportConformanceProfile
                .CreateDisabled("Pulsar")
                .WithScenario(TransportConformanceScenario.ConnectionDropRecovery, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.PublishDuringOutage, ConformanceSupport.Supported)
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
                        _PulsarReplyChannelIssueUrl
                    )
                )
                .WithScenario(TransportConformanceScenario.RequestReplyStartupRejection, ConformanceSupport.Supported)
                .WithScenario(
                    TransportConformanceScenario.HeaderBuilderFailureRedelivery,
                    ConformanceSupport.Unsupported(
                        "A header builder failure settles the message as malformed and acknowledges it, so the message is lost instead of redelivered.",
                        _HeaderBuilderIssueUrl
                    )
                )
                .WithScenario(
                    TransportConformanceScenario.DeadLetterSingleCopy,
                    ConformanceSupport.NotApplicable(
                        "Pulsar's dead-letter topic is fed only by the client's redelivery budget, so a dead-letter acknowledges the message as a commit does."
                    )
                )
                .WithScenario(TransportConformanceScenario.StandardHeaderRoundTrip, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.HandlerConcurrencyCap, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.PerKeyOrdering, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.BodySizeLimit, ConformanceSupport.Supported)
                .WithScenario(
                    TransportConformanceScenario.MessageExpiry,
                    ConformanceSupport.NotApplicable(
                        "Pulsar's message time-to-live is a namespace policy an operator sets; the provider sets none."
                    )
                )
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
                        _AzureServiceBusReplyChannelIssueUrl
                    )
                )
                .WithScenario(TransportConformanceScenario.RequestReplyStartupRejection, ConformanceSupport.Supported)
                .WithScenario(
                    TransportConformanceScenario.HeaderBuilderFailureRedelivery,
                    ConformanceSupport.Unsupported(
                        "A header builder failure dead-letters the message at once, so it is never redelivered and only a manual replay recovers it.",
                        _HeaderBuilderIssueUrl
                    )
                )
                .WithScenario(TransportConformanceScenario.DeadLetterSingleCopy, _AzureWorkflowGap)
                .WithScenario(TransportConformanceScenario.StandardHeaderRoundTrip, _AzureWorkflowGap)
                .WithScenario(TransportConformanceScenario.HandlerConcurrencyCap, _AzureWorkflowGap)
                .WithScenario(
                    TransportConformanceScenario.PerKeyOrdering,
                    ConformanceSupport.Unsupported(
                        "Ordering by key needs a session-enabled entity, which the conformance leaf does not provision, and the protected Azure workflow to run it.",
                        TransportConformanceProfile.GapIssueUrl
                    )
                )
                .WithScenario(TransportConformanceScenario.BodySizeLimit, _AzureWorkflowGap)
                .WithScenario(TransportConformanceScenario.MessageExpiry, _AzureWorkflowGap)
                .EnableRealBrokerLeaf(),
            ["Azure Service Bus Emulator"] = _CreateAzureServiceBusEmulatorProfile(),
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
                )
                .WithScenario(
                    TransportConformanceScenario.HeaderBuilderFailureRedelivery,
                    ConformanceSupport.NotApplicable(
                        "InMemory transports TransportMessage instances directly, so no custom headers builder runs."
                    )
                )
                .WithScenario(
                    TransportConformanceScenario.PerKeyOrdering,
                    ConformanceSupport.NotApplicable(
                        "InMemory rejects a routing affinity key at publish, so it orders no messages by key."
                    )
                )
                .WithScenario(
                    TransportConformanceScenario.BodySizeLimit,
                    ConformanceSupport.NotApplicable("InMemory keeps bodies in process memory and has no size limit.")
                )
                .WithScenario(
                    TransportConformanceScenario.MessageExpiry,
                    ConformanceSupport.NotApplicable("InMemory has no message time-to-live.")
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
                    ConformanceSupport.NotApplicable("Redis supports request/reply, so startup accepts it.")
                )
                .WithScenario(
                    TransportConformanceScenario.HeaderBuilderFailureRedelivery,
                    ConformanceSupport.Unsupported(
                        "A header builder failure leaves the entry pending, and the claim pass redelivers it without end: Redis has no delivery budget and the provider adds none.",
                        _DeliveryBudgetIssueUrl
                    )
                )
                .WithScenario(
                    TransportConformanceScenario.DeadLetterSingleCopy,
                    ConformanceSupport.NotApplicable(
                        "Redis Streams have no dead-letter destination, so a dead-letter acknowledges the entry as a commit does."
                    )
                )
                .WithScenario(TransportConformanceScenario.StandardHeaderRoundTrip, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.HandlerConcurrencyCap, ConformanceSupport.Supported)
                .WithScenario(
                    TransportConformanceScenario.PerKeyOrdering,
                    ConformanceSupport.NotApplicable(
                        "Redis rejects a routing affinity key at publish, so it orders no messages by key."
                    )
                )
                .WithScenario(
                    TransportConformanceScenario.BodySizeLimit,
                    ConformanceSupport.NotApplicable(
                        "Redis bounds a stream field only by the server's 512 MB proto-max-bulk-len, a server setting far above any body a conformance run can carry."
                    )
                )
                .WithScenario(
                    TransportConformanceScenario.MessageExpiry,
                    ConformanceSupport.NotApplicable("Redis Stream entries have no time-to-live.")
                )
                .EnableRealBrokerLeaf(),
            [SimulatedBroker.ProviderName] = _CreateSimulatorProfile(),
        }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>
    /// Gets the in-process simulator's profile. It is not a real broker: its cells stand in for real-broker evidence in
    /// the fast tier, so each one it supports must also be supported by a real broker leaf.
    /// </summary>
    public static TransportConformanceProfile Simulator => Providers[SimulatedBroker.ProviderName];

    public static IReadOnlyList<string> GetValidationErrors()
    {
        return Providers
            .Values.SelectMany(GetValidationErrors)
            .Concat(GetSimulatorSubsetErrors(Simulator, Providers.Values))
            .ToList();
    }

    /// <summary>
    /// Returns an error for each scenario <paramref name="simulator"/> supports that no real broker leaf among
    /// <paramref name="providers"/> supports. A fast-tier pass the real brokers cannot back would say the transports keep
    /// a guarantee that only the simulator keeps.
    /// </summary>
    public static IReadOnlyList<string> GetSimulatorSubsetErrors(
        TransportConformanceProfile simulator,
        IEnumerable<TransportConformanceProfile> providers
    )
    {
        var realBrokers = providers
            .Where(profile => profile.IsRealBrokerLeafEnabled && !ReferenceEquals(profile, simulator))
            .ToList();

        return simulator
            .Scenarios.Where(cell => cell.Value.Status == ConformanceStatus.Supported)
            .Select(cell => cell.Key)
            .Where(scenario =>
                !realBrokers.Exists(profile =>
                    profile.Scenarios.TryGetValue(scenario, out var support)
                    && support.Status == ConformanceStatus.Supported
                )
            )
            .Order()
            .Select(scenario =>
                $"{simulator.Provider}: {scenario} is Supported on the simulator but on no real broker leaf, so its "
                + "evidence would stand alone."
            )
            .ToList();
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

    private static TransportConformanceProfile _CreateSimulatorProfile()
    {
        TransportConformanceScenario[] supported =
        [
            TransportConformanceScenario.RoutingAffinityMappingOrRejection,
            TransportConformanceScenario.QueueRoundTrip,
            TransportConformanceScenario.BusRoundTrip,
            TransportConformanceScenario.HeaderRoundTrip,
            TransportConformanceScenario.EmptyBodyDispatch,
            TransportConformanceScenario.CommitSettlement,
            TransportConformanceScenario.RejectRedelivery,
            TransportConformanceScenario.ConsumerPauseRecovery,
            TransportConformanceScenario.BrokerInterruptionRecovery,
            TransportConformanceScenario.ConnectionDropRecovery,
            TransportConformanceScenario.PublishDuringOutage,
            TransportConformanceScenario.StaleSettlement,
            TransportConformanceScenario.HandlerFailureRedelivery,
            TransportConformanceScenario.BoundedGracefulShutdown,
            TransportConformanceScenario.BusConsumerIdentityFanOut,
            TransportConformanceScenario.BusReplicaCompetition,
            TransportConformanceScenario.QueueOwnership,
            TransportConformanceScenario.SameNameLaneIsolation,
            TransportConformanceScenario.DeadLetterSingleCopy,
            TransportConformanceScenario.StandardHeaderRoundTrip,
            TransportConformanceScenario.HandlerConcurrencyCap,
        ];

        var profile = TransportConformanceProfile
            .CreateDisabled(SimulatedBroker.ProviderName)
            .WithRuntimeCapabilities(
                SimulatedBroker.ProviderName,
                supportsBus: true,
                supportsQueue: true,
                supportsIndependentLaneTopology: true
            )
            // In-process, so a stray delivery shows within microseconds; a short window keeps the fast tier fast.
            .WithMalformedEnvelopeBound(
                "simulated delivery handed over without a transport envelope",
                1,
                TimeSpan.FromMilliseconds(250)
            );

        foreach (var scenario in supported)
        {
            profile = profile.WithScenario(scenario, ConformanceSupport.Supported);
        }

        return profile
            .WithRequestReplyScenarios(ConformanceSupport.Supported)
            .WithScenario(
                TransportConformanceScenario.RequestReplyStartupRejection,
                ConformanceSupport.NotApplicable("The simulator supports request/reply, so startup accepts it.")
            )
            .WithScenario(
                TransportConformanceScenario.StartupRejectionBeforeSideEffects,
                ConformanceSupport.NotApplicable(
                    "The simulator accepts both lanes and every subscription kind, so startup has nothing to reject."
                )
            )
            .WithScenario(
                TransportConformanceScenario.MalformedEnvelopeTerminalSettlement,
                ConformanceSupport.NotApplicable(
                    "The simulator hands TransportMessage instances over directly and has no transport envelope to parse."
                )
            )
            .WithScenario(
                TransportConformanceScenario.RawPayloadConsumption,
                ConformanceSupport.NotApplicable(
                    "The simulator carries only messages Headless publishes; no foreign producer can reach it."
                )
            )
            .WithScenario(
                TransportConformanceScenario.HeaderBuilderFailureRedelivery,
                ConformanceSupport.NotApplicable(
                    "The simulator hands TransportMessage instances over directly, so no custom headers builder runs."
                )
            )
            .WithScenario(
                TransportConformanceScenario.PerKeyOrdering,
                ConformanceSupport.NotApplicable(
                    "The simulator rejects a routing affinity key at publish, so it orders no messages by key."
                )
            )
            .WithScenario(
                TransportConformanceScenario.BodySizeLimit,
                ConformanceSupport.NotApplicable("The simulator keeps bodies in memory and has no size limit.")
            )
            .WithScenario(
                TransportConformanceScenario.MessageExpiry,
                ConformanceSupport.NotApplicable("The simulator has no message time-to-live.")
            );
    }

    /// <summary>
    /// Gets the Azure Service Bus emulator's profile: the provider with <c>AutoProvision = false</c> on the fixed entities
    /// its <c>Config.json</c> declares. A scenario that creates an entity at run time is not applicable here and stays the
    /// real namespace's to prove. Its leaves skip on any machine that has not accepted the emulator's license, and it has no
    /// routing-affinity evidence, so it does not count as a real broker leaf: no simulator cell or drift verdict rests on it.
    /// </summary>
    private static TransportConformanceProfile _CreateAzureServiceBusEmulatorProfile()
    {
        TransportConformanceScenario[] supported =
        [
            TransportConformanceScenario.QueueRoundTrip,
            TransportConformanceScenario.BusRoundTrip,
            TransportConformanceScenario.HeaderRoundTrip,
            TransportConformanceScenario.CommitSettlement,
            TransportConformanceScenario.RejectRedelivery,
            TransportConformanceScenario.ConsumerPauseRecovery,
            TransportConformanceScenario.BrokerInterruptionRecovery,
            TransportConformanceScenario.StaleSettlement,
            TransportConformanceScenario.HandlerFailureRedelivery,
        ];

        TransportConformanceScenario[] provisioned =
        [
            TransportConformanceScenario.RoutingAffinityMappingOrRejection,
            TransportConformanceScenario.BusConsumerIdentityFanOut,
            TransportConformanceScenario.BusReplicaCompetition,
            TransportConformanceScenario.QueueOwnership,
            TransportConformanceScenario.SameNameLaneIsolation,
        ];

        var profile = TransportConformanceProfile
            .CreateDisabled("Azure Service Bus Emulator")
            .WithRuntimeCapabilities(
                "Azure Service Bus",
                supportsBus: true,
                supportsQueue: true,
                supportsIndependentLaneTopology: true
            )
            .WithMalformedEnvelopeBound("Service Bus dead-letter settlement", 1, TimeSpan.FromSeconds(10));

        foreach (var scenario in supported)
        {
            profile = profile.WithScenario(scenario, ConformanceSupport.Supported);
        }

        foreach (var scenario in provisioned)
        {
            profile = profile.WithScenario(
                scenario,
                ConformanceSupport.NotApplicable(
                    "The scenario creates its queues, topics, or subscriptions at run time, and the emulator profile runs with provisioning off on fixed entities; the real-namespace profile proves it."
                )
            );
        }

        return profile
            .WithRequestReplyScenarios(
                ConformanceSupport.Unsupported(
                    "The Azure Service Bus reply channel is not built yet, so the provider rejects request/reply at startup.",
                    _AzureServiceBusReplyChannelIssueUrl
                )
            )
            .WithScenario(
                TransportConformanceScenario.HeaderBuilderFailureRedelivery,
                ConformanceSupport.Unsupported(
                    "A header builder failure dead-letters the message at once, so it is never redelivered and only a manual replay recovers it.",
                    _HeaderBuilderIssueUrl
                )
            )
            .WithScenario(
                TransportConformanceScenario.BoundedGracefulShutdown,
                ConformanceSupport.Unsupported(
                    "Stopping a consumer while a handler runs waits for the handler, because the processor drains in-flight handlers without the shutdown budget.",
                    "https://github.com/xshaheen/headless-framework/issues/1282"
                )
            );
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
