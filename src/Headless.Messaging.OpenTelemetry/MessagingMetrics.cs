// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Headless.Messaging.OpenTelemetry;

/// <summary>
/// OpenTelemetry metrics for messaging operations.
/// </summary>
internal sealed class MessagingMetrics : IDisposable
{
    public const string MeterName = "Headless.Messaging";
    private const string _Version = "1.0.0";

    private readonly Meter _meter;
    private readonly Counter<long> _messagesPublished;
    private readonly Counter<long> _messagesConsumed;
    private readonly Counter<long> _subscriberInvocations;
    private readonly Counter<long> _publishErrors;
    private readonly Counter<long> _consumeErrors;
    private readonly Counter<long> _subscriberErrors;
    private readonly Histogram<double> _publishDuration;
    private readonly Histogram<double> _consumeDuration;
    private readonly Histogram<double> _subscriberDuration;
    private readonly Histogram<double> _messagePersistenceDuration;
    private readonly Histogram<long> _messageSize;

    public MessagingMetrics()
    {
        _meter = new Meter(MeterName, _Version);

        // Counters for message throughput
        _messagesPublished = _meter.CreateCounter<long>(
            "messaging.publish.messages",
            unit: "{message}",
            description: "Number of messages published"
        );

        _messagesConsumed = _meter.CreateCounter<long>(
            "messaging.consume.messages",
            unit: "{message}",
            description: "Number of messages consumed"
        );

        _subscriberInvocations = _meter.CreateCounter<long>(
            "messaging.subscriber.invocations",
            unit: "{invocation}",
            description: "Number of subscriber invocations"
        );

        // Counters for errors
        _publishErrors = _meter.CreateCounter<long>(
            "messaging.publish.errors",
            unit: "{error}",
            description: "Number of publish errors"
        );

        _consumeErrors = _meter.CreateCounter<long>(
            "messaging.consume.errors",
            unit: "{error}",
            description: "Number of consume errors"
        );

        _subscriberErrors = _meter.CreateCounter<long>(
            "messaging.subscriber.errors",
            unit: "{error}",
            description: "Number of subscriber invocation errors"
        );

        // Histograms for latency
        _publishDuration = _meter.CreateHistogram<double>(
            "messaging.publish.duration",
            unit: "ms",
            description: "Duration of message publish operations"
        );

        _consumeDuration = _meter.CreateHistogram<double>(
            "messaging.consume.duration",
            unit: "ms",
            description: "Duration of message consume operations"
        );

        _subscriberDuration = _meter.CreateHistogram<double>(
            "messaging.subscriber.duration",
            unit: "ms",
            description: "Duration of subscriber invocations"
        );

        _messagePersistenceDuration = _meter.CreateHistogram<double>(
            "messaging.persistence.duration",
            unit: "ms",
            description: "Duration of message persistence operations"
        );

        // Histogram for message size
        _messageSize = _meter.CreateHistogram<long>(
            "messaging.message.size",
            unit: "By",
            description: "Size of message bodies in bytes"
        );
    }

    public void RecordPublish(string operation, string brokerName, long? elapsedMs = null)
    {
        var tags = new TagList { { "messaging.operation", operation }, { "messaging.system", brokerName } };

        _messagesPublished.Add(1, tags);

        if (elapsedMs.HasValue)
        {
            _publishDuration.Record(elapsedMs.Value, tags);
        }
    }

    public void RecordPublishError(string operation, string brokerName, string errorType)
    {
        _publishErrors.Add(
            1,
            new TagList
            {
                { "messaging.operation", operation },
                { "messaging.system", brokerName },
                { "error.type", errorType },
            }
        );
    }

    public void RecordConsume(string operation, string brokerName, string? consumerGroup = null, long? elapsedMs = null)
    {
        var tags = new TagList { { "messaging.operation", operation }, { "messaging.system", brokerName } };

        if (!string.IsNullOrEmpty(consumerGroup))
        {
            tags.Add("messaging.consumer.group", consumerGroup);
        }

        _messagesConsumed.Add(1, tags);

        if (elapsedMs.HasValue)
        {
            _consumeDuration.Record(elapsedMs.Value, tags);
        }
    }

    public void RecordConsumeError(string operation, string brokerName, string errorType, string? consumerGroup = null)
    {
        var tags = new TagList
        {
            { "messaging.operation", operation },
            { "messaging.system", brokerName },
            { "error.type", errorType },
        };

        if (!string.IsNullOrEmpty(consumerGroup))
        {
            tags.Add("messaging.consumer.group", consumerGroup);
        }

        _consumeErrors.Add(1, tags);
    }

    public void RecordSubscriberInvocation(string subscriberName, string operation, long? elapsedMs = null)
    {
        var tags = new TagList { { "messaging.subscriber", subscriberName }, { "messaging.operation", operation } };

        _subscriberInvocations.Add(1, tags);

        if (elapsedMs.HasValue)
        {
            _subscriberDuration.Record(elapsedMs.Value, tags);
        }
    }

    public void RecordSubscriberError(string subscriberName, string operation, string errorType)
    {
        _subscriberErrors.Add(
            1,
            new TagList
            {
                { "messaging.subscriber", subscriberName },
                { "messaging.operation", operation },
                { "error.type", errorType },
            }
        );
    }

    public void RecordPersistence(string operation, long elapsedMs, bool isPublish)
    {
        _messagePersistenceDuration.Record(
            elapsedMs,
            new TagList
            {
                { "messaging.operation", operation },
                { "messaging.persistence.type", isPublish ? "publish" : "consume" },
            }
        );
    }

    public void RecordMessageSize(long sizeBytes, string operation)
    {
        _messageSize.Record(sizeBytes, new TagList { { "messaging.operation", operation } });
    }

    public void Dispose()
    {
        _meter.Dispose();
    }
}
