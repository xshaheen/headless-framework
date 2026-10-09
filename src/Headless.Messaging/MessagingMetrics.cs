// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Diagnostics.Metrics;
using Headless.Messaging.Internal;

namespace Headless.Messaging;

/// <summary>
/// OpenTelemetry metric instruments for messaging operations, registered against
/// <see cref="MessagingDiagnostics.Meter"/>. The client and processing instruments are the OpenTelemetry messaging
/// semantic-convention metrics (<c>messaging.client.sent.messages</c>, <c>messaging.client.consumed.messages</c>,
/// <c>messaging.client.operation.duration</c>, <c>messaging.process.duration</c>) with their standard attributes; a
/// failed operation is recorded on the same instrument with <c>error.type</c>. Instruments the conventions do not
/// define are namespaced <c>headless.messaging.*</c>, so the <c>messaging.*</c> namespace only ever carries
/// convention names.
/// </summary>
/// <remarks>
/// Instruments are created directly on the <see cref="Meter"/> (rather than through a source generator) so the
/// hot-path early-out can read each instrument's <c>Enabled</c> flag and short-circuit before building a
/// <see cref="TagList"/> when no listener is attached.
/// </remarks>
internal static class MessagingMetrics
{
    // --- Instrument names -------------------------------------------------------------------------------------

    internal const string ClientSentMessagesName = "messaging.client.sent.messages";
    internal const string ClientConsumedMessagesName = "messaging.client.consumed.messages";
    internal const string ClientOperationDurationName = "messaging.client.operation.duration";
    internal const string ProcessDurationName = "messaging.process.duration";
    internal const string PersistenceDurationName = "headless.messaging.persistence.duration";
    internal const string MessageBodySizeName = "headless.messaging.message.body.size";
    internal const string InboxDuplicatesName = "headless.messaging.inbox.duplicates";
    internal const string InboxAttemptsName = "headless.messaging.inbox.attempts";
    internal const string InboxRecoveriesName = "headless.messaging.inbox.recoveries";
    internal const string InboxTerminalName = "headless.messaging.inbox.terminal";
    internal const string InboxReplaysName = "headless.messaging.inbox.replays";
    internal const string InboxRetentionName = "headless.messaging.inbox.retention";
    internal const string InboxCapabilitiesName = "headless.messaging.inbox.capabilities";
    internal const string OperatorOperationsName = "headless.messaging.operator.operations";
    internal const string ReceiveOutcomesName = "headless.messaging.receive.outcomes";
    internal const string EveryInstanceDeliveriesName = "headless.messaging.every_instance.deliveries";
    internal const string RequestReplyDroppedRepliesName = "headless.messaging.request_reply.dropped_replies";
    internal const string RequestReplyRequestsName = "headless.messaging.request_reply.requests";
    internal const string RequestReplyDurationName = "headless.messaging.request_reply.duration";

    // --- Attribute (tag) names --------------------------------------------------------------------------------

    internal const string TagOperationName = "messaging.operation.name";
    internal const string TagOperationType = "messaging.operation.type";
    internal const string TagSystem = "messaging.system";
    internal const string TagDestinationName = "messaging.destination.name";

    // The semantic-convention name for the consumer group; its value is the consumer identity, which is what a broker
    // subscription is named after.
    internal const string TagConsumerGroupName = "messaging.consumer.group.name";
    internal const string TagErrorType = "error.type";
    internal const string TagServerAddress = "server.address";
    internal const string TagServerPort = "server.port";
    internal const string TagSubscriber = "headless.messaging.subscriber";
    internal const string TagPersistenceType = "headless.messaging.persistence.type";
    internal const string TagReceiveOutcome = "headless.messaging.receive.outcome";
    internal const string TagEveryInstanceOutcome = "headless.messaging.every_instance.outcome";
    internal const string TagRequestReplyDropReason = "headless.messaging.request_reply.drop_reason";
    internal const string TagRequestReplyOutcome = "headless.messaging.request_reply.outcome";
    internal const string TagOperatorOperation = "headless.messaging.operator.operation";
    internal const string TagOperatorTargetKind = "headless.messaging.operator.target_kind";

