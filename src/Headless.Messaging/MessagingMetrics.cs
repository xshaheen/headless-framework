// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Diagnostics.Metrics;
using Headless.Messaging.Internal;

namespace Headless.Messaging;

/// <summary>
/// OpenTelemetry metric instruments for messaging operations, registered against
/// <see cref="MessagingDiagnostics.Meter"/>. Instrument names and standard dimensions follow the OpenTelemetry
/// messaging semantic conventions verbatim (<c>messaging.publish.messages</c>, <c>messaging.consume.duration</c>,
/// dims <c>messaging.operation</c> / <c>messaging.system</c> / <c>messaging.consumer.group.name</c> /
/// <c>error.type</c>); framework-owned extras are namespaced <c>headless.messaging.*</c>.
/// </summary>
/// <remarks>
/// Instruments are created directly on the <see cref="Meter"/> (rather than through a source generator) so the
/// hot-path early-out can read each instrument's <c>Enabled</c> flag and short-circuit before building a
/// <see cref="TagList"/> when no listener is attached.
/// </remarks>
internal static class MessagingMetrics
{
    // --- Instrument names -------------------------------------------------------------------------------------

    internal const string PublishMessagesName = "messaging.publish.messages";
    internal const string ConsumeMessagesName = "messaging.consume.messages";
    internal const string SubscriberInvocationsName = "messaging.subscriber.invocations";
    internal const string PublishErrorsName = "messaging.publish.errors";
    internal const string ConsumeErrorsName = "messaging.consume.errors";
    internal const string SubscriberErrorsName = "messaging.subscriber.errors";
    internal const string PublishDurationName = "messaging.publish.duration";
    internal const string ConsumeDurationName = "messaging.consume.duration";
    internal const string SubscriberDurationName = "messaging.subscriber.duration";
    internal const string PersistenceDurationName = "messaging.persistence.duration";
    internal const string MessageSizeName = "messaging.message.size";
    internal const string InboxDuplicatesName = "messaging.inbox.duplicates";
    internal const string InboxAttemptsName = "messaging.inbox.attempts";
    internal const string InboxRecoveriesName = "messaging.inbox.recoveries";
    internal const string InboxTerminalName = "messaging.inbox.terminal";
    internal const string InboxReplaysName = "messaging.inbox.replays";
    internal const string InboxRetentionName = "messaging.inbox.retention";
    internal const string InboxCapabilitiesName = "messaging.inbox.capabilities";
    internal const string OperatorOperationsName = "messaging.operator.operations";
    internal const string ReceiveOutcomesName = "messaging.receive.outcomes";
    internal const string EveryInstanceDeliveriesName = "messaging.every_instance.deliveries";
    internal const string RequestReplyDroppedRepliesName = "messaging.request_reply.dropped_replies";
    internal const string RequestReplyRequestsName = "messaging.request_reply.requests";
    internal const string RequestReplyDurationName = "messaging.request_reply.duration";

    // --- Dimension (tag) names --------------------------------------------------------------------------------

    internal const string TagOperation = "messaging.operation";
    internal const string TagSystem = "messaging.system";

    // The semantic-convention name for the consumer group; its value is the consumer identity, which is what a broker
    // subscription is named after.
    internal const string TagConsumerGroupName = "messaging.consumer.group.name";
    internal const string TagErrorType = "error.type";
    internal const string TagSubscriber = "messaging.subscriber";
    internal const string TagPersistenceType = "messaging.persistence.type";
    internal const string TagReceiveOutcome = "messaging.receive.outcome";
    internal const string TagEveryInstanceOutcome = "messaging.every_instance.outcome";
    internal const string TagRequestReplyDropReason = "messaging.request_reply.drop_reason";
    internal const string TagRequestReplyOutcome = "messaging.request_reply.outcome";

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

    private static readonly Counter<long> _MessagesPublished = MessagingDiagnostics.Meter.CreateCounter<long>(
        PublishMessagesName
    );

