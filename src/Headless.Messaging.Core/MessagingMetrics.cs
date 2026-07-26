// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Diagnostics.Metrics;
using Headless.Messaging.Internal;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Messaging;

/// <summary>
/// OpenTelemetry metric instruments for messaging operations, registered against
/// <see cref="MessagingDiagnostics.Meter"/>. Instrument names and standard dimensions follow the OpenTelemetry
/// messaging semantic conventions verbatim (<c>messaging.publish.messages</c>, <c>messaging.consume.duration</c>,
/// dims <c>messaging.operation</c> / <c>messaging.system</c> / <c>messaging.consumer.group</c> /
/// <c>error.type</c>); see docs/solutions/conventions/opentelemetry-instrumentation-conventions.md.
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

    // --- Dimension (tag) names --------------------------------------------------------------------------------

    internal const string TagOperation = "messaging.operation";
    internal const string TagSystem = "messaging.system";
    internal const string TagConsumerGroup = "messaging.consumer.group";
    internal const string TagErrorType = "error.type";
    internal const string TagSubscriber = "messaging.subscriber";
    internal const string TagPersistenceType = "messaging.persistence.type";

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
        || _MessageSize.Enabled;

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
        string? consumerGroup = null,
        long? elapsedMs = null
    )
    {
        var group = consumerGroup ?? "";

        if (_MessagesConsumed.Enabled)
        {
            _MessagesConsumed.Add(
                1,
                new TagList
                {
                    { TagOperation, operation },
                    { TagSystem, brokerName },
                    { TagConsumerGroup, group },
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
                    { TagConsumerGroup, group },
                }
            );
        }
    }

    internal static void RecordConsumeError(
        string operation,
        string brokerName,
        string errorType,
        string? consumerGroup = null
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
                { TagConsumerGroup, consumerGroup ?? "" },
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
        if (DeliveryModeTagEnricher.ToTagValue(delivery.RequestedDeliveryMode) is { } requested)
        {
            tags.Add(MessagingTags.RequestedDeliveryMode, requested);
        }

        if (DeliveryModeTagEnricher.ToTagValue(delivery.ResolvedDeliveryMode) is { } resolved)
        {
            tags.Add(MessagingTags.ResolvedDeliveryMode, resolved);
        }
    }
}