    // --- Operation names and types ----------------------------------------------------------------------------

    // The framework publishes through one send call per message, receives one delivery per transport callback, and
    // processes it in one handler invocation, so each phase maps to exactly one convention operation type. The names
    // are the framework's own verbs: the conventions leave messaging.operation.name to the system.
    internal const string OperationNamePublish = "publish";
    internal const string OperationNameReceive = "receive";
    internal const string OperationNameProcess = "process";
    internal const string OperationTypeSend = "send";
    internal const string OperationTypeReceive = "receive";
    internal const string OperationTypeProcess = "process";

    // A send that may or may not have reached the broker: a timeout or shutdown raced it, or the broker accepted it
    // and recording that failed. Not an exception type, so it gets a convention-style snake_case value.
    internal const string ErrorTypeAmbiguousDelivery = "ambiguous_delivery";

    // --- Receive outcomes ------------------------------------------------------------------------------------

    internal const string ReceiveOutcomeAccepted = "accepted";
    internal const string ReceiveOutcomeRejected = "rejected";
    internal const string ReceiveOutcomeCancelled = "cancelled";

    // A receive middleware skipped the message, so it was settled without running the consumer.
    internal const string ReceiveOutcomeSkipped = "skipped";

    // A request whose caller had already stopped waiting, settled without running the consumer.
    internal const string ReceiveOutcomeExpired = "expired";

    // A request that reached a consumer which cannot answer it, settled without running the consumer and answered with
    // a no_responder fault. Kept apart from a middleware skip so an operator can tell a deployment gap from a policy.
    internal const string ReceiveOutcomeNoResponder = "no_responder";

    // --- Request/reply drop reasons ---------------------------------------------------------------------------

    // A request named a reply destination outside the reserved reply namespace, so the responder wrote nothing.
    internal const string DropReasonInvalidReplyAddress = "invalid_reply_address";

    // The call had already ended without a reply (timed out, canceled, or aborted) when the reply arrived.
    internal const string DropReasonLate = "late";

    // No call in this process ever waited on the reply's request id, or the reply named none.
    internal const string DropReasonUnknown = "unknown";

    // The call had already been completed by an earlier reply.
    internal const string DropReasonDuplicate = "duplicate";

    // The reply's tenant differs from the tenant the request was sent under, so it cannot answer that call.
    internal const string DropReasonTenantMismatch = "tenant_mismatch";

    // --- Request/reply call outcomes --------------------------------------------------------------------------

    internal const string RequestOutcomeReplied = "replied";
    internal const string RequestOutcomeFaulted = "faulted";
    internal const string RequestOutcomeContractMismatch = "contract_mismatch";
    internal const string RequestOutcomeTimedOut = "timed_out";
    internal const string RequestOutcomeCanceled = "canceled";
    internal const string RequestOutcomeAborted = "aborted";
    internal const string RequestOutcomeNotSent = "not_sent";

    // The send failed or the reply could not be read: the call ended with an exception outside the request/reply set.
    internal const string RequestOutcomeFailed = "failed";

    // --- Instruments ------------------------------------------------------------------------------------------

    // The bucket boundaries the messaging conventions advise for their duration histograms, in seconds. The
    // framework's own durations use them too, so every messaging latency shares one resolution.
    internal static readonly IReadOnlyList<double> DurationBucketBoundaries =
    [
        0.005,
        0.01,
        0.025,
        0.05,
        0.075,
        0.1,
        0.25,
        0.5,
        0.75,
        1,
        2.5,
        5,
        7.5,
        10,
    ];

    private static readonly Counter<long> _ClientSentMessages = MessagingDiagnostics.Meter.CreateCounter<long>(
        ClientSentMessagesName,
        unit: "{message}",
        description: "Number of messages producers attempted to send to the broker."
    );

    private static readonly Counter<long> _ClientConsumedMessages = MessagingDiagnostics.Meter.CreateCounter<long>(
        ClientConsumedMessagesName,
        unit: "{message}",
        description: "Number of messages delivered to consumers by the broker."
    );