    private static readonly Counter<long> _MessagesConsumed = MessagingDiagnostics.Meter.CreateCounter<long>(
        ConsumeMessagesName
    );

    private static readonly Counter<long> _SubscriberInvocations = MessagingDiagnostics.Meter.CreateCounter<long>(
        SubscriberInvocationsName
    );

    private static readonly Counter<long> _PublishErrors = MessagingDiagnostics.Meter.CreateCounter<long>(
        PublishErrorsName
    );

    private static readonly Counter<long> _ConsumeErrors = MessagingDiagnostics.Meter.CreateCounter<long>(
        ConsumeErrorsName
    );

    private static readonly Counter<long> _SubscriberErrors = MessagingDiagnostics.Meter.CreateCounter<long>(
        SubscriberErrorsName
    );

    private static readonly Histogram<double> _PublishDuration = MessagingDiagnostics.Meter.CreateHistogram<double>(
        PublishDurationName,
        unit: "ms"
    );

    private static readonly Histogram<double> _ConsumeDuration = MessagingDiagnostics.Meter.CreateHistogram<double>(
        ConsumeDurationName,
        unit: "ms"
    );

    private static readonly Histogram<double> _SubscriberDuration = MessagingDiagnostics.Meter.CreateHistogram<double>(
        SubscriberDurationName,
        unit: "ms"
    );

    private static readonly Histogram<double> _PersistenceDuration = MessagingDiagnostics.Meter.CreateHistogram<double>(
        PersistenceDurationName,
        unit: "ms"
    );

    private static readonly Histogram<long> _MessageSize = MessagingDiagnostics.Meter.CreateHistogram<long>(
        MessageSizeName,
        unit: "By"
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

    private static readonly Histogram<double> _RequestReplyDuration =
        MessagingDiagnostics.Meter.CreateHistogram<double>(RequestReplyDurationName, unit: "ms");

    /// <summary>Whether any messaging instrument currently has a subscribed listener.</summary>
    internal static bool AnyEnabled =>
        _MessagesPublished.Enabled
        || _MessagesConsumed.Enabled
        || _SubscriberInvocations.Enabled
        || _PublishErrors.Enabled
        || _ConsumeErrors.Enabled
        || _SubscriberErrors.Enabled
        || _PublishDuration.Enabled
        || _ConsumeDuration.Enabled
        || _SubscriberDuration.Enabled
        || _PersistenceDuration.Enabled
        || _MessageSize.Enabled
        || _InboxDuplicates.Enabled
        || _InboxAttempts.Enabled
        || _InboxRecoveries.Enabled
        || _InboxTerminal.Enabled
        || _InboxReplays.Enabled
        || _InboxRetention.Enabled
        || _InboxCapabilities.Enabled
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
            { "messaging.target.kind", "ScheduledDelivery" },
            { TagOperation, operationType.ToString("G") },
            { MessagingTags.Lane, LaneTagEnricher.ToTagValues(lane).Lane },
            { MessagingTags.InboxOutcome, outcome.ToString("G") },
            { MessagingTags.InboxProvider, provider },
        };

