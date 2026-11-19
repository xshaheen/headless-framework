// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Tests.Simulation;

namespace Tests.Capabilities;

/// <summary>Broker-observed scenarios tracked for every messaging transport provider.</summary>
[PublicAPI]
public enum TransportConformanceScenario
{
    RoutingAffinityMappingOrHeader,
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
    KeySequencing,
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
    bool SupportsIndependentLaneTopology,
    bool SupportsEveryInstance
)
{
    public static TransportRuntimeCapabilityExpectation Disabled(string provider)
    {
        return new TransportRuntimeCapabilityExpectation(provider, false, false, false, false);
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

        if (actual.SupportsEveryInstance != SupportsEveryInstance)
        {
            errors.Add(
                $"{Provider}: production every-instance support is {actual.SupportsEveryInstance}; manifest expects {SupportsEveryInstance}."
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
    /// <summary>
    /// Starts a profile that declares no scenario. Every scenario must then be declared explicitly, so a scenario added
    /// later fails the manifest's validation on each provider until that provider decides its cell.
    /// </summary>
    public static TransportConformanceProfile CreateDisabled(string provider)
    {
        var scenarios = new Dictionary<TransportConformanceScenario, ConformanceSupport>();

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
        bool supportsIndependentLaneTopology,
        bool supportsEveryInstance
    )
    {
        return this with
        {
            ExpectedRuntimeCapabilities = new TransportRuntimeCapabilityExpectation(
                provider,
                supportsBus,
                supportsQueue,
                supportsIndependentLaneTopology,
                supportsEveryInstance
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
    /// <summary>The open issue that builds the Azure Service Bus reply channel request/reply needs.</summary>
    private const string _AzureServiceBusReplyChannelIssueUrl =
        "https://github.com/xshaheen/headless-framework/issues/1070";

    /// <summary>The open issue that tracks a delivery budget for the brokers that have none, Kafka and Redis.</summary>
    private const string _DeliveryBudgetIssueUrl = "https://github.com/xshaheen/headless-framework/issues/1288";

    /// <summary>The open issue that tracks the emulator dead-lettering two copies of one message.</summary>
    private const string _EmulatorDeadLetterDuplicateIssueUrl =
        "https://github.com/xshaheen/headless-framework/issues/1672";

    /// <summary>The open issue that tracks the real-namespace cells only the protected Azure workflow can prove.</summary>
    private const string _AzureWorkflowIssueUrl = "https://github.com/xshaheen/headless-framework/issues/1760";

    /// <summary>The open issue that tracks the emulator cells no emulator leaf runs yet.</summary>
    private const string _EmulatorGapIssueUrl = "https://github.com/xshaheen/headless-framework/issues/1761";

    /// <summary>An Azure Service Bus cell that only the protected, credential-gated workflow can prove.</summary>
    private static readonly ConformanceSupport _AzureWorkflowGap = ConformanceSupport.Unsupported(
        "Needs the protected Azure workflow to execute; no run has proven it yet.",
        _AzureWorkflowIssueUrl
    );

    /// <summary>An emulator cell the emulator can run once its leaf declares the entity or option the scenario needs.</summary>
    private static readonly ConformanceSupport _EmulatorGap = ConformanceSupport.Unsupported(
        "The emulator leaf does not run this scenario yet: it needs an entity in Emulator/Config.json, a session option the fixture does not take, or an emulator copy of the real-namespace leaf test.",
        _EmulatorGapIssueUrl
    );

    private static ConformanceSupport _NetworkFaultGap(string provider, string issueUrl) =>
        ConformanceSupport.Unsupported($"{provider} has no network-fault leaf yet.", issueUrl);

    private static readonly TransportConformanceScenario[] _MandatoryBaselineScenarios =
    [
        TransportConformanceScenario.RoutingAffinityMappingOrHeader,
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
                .WithScenario(TransportConformanceScenario.RoutingAffinityMappingOrHeader, ConformanceSupport.Supported)
                .WithRuntimeCapabilities(
                    "NATS JetStream",
                    supportsBus: true,
                    supportsQueue: true,
                    supportsIndependentLaneTopology: true,
                    supportsEveryInstance: true
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
                .WithScenario(
                    TransportConformanceScenario.StartupRejectionBeforeSideEffects,
                    ConformanceSupport.Supported
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
                        "NATS carries a routing affinity key as a header and does not order its deliveries by it; KeySequencing covers the order a sequential consumer keeps."
                    )
                )
                .WithScenario(TransportConformanceScenario.KeySequencing, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.BodySizeLimit, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.MessageExpiry, ConformanceSupport.Supported)
                .EnableRealBrokerLeaf(),
            ["RabbitMQ"] = TransportConformanceProfile
                .CreateDisabled("RabbitMQ")
                .WithScenario(TransportConformanceScenario.ConnectionDropRecovery, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.PublishDuringOutage, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.RawPayloadConsumption, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.RoutingAffinityMappingOrHeader, ConformanceSupport.Supported)
                .WithRuntimeCapabilities(
                    "RabbitMQ",
                    supportsBus: true,
                    supportsQueue: true,
                    supportsIndependentLaneTopology: true,
                    supportsEveryInstance: true
                )
                .WithMalformedEnvelopeBound("basic.reject with requeue disabled", 1, TimeSpan.FromSeconds(3))
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
                    ConformanceSupport.NotApplicable("RabbitMQ supports request/reply, so startup accepts it.")
                )
                .WithScenario(
                    TransportConformanceScenario.StartupRejectionBeforeSideEffects,
                    ConformanceSupport.Supported
                )
                .WithScenario(TransportConformanceScenario.HeaderBuilderFailureRedelivery, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.DeadLetterSingleCopy, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.StandardHeaderRoundTrip, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.HandlerConcurrencyCap, ConformanceSupport.Supported)
                .WithScenario(
                    TransportConformanceScenario.PerKeyOrdering,
                    ConformanceSupport.NotApplicable(
                        "RabbitMQ carries a routing affinity key as a header and does not order its deliveries by it; KeySequencing covers the order a sequential consumer keeps."
                    )
                )
                .WithScenario(TransportConformanceScenario.KeySequencing, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.BodySizeLimit, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.MessageExpiry, ConformanceSupport.Supported)
                .EnableRealBrokerLeaf(),
            ["AWS/LocalStack"] = TransportConformanceProfile
                .CreateDisabled("AWS/LocalStack")
                .WithScenario(TransportConformanceScenario.ConnectionDropRecovery, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.PublishDuringOutage, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.RawPayloadConsumption, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.RoutingAffinityMappingOrHeader, ConformanceSupport.Supported)
                .WithRuntimeCapabilities(
                    "Amazon SQS",
                    supportsBus: true,
                    supportsQueue: true,
                    supportsIndependentLaneTopology: true,
                    supportsEveryInstance: false
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
                .WithScenario(
                    TransportConformanceScenario.StartupRejectionBeforeSideEffects,
                    ConformanceSupport.Supported
                )
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
                .WithScenario(TransportConformanceScenario.KeySequencing, ConformanceSupport.Supported)
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
                .WithScenario(TransportConformanceScenario.RoutingAffinityMappingOrHeader, ConformanceSupport.Supported)
                .WithRuntimeCapabilities(
                    "Kafka",
                    supportsBus: false,
                    supportsQueue: true,
                    supportsIndependentLaneTopology: false,
                    supportsEveryInstance: false
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
                .WithScenario(TransportConformanceScenario.EmptyBodyDispatch, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.CommitSettlement, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.RejectRedelivery, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.ConsumerPauseRecovery, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.BrokerInterruptionRecovery, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.StaleSettlement, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.HandlerFailureRedelivery, ConformanceSupport.Supported)
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
                        _DeliveryBudgetIssueUrl
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
                .WithScenario(TransportConformanceScenario.KeySequencing, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.BodySizeLimit, ConformanceSupport.Supported)
                .WithScenario(
                    TransportConformanceScenario.MessageExpiry,
                    ConformanceSupport.NotApplicable(
                        "Kafka retention deletes whole log segments on a topic-wide schedule; it is not a per-message time-to-live, and the provider sets none."
                    )
                )
                .EnableRealBrokerLeaf(),
            ["Azure Service Bus"] = TransportConformanceProfile
                .CreateDisabled("Azure Service Bus")
                .WithScenario(TransportConformanceScenario.RawPayloadConsumption, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.RoutingAffinityMappingOrHeader, ConformanceSupport.Supported)
                .WithRuntimeCapabilities(
                    "Azure Service Bus",
                    supportsBus: true,
                    supportsQueue: true,
                    supportsIndependentLaneTopology: true,
                    supportsEveryInstance: true
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
                    ConformanceSupport.NotApplicable(
                        "A managed Azure Service Bus namespace cannot be restarted by a test; the emulator profile proves the client's recovery from a broker restart."
                    )
                )
                .WithScenario(
                    TransportConformanceScenario.StartupRejectionBeforeSideEffects,
                    ConformanceSupport.Supported
                )
                .WithScenario(TransportConformanceScenario.StaleSettlement, _AzureWorkflowGap)
                .WithScenario(TransportConformanceScenario.HandlerFailureRedelivery, _AzureWorkflowGap)
                .WithScenario(TransportConformanceScenario.EmptyBodyDispatch, _AzureWorkflowGap)
                .WithScenario(TransportConformanceScenario.MalformedEnvelopeTerminalSettlement, _AzureWorkflowGap)
                .WithScenario(TransportConformanceScenario.KeySequencing, _AzureWorkflowGap)
                .WithScenario(
                    TransportConformanceScenario.ConnectionDropRecovery,
                    _NetworkFaultGap("Azure Service Bus", _AzureWorkflowIssueUrl)
                )
                .WithScenario(
                    TransportConformanceScenario.PublishDuringOutage,
                    _NetworkFaultGap("Azure Service Bus", _AzureWorkflowIssueUrl)
                )
                .WithRequestReplyScenarios(
                    ConformanceSupport.Unsupported(
                        "The Azure Service Bus reply channel is not built yet, so the provider rejects request/reply at startup.",
                        _AzureServiceBusReplyChannelIssueUrl
                    )
                )
                .WithScenario(TransportConformanceScenario.RequestReplyStartupRejection, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.HeaderBuilderFailureRedelivery, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.DeadLetterSingleCopy, _AzureWorkflowGap)
                .WithScenario(TransportConformanceScenario.StandardHeaderRoundTrip, _AzureWorkflowGap)
                .WithScenario(TransportConformanceScenario.HandlerConcurrencyCap, _AzureWorkflowGap)
                .WithScenario(
                    TransportConformanceScenario.PerKeyOrdering,
                    ConformanceSupport.Unsupported(
                        "Ordering by key needs a session-enabled entity, which the conformance leaf does not provision, and the protected Azure workflow to run it.",
                        _AzureWorkflowIssueUrl
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
                .WithScenario(TransportConformanceScenario.RoutingAffinityMappingOrHeader, ConformanceSupport.Supported)
                .WithRuntimeCapabilities(
                    "InMemory",
                    supportsBus: true,
                    supportsQueue: true,
                    supportsIndependentLaneTopology: true,
                    supportsEveryInstance: true
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
                        "InMemory carries a routing affinity key as a header and does not order its deliveries by it; KeySequencing covers the order a sequential consumer keeps."
                    )
                )
                .WithScenario(TransportConformanceScenario.KeySequencing, ConformanceSupport.Supported)
                .WithScenario(
                    TransportConformanceScenario.BodySizeLimit,
                    ConformanceSupport.NotApplicable("InMemory keeps bodies in process memory and has no size limit.")
                )
                .WithScenario(
                    TransportConformanceScenario.MessageExpiry,
                    ConformanceSupport.NotApplicable("InMemory has no message time-to-live.")
                )
                .WithScenario(
                    TransportConformanceScenario.ConnectionDropRecovery,
                    ConformanceSupport.NotApplicable(
                        "The in-memory broker lives in the host process, so there is no connection to drop."
                    )
                )
                .WithScenario(
                    TransportConformanceScenario.PublishDuringOutage,
                    ConformanceSupport.NotApplicable(
                        "The in-memory broker lives in the host process, so it cannot be unreachable while the host runs."
                    )
                )
                .WithScenario(TransportConformanceScenario.ConsumerPauseRecovery, ConformanceSupport.Supported)
                .WithScenario(
                    TransportConformanceScenario.BrokerInterruptionRecovery,
                    ConformanceSupport.NotApplicable(
                        "The in-memory broker lives in the host process, so it cannot restart under a host that keeps running."
                    )
                )
                .WithScenario(TransportConformanceScenario.StaleSettlement, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.HandlerFailureRedelivery, ConformanceSupport.Supported)
                .WithScenario(
                    TransportConformanceScenario.DeadLetterSingleCopy,
                    ConformanceSupport.NotApplicable(
                        "InMemory has no dead-letter destination, so a dead-letter settles the delivery as a commit does."
                    )
                )
                .WithScenario(TransportConformanceScenario.StandardHeaderRoundTrip, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.HandlerConcurrencyCap, ConformanceSupport.Supported)
                .WithScenario(
                    TransportConformanceScenario.StartupRejectionBeforeSideEffects,
                    ConformanceSupport.Supported
                ),
            ["Redis"] = TransportConformanceProfile
                .CreateDisabled("Redis")
                .WithScenario(TransportConformanceScenario.ConnectionDropRecovery, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.PublishDuringOutage, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.RawPayloadConsumption, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.RoutingAffinityMappingOrHeader, ConformanceSupport.Supported)
                .WithRuntimeCapabilities(
                    "Redis",
                    supportsBus: true,
                    supportsQueue: true,
                    supportsIndependentLaneTopology: true,
                    supportsEveryInstance: true
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
                    TransportConformanceScenario.StartupRejectionBeforeSideEffects,
                    ConformanceSupport.Supported
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
                        "Redis carries a routing affinity key as a header and does not order its deliveries by it; KeySequencing covers the order a sequential consumer keeps."
                    )
                )
                .WithScenario(TransportConformanceScenario.KeySequencing, ConformanceSupport.Supported)
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

        _ValidateStartupRejection(profile, errors);

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
            TransportConformanceScenario.RoutingAffinityMappingOrHeader,
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
            TransportConformanceScenario.StartupRejectionBeforeSideEffects,
        ];

        var profile = TransportConformanceProfile
            .CreateDisabled(SimulatedBroker.ProviderName)
            .WithRuntimeCapabilities(
                SimulatedBroker.ProviderName,
                supportsBus: true,
                supportsQueue: true,
                supportsIndependentLaneTopology: true,
                supportsEveryInstance: true
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
                    "The simulator carries a routing affinity key as a header and does not order its deliveries by it; KeySequencing covers the order a sequential consumer keeps."
                )
            )
            .WithScenario(TransportConformanceScenario.KeySequencing, ConformanceSupport.Supported)
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
            TransportConformanceScenario.BoundedGracefulShutdown,
            TransportConformanceScenario.KeySequencing,
        ];

        TransportConformanceScenario[] provisioned =
        [
            TransportConformanceScenario.RoutingAffinityMappingOrHeader,
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
                supportsIndependentLaneTopology: true,
                supportsEveryInstance: false
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

        TransportConformanceScenario[] notYetRun =
        [
            TransportConformanceScenario.EmptyBodyDispatch,
            TransportConformanceScenario.StandardHeaderRoundTrip,
            TransportConformanceScenario.HandlerConcurrencyCap,
            TransportConformanceScenario.DeadLetterSingleCopy,
            TransportConformanceScenario.MessageExpiry,
            TransportConformanceScenario.BodySizeLimit,
            TransportConformanceScenario.PerKeyOrdering,
            TransportConformanceScenario.MalformedEnvelopeTerminalSettlement,
            TransportConformanceScenario.RawPayloadConsumption,
        ];

        foreach (var scenario in notYetRun)
        {
            profile = profile.WithScenario(scenario, _EmulatorGap);
        }

        return profile
            .WithScenario(
                TransportConformanceScenario.HeaderBuilderFailureRedelivery,
                ConformanceSupport.Unsupported(
                    "The emulator intermittently dead-letters two copies of a message whose header builder keeps failing; the cause is not yet known.",
                    _EmulatorDeadLetterDuplicateIssueUrl
                )
            )
            // Provisioning off takes every-instance subscriptions away, and request/reply has no reply channel yet; both
            // are refused at bootstrap, before any connection opens.
            .WithScenario(TransportConformanceScenario.StartupRejectionBeforeSideEffects, ConformanceSupport.Supported)
            .WithScenario(TransportConformanceScenario.RequestReplyStartupRejection, ConformanceSupport.Supported)
            .WithScenario(
                TransportConformanceScenario.ConnectionDropRecovery,
                _NetworkFaultGap("Azure Service Bus Emulator", _EmulatorGapIssueUrl)
            )
            .WithScenario(
                TransportConformanceScenario.PublishDuringOutage,
                _NetworkFaultGap("Azure Service Bus Emulator", _EmulatorGapIssueUrl)
            )
            .WithRequestReplyScenarios(
                ConformanceSupport.Unsupported(
                    "The Azure Service Bus reply channel is not built yet, so the provider rejects request/reply at startup.",
                    _AzureServiceBusReplyChannelIssueUrl
                )
            );
    }

    /// <summary>
    /// Every transport rejects something at startup: a route that requires routing affinity on a destination without a
    /// native key mapping, which every provider has, and a lane, shared lane topology, or every-instance subscription
    /// it lacks. So the cell is never not applicable; a provider proves its rejection or quarantines it.
    /// </summary>
    private static void _ValidateStartupRejection(TransportConformanceProfile profile, ICollection<string> errors)
    {
        if (
            profile.Scenarios.TryGetValue(
                TransportConformanceScenario.StartupRejectionBeforeSideEffects,
                out var support
            )
            && support.Status == ConformanceStatus.NotApplicable
        )
        {
            errors.Add(
                $"{profile.Provider}: StartupRejectionBeforeSideEffects cannot be NotApplicable, because every transport rejects a route that requires routing affinity on a destination without a native key mapping."
            );
        }
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