    private static readonly Histogram<double> _ClientOperationDuration = _CreateDurationHistogram(
        ClientOperationDurationName,
        "Duration of messaging operations initiated by a producer or consumer client."
    );

    private static readonly Histogram<double> _ProcessDuration = _CreateDurationHistogram(
        ProcessDurationName,
        "Duration of processing operations."
    );

    private static readonly Histogram<double> _PersistenceDuration = _CreateDurationHistogram(
        PersistenceDurationName,
        "Duration of writing a message to the outbox or inbox store."
    );

    private static readonly Histogram<long> _MessageBodySize = MessagingDiagnostics.Meter.CreateHistogram<long>(
        MessageBodySizeName,
        unit: "By",
        description: "Size of published message bodies."
    );

    private static readonly Counter<long> _InboxDuplicates = MessagingDiagnostics.Meter.CreateCounter<long>(
        InboxDuplicatesName
    );

    private static readonly Counter<long> _InboxAttempts = MessagingDiagnostics.Meter.CreateCounter<long>(
        InboxAttemptsName
    );

    private static readonly Counter<long> _InboxRecoveries = MessagingDiagnostics.Meter.CreateCounter<long>(
        InboxRecoveriesName
    );

    private static readonly Counter<long> _InboxTerminal = MessagingDiagnostics.Meter.CreateCounter<long>(
        InboxTerminalName
    );

    private static readonly Counter<long> _InboxReplays = MessagingDiagnostics.Meter.CreateCounter<long>(
        InboxReplaysName
    );

    private static readonly Counter<long> _InboxRetention = MessagingDiagnostics.Meter.CreateCounter<long>(
        InboxRetentionName
    );

    private static readonly Counter<long> _InboxCapabilities = MessagingDiagnostics.Meter.CreateCounter<long>(
        InboxCapabilitiesName
    );

    private static readonly Counter<long> _OperatorOperations = MessagingDiagnostics.Meter.CreateCounter<long>(
        OperatorOperationsName
    );

    private static readonly Counter<long> _ReceiveOutcomes = MessagingDiagnostics.Meter.CreateCounter<long>(
        ReceiveOutcomesName
    );

    private static readonly Counter<long> _EveryInstanceDeliveries = MessagingDiagnostics.Meter.CreateCounter<long>(
        EveryInstanceDeliveriesName
    );

    private static readonly Counter<long> _RequestReplyDroppedReplies = MessagingDiagnostics.Meter.CreateCounter<long>(
        RequestReplyDroppedRepliesName
    );

    private static readonly Counter<long> _RequestReplyRequests = MessagingDiagnostics.Meter.CreateCounter<long>(
        RequestReplyRequestsName
    );

    private static readonly Histogram<double> _RequestReplyDuration = _CreateDurationHistogram(
        RequestReplyDurationName,
        "Duration of request calls, from send to reply or failure."
    );

    /// <summary>Whether any messaging instrument currently has a subscribed listener.</summary>
    internal static bool AnyEnabled =>
        _ClientSentMessages.Enabled
        || _ClientConsumedMessages.Enabled
        || _ClientOperationDuration.Enabled
        || _ProcessDuration.Enabled
        || _PersistenceDuration.Enabled
        || _MessageBodySize.Enabled
        || _InboxDuplicates.Enabled
        || _InboxAttempts.Enabled
        || _InboxRecoveries.Enabled
        || _InboxTerminal.Enabled
        || _InboxReplays.Enabled
        || _InboxRetention.Enabled
        || _InboxCapabilities.Enabled
        || _OperatorOperations.Enabled
        || _ReceiveOutcomes.Enabled
        || _EveryInstanceDeliveries.Enabled
        || _RequestReplyDroppedReplies.Enabled
        || _RequestReplyRequests.Enabled
        || _RequestReplyDuration.Enabled;