        _OperatorOperations.Add(1, tags);
    }

    // --- Record helpers ---------------------------------------------------------------------------------------

    internal static void RecordPublish(
        string operation,
        string brokerName,
        MessageLane lane,
        in DeliveryMetadataValues delivery,
        long? elapsedMs = null
    )
    {
        var tags = _CreateDeliveryTags(operation, brokerName, lane, delivery);

        if (_MessagesPublished.Enabled)
        {
            _MessagesPublished.Add(1, tags);
        }

        if (elapsedMs.HasValue && _PublishDuration.Enabled)
        {
            _PublishDuration.Record(elapsedMs.Value, tags);
        }
    }

    internal static void RecordPublishError(
        string operation,
        string brokerName,
        string errorType,
        MessageLane lane,
        in DeliveryMetadataValues delivery
    )
    {
        if (!_PublishErrors.Enabled)
        {
            return;
        }

        var tags = _CreateDeliveryTags(operation, brokerName, lane, delivery);
        tags.Add(TagErrorType, errorType);
        _PublishErrors.Add(1, tags);
    }

    internal static void RecordConsume(
        string operation,
        string brokerName,
        string? consumerIdentity = null,
        long? elapsedMs = null
    )
    {
        var identity = consumerIdentity ?? "";

        if (_MessagesConsumed.Enabled)
        {
            _MessagesConsumed.Add(
                1,
                new TagList
                {
                    { TagOperation, operation },
                    { TagSystem, brokerName },
                    { TagConsumerGroupName, identity },
                }
            );
        }

        if (elapsedMs.HasValue && _ConsumeDuration.Enabled)
        {
            _ConsumeDuration.Record(
                elapsedMs.Value,
                new TagList
                {
                    { TagOperation, operation },
                    { TagSystem, brokerName },
                    { TagConsumerGroupName, identity },
                }
            );
        }
    }

    internal static void RecordConsumeError(
        string operation,
        string brokerName,
        string errorType,
        string? consumerIdentity = null
    )
    {
        if (!_ConsumeErrors.Enabled)
        {
            return;
        }

        _ConsumeErrors.Add(
            1,
            new TagList
            {
                { TagOperation, operation },
                { TagSystem, brokerName },
                { TagErrorType, errorType },
                { TagConsumerGroupName, consumerIdentity ?? "" },
            }
        );
    }

    internal static void RecordSubscriberInvocation(string subscriberName, string operation, long? elapsedMs = null)
    {
        if (_SubscriberInvocations.Enabled)
        {
            _SubscriberInvocations.Add(
                1,
                new TagList { { TagSubscriber, subscriberName }, { TagOperation, operation } }
            );
        }

        if (elapsedMs.HasValue && _SubscriberDuration.Enabled)
        {
            _SubscriberDuration.Record(
                elapsedMs.Value,
                new TagList { { TagSubscriber, subscriberName }, { TagOperation, operation } }
            );
        }
    }

    internal static void RecordSubscriberError(string subscriberName, string operation, string errorType)
    {
        if (!_SubscriberErrors.Enabled)
        {
            return;
        }

        _SubscriberErrors.Add(
            1,
            new TagList
            {
                { TagSubscriber, subscriberName },
                { TagOperation, operation },
                { TagErrorType, errorType },
            }
        );
    }

    internal static void RecordPersistence(
        string operation,
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
            { TagOperation, operation },
            { TagPersistenceType, isPublish ? "publish" : "consume" },
        };
        if (lane is { } definedLane)
        {
            tags.Add(MessagingTags.Lane, LaneTagEnricher.ToTagValues(definedLane).Lane);
        }

        _AddDeliveryTags(ref tags, delivery);
        _PersistenceDuration.Record(elapsedMs, tags);
    }

    internal static void RecordMessageSize(long sizeBytes, string operation)
    {
        if (!_MessageSize.Enabled)
        {
            return;
        }

        _MessageSize.Record(sizeBytes, new TagList { { TagOperation, operation } });
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
    internal static void RecordRequest(string outcome, double elapsedMs)
    {
        var tag = new KeyValuePair<string, object?>(TagRequestReplyOutcome, outcome);

        if (_RequestReplyRequests.Enabled)
        {
            _RequestReplyRequests.Add(1, tag);
        }

        if (_RequestReplyDuration.Enabled)
        {
            _RequestReplyDuration.Record(elapsedMs, tag);
        }
    }

    private static TagList _CreateDeliveryTags(
        string operation,
        string brokerName,
        MessageLane lane,
        in DeliveryMetadataValues delivery
    )
    {
        var tags = new TagList
        {
            { TagOperation, operation },
            { TagSystem, brokerName },
            { MessagingTags.Lane, LaneTagEnricher.ToTagValues(lane).Lane },
        };
        _AddDeliveryTags(ref tags, delivery);
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