    internal static void RecordInbox(
        InboxMetricKind kind,
        string consumerIdentity,
        MessageLane lane,
        InboxMetricOutcome outcome,
        InboxGuarantee guarantee,
        string provider,
        string? tenantId = null,
        string? tenantTagName = null
    )
    {
        var instrument = kind switch
        {
            InboxMetricKind.Duplicate => _InboxDuplicates,
            InboxMetricKind.Attempt => _InboxAttempts,
            InboxMetricKind.Recovery => _InboxRecoveries,
            InboxMetricKind.Terminal => _InboxTerminal,
            InboxMetricKind.Replay => _InboxReplays,
            InboxMetricKind.Retention => _InboxRetention,
            InboxMetricKind.Capability => _InboxCapabilities,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, message: null),
        };
        if (!instrument.Enabled)
        {
            return;
        }

        var tags = new TagList
        {
            { MessagingTags.InboxConsumer, consumerIdentity },
            { MessagingTags.Lane, LaneTagEnricher.ToTagValues(lane).Lane },
            { MessagingTags.InboxOutcome, outcome.ToString("G") },
            { MessagingTags.InboxGuarantee, guarantee.ToString("G") },
            { MessagingTags.InboxProvider, provider },
        };
        if (tenantTagName is not null && tenantId is not null)
        {
            tags.Add(tenantTagName, tenantId);
        }

        instrument.Add(1, tags);
    }

    internal static void RecordScheduledOperation(
        MessagingOperationType operationType,
        MessageLane lane,
        InboxOperationOutcome outcome,
        string provider
    )
    {
        if (!_OperatorOperations.Enabled)
        {
            return;
        }

        var tags = new TagList
        {
            { TagOperatorTargetKind, "ScheduledDelivery" },
            { TagOperatorOperation, operationType.ToString("G") },
            { MessagingTags.Lane, LaneTagEnricher.ToTagValues(lane).Lane },
            { MessagingTags.InboxOutcome, outcome.ToString("G") },
            { MessagingTags.InboxProvider, provider },
        };

        _OperatorOperations.Add(1, tags);
    }

    // --- Record helpers ---------------------------------------------------------------------------------------

    /// <summary>
    /// Records one send attempt on <c>messaging.client.sent.messages</c> and, when timed, its duration on
    /// <c>messaging.client.operation.duration</c>. A failed send carries <paramref name="errorType"/>.
    /// </summary>
    internal static void RecordPublish(
        string destinationName,
        BrokerAddress broker,
        MessageLane lane,
        in DeliveryMetadataValues delivery,
        long? elapsedMs,
        string? errorType = null
    )
    {
        // Guard before building tags: with no listener attached, tag construction (TagList array, endpoint lookup,
        // DeliveryMetadata read) is pure per-publish waste.
        var recordDuration = elapsedMs.HasValue && _ClientOperationDuration.Enabled;
        if (!_ClientSentMessages.Enabled && !recordDuration)
        {
            return;
        }

        var tags = _CreateClientTags(
            OperationNamePublish,
            OperationTypeSend,
            destinationName,
            broker,
            consumerIdentity: null,
            errorType
        );
        tags.Add(MessagingTags.Lane, LaneTagEnricher.ToTagValues(lane).Lane);
        _AddDeliveryTags(ref tags, delivery);

        if (_ClientSentMessages.Enabled)
        {
            _ClientSentMessages.Add(1, tags);
        }

        if (recordDuration)
        {
            _ClientOperationDuration.Record(_ToSeconds(elapsedMs!.Value), tags);
        }
    }

    /// <summary>
    /// Records one transport delivery on <c>messaging.client.consumed.messages</c> and, when timed, its receive
    /// duration on <c>messaging.client.operation.duration</c>. A failed receive carries <paramref name="errorType"/>.
    /// </summary>
    internal static void RecordReceive(
        string destinationName,
        BrokerAddress broker,
        string? consumerIdentity,
        long? elapsedMs,
        string? errorType = null
    )
    {
        var recordDuration = elapsedMs.HasValue && _ClientOperationDuration.Enabled;
        if (!_ClientConsumedMessages.Enabled && !recordDuration)
        {
            return;
        }

        var tags = _CreateClientTags(
            OperationNameReceive,
            OperationTypeReceive,
            destinationName,
            broker,
            consumerIdentity ?? "",
            errorType
        );

        if (_ClientConsumedMessages.Enabled)
        {
            _ClientConsumedMessages.Add(1, tags);
        }

        if (recordDuration)
        {
            _ClientOperationDuration.Record(_ToSeconds(elapsedMs!.Value), tags);
        }
    }

    /// <summary>
    /// Records one subscriber invocation on <c>messaging.process.duration</c>. A failed invocation carries
    /// <paramref name="errorType"/>; the histogram's count is the invocation count.
    /// </summary>
    internal static void RecordProcess(
        string destinationName,
        string? system,
        string? consumerIdentity,
        string subscriber,
        long elapsedMs,
        string? errorType = null
    )
    {
        if (!_ProcessDuration.Enabled)
        {
            return;
        }

        var tags = new TagList
        {
            { TagOperationName, OperationNameProcess },
            { TagOperationType, OperationTypeProcess },
            { TagDestinationName, destinationName },
            { TagConsumerGroupName, consumerIdentity ?? "" },
            { TagSubscriber, subscriber },
        };

        if (!string.IsNullOrEmpty(system))
        {
            tags.Add(TagSystem, system);
        }

        if (errorType is not null)
        {
            tags.Add(TagErrorType, errorType);
        }

        _ProcessDuration.Record(_ToSeconds(elapsedMs), tags);
    }

    internal static void RecordPersistence(
        string destinationName,
        long elapsedMs,
        bool isPublish,
        MessageLane? lane = null,
        DeliveryMetadataValues delivery = default
    )
    {
        if (!_PersistenceDuration.Enabled)
        {
            return;
        }

        var tags = new TagList
        {
            { TagDestinationName, destinationName },
            { TagPersistenceType, isPublish ? "publish" : "consume" },
        };
        if (lane is { } definedLane)
        {
            tags.Add(MessagingTags.Lane, LaneTagEnricher.ToTagValues(definedLane).Lane);
        }

        _AddDeliveryTags(ref tags, delivery);
        _PersistenceDuration.Record(_ToSeconds(elapsedMs), tags);
    }

    internal static void RecordMessageBodySize(long sizeBytes, string destinationName, string system)
    {
        if (!_MessageBodySize.Enabled)
        {
            return;
        }

        _MessageBodySize.Record(
            sizeBytes,
            new TagList { { TagDestinationName, destinationName }, { TagSystem, system } }
        );
    }

    /// <summary>
    /// Records one receive-stage outcome per delivery: <c>accepted</c> when the delivery continued to
    /// admission, <c>skipped</c> on a middleware-declared skip or a request that reached a consumer that does not
    /// respond, <c>expired</c> on a request that arrived after its caller stopped waiting, <c>rejected</c> on
    /// poison-on-arrival (explicit reject, middleware fault, undeclared outcome, or a Stage A deserialization failure),
    /// and <c>cancelled</c> when the receive was aborted by its bound cancellation token.
    /// </summary>
    internal static void RecordReceiveOutcome(string outcome)
    {
        if (!_ReceiveOutcomes.Enabled)
        {
            return;
        }

        _ReceiveOutcomes.Add(1, new TagList { { TagReceiveOutcome, outcome } });
    }

    /// <summary>
    /// Records one every-instance delivery, which has no inbox row to count it: <c>succeeded</c> when the consumer
    /// returned, <c>failed</c> when it threw (with <c>error.type</c>), <c>dropped</c> when the message never reached it or
    /// faulted outside it (no consumer on the subscription, a receive-stage reject, a core or transport fault, or a NATS
    /// channel overflow with <c>error.type</c> = <c>overflow</c>), and <c>skipped</c> when receive middleware skipped it.
    /// Every outcome commits the message, except where the transport itself discarded it.
    /// </summary>
    internal static void RecordEveryInstanceDelivery(string consumerIdentity, string outcome, string? errorType = null)
    {
        if (!_EveryInstanceDeliveries.Enabled)
        {
            return;
        }

        var tags = new TagList { { TagConsumerGroupName, consumerIdentity }, { TagEveryInstanceOutcome, outcome } };
        if (errorType is not null)
        {
            tags.Add(TagErrorType, errorType);
        }

        _EveryInstanceDeliveries.Add(1, tags);
    }

    /// <summary>
    /// Records a reply that was not delivered, tagged only with the drop reason: request and instance identifiers are
    /// unbounded, so they never become metric tags.
    /// </summary>
    internal static void RecordDroppedReply(string reason)
    {
        if (!_RequestReplyDroppedReplies.Enabled)
        {
            return;
        }

        _RequestReplyDroppedReplies.Add(1, new KeyValuePair<string, object?>(TagRequestReplyDropReason, reason));
    }

    /// <summary>
    /// Records how one request call ended and how long it took, tagged only with the outcome: request, correlation, and
    /// instance identifiers are unbounded, so they never become metric tags.
    /// </summary>
    internal static void RecordRequest(string outcome, TimeSpan elapsed)
    {
        var tag = new KeyValuePair<string, object?>(TagRequestReplyOutcome, outcome);

        if (_RequestReplyRequests.Enabled)
        {
            _RequestReplyRequests.Add(1, tag);
        }

        if (_RequestReplyDuration.Enabled)
        {
            _RequestReplyDuration.Record(elapsed.TotalSeconds, tag);
        }
    }

    private static Histogram<double> _CreateDurationHistogram(string name, string description)
    {
        return MessagingDiagnostics.Meter.CreateHistogram(
            name,
            unit: "s",
            description: description,
            tags: null,
            advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = DurationBucketBoundaries }
        );
    }

    // Emission sites time operations in whole milliseconds; the conventions measure durations in seconds.
    private static double _ToSeconds(long elapsedMs) => elapsedMs / 1000d;

    private static TagList _CreateClientTags(
        string operationName,
        string operationType,
        string destinationName,
        BrokerAddress broker,
        string? consumerIdentity,
        string? errorType
    )
    {
        var tags = new TagList
        {
            { TagOperationName, operationName },
            { TagOperationType, operationType },
            { TagSystem, broker.Name },
            { TagDestinationName, destinationName },
        };

        if (consumerIdentity is not null)
        {
            tags.Add(TagConsumerGroupName, consumerIdentity);
        }

        var server = MessagingServerEndpoint.From(broker);
        if (server.Address is { } address)
        {
            tags.Add(TagServerAddress, address);
        }

        if (server.Port is { } port)
        {
            tags.Add(TagServerPort, port);
        }

        if (errorType is not null)
        {
            tags.Add(TagErrorType, errorType);
        }

        return tags;
    }

    private static void _AddDeliveryTags(ref TagList tags, in DeliveryMetadataValues delivery)
    {
        if (DeliveryModeTagEnricher.ToRequestedTagValue(delivery.RequestedDeliveryMode) is { } requested)
        {
            tags.Add(MessagingTags.RequestedDeliveryMode, requested);
        }

        if (DeliveryModeTagEnricher.ToResolvedTagValue(delivery.ResolvedDeliveryMode) is { } resolved)
        {
            tags.Add(MessagingTags.ResolvedDeliveryMode, resolved);
        }
    }
}

internal enum InboxMetricKind
{
    Duplicate = 0,
    Attempt = 1,
    Recovery = 2,
    Terminal = 3,
    Replay = 4,
    Retention = 5,
    Capability = 6,
}

internal enum InboxMetricOutcome
{
    Winner = 0,
    InFlightDuplicate = 1,
    SucceededDuplicate = 2,
    TerminalFailedDuplicate = 3,
    Reserved = 4,
    Succeeded = 5,
    FailedExhausted = 6,
    Orphaned = 7,
    Routable = 8,
    Held = 9,
    Released = 10,
    Purged = 11,
    Replayed = 12,
    Expired = 13,
}

/// <summary>How inbox measurements treat the tenant dimension, resolved once from the host configuration.</summary>
/// <param name="TenantTagName">The tenant attribute to add to inbox measurements, or <see langword="null"/> to omit it.</param>
internal sealed record InboxMetricPolicy(string? TenantTagName);
