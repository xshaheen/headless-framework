// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Diagnostics.Metrics;

namespace Headless.Messaging.OpenTelemetry;

/// <summary>
/// OpenTelemetry metrics for messaging operations.
/// </summary>
internal sealed partial class MessagingMetrics : IDisposable
{
    public const string MeterName = "Headless.Messaging";
    private const string _Version = "1.0.0";

    private readonly Meter _meter;
    private readonly PublishMessageCounter _messagesPublished;
    private readonly ConsumeMessageCounter _messagesConsumed;
    private readonly SubscriberInvocationCounter _subscriberInvocations;
    private readonly PublishErrorCounter _publishErrors;
    private readonly ConsumeErrorCounter _consumeErrors;
    private readonly SubscriberErrorCounter _subscriberErrors;
    private readonly PublishDurationHistogram _publishDuration;
    private readonly ConsumeDurationHistogram _consumeDuration;
    private readonly SubscriberDurationHistogram _subscriberDuration;
    private readonly PersistenceDurationHistogram _messagePersistenceDuration;
    private readonly MessageSizeHistogram _messageSize;
    private readonly ScheduledJobExecutionCounter _scheduledJobExecutions;
    private readonly ScheduledJobDurationHistogram _scheduledJobDuration;
    private readonly ScheduledJobErrorCounter _scheduledJobErrors;

    public MessagingMetrics()
    {
        _meter = new Meter(MeterName, _Version);

        _messagesPublished = Instruments.CreatePublishMessageCounter(_meter);
        _messagesConsumed = Instruments.CreateConsumeMessageCounter(_meter);
        _subscriberInvocations = Instruments.CreateSubscriberInvocationCounter(_meter);
        _publishErrors = Instruments.CreatePublishErrorCounter(_meter);
        _consumeErrors = Instruments.CreateConsumeErrorCounter(_meter);
        _subscriberErrors = Instruments.CreateSubscriberErrorCounter(_meter);
        _publishDuration = Instruments.CreatePublishDurationHistogram(_meter);
        _consumeDuration = Instruments.CreateConsumeDurationHistogram(_meter);
        _subscriberDuration = Instruments.CreateSubscriberDurationHistogram(_meter);
        _messagePersistenceDuration = Instruments.CreatePersistenceDurationHistogram(_meter);
        _messageSize = Instruments.CreateMessageSizeHistogram(_meter);
        _scheduledJobExecutions = Instruments.CreateScheduledJobExecutionCounter(_meter);
        _scheduledJobDuration = Instruments.CreateScheduledJobDurationHistogram(_meter);
        _scheduledJobErrors = Instruments.CreateScheduledJobErrorCounter(_meter);
    }

    public void RecordPublish(string operation, string brokerName, long? elapsedMs = null)
    {
        _messagesPublished.Add(1, operation, brokerName);

        if (elapsedMs.HasValue)
        {
            _publishDuration.Record(elapsedMs.Value, operation, brokerName);
        }
    }

    public void RecordPublishError(string operation, string brokerName, string errorType)
    {
        _publishErrors.Add(1, operation, brokerName, errorType);
    }

    public void RecordConsume(string operation, string brokerName, string? consumerGroup = null, long? elapsedMs = null)
    {
        _messagesConsumed.Add(1, operation, brokerName, consumerGroup ?? "");

        if (elapsedMs.HasValue)
        {
            _consumeDuration.Record(elapsedMs.Value, operation, brokerName, consumerGroup ?? "");
        }
    }

    public void RecordConsumeError(string operation, string brokerName, string errorType, string? consumerGroup = null)
    {
        _consumeErrors.Add(1, operation, brokerName, errorType, consumerGroup ?? "");
    }

    public void RecordSubscriberInvocation(string subscriberName, string operation, long? elapsedMs = null)
    {
        _subscriberInvocations.Add(1, subscriberName, operation);

        if (elapsedMs.HasValue)
        {
            _subscriberDuration.Record(elapsedMs.Value, subscriberName, operation);
        }
    }

    public void RecordSubscriberError(string subscriberName, string operation, string errorType)
    {
        _subscriberErrors.Add(1, subscriberName, operation, errorType);
    }

    public void RecordPersistence(string operation, long elapsedMs, bool isPublish)
    {
        _messagePersistenceDuration.Record(elapsedMs, operation, isPublish ? "publish" : "consume");
    }

    public void RecordMessageSize(long sizeBytes, string operation)
    {
        _messageSize.Record(sizeBytes, operation);
    }

    public void RecordScheduledJobExecution(string jobName, long? elapsedMs = null)
    {
        _scheduledJobExecutions.Add(1, jobName);

        if (elapsedMs.HasValue)
        {
            _scheduledJobDuration.Record(elapsedMs.Value, jobName);
        }
    }

    public void RecordScheduledJobError(string jobName, string errorType)
    {
        _scheduledJobErrors.Add(1, jobName, errorType);
    }

    public void Dispose()
    {
        _meter.Dispose();
    }

    [SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes")]
    private static partial class Instruments
    {
        [Counter<long>("messaging.operation", "messaging.system", Name = "messaging.publish.messages")]
        internal static partial PublishMessageCounter CreatePublishMessageCounter(Meter meter);

        [Counter<long>(
            "messaging.operation",
            "messaging.system",
            "messaging.consumer.group",
            Name = "messaging.consume.messages"
        )]
        internal static partial ConsumeMessageCounter CreateConsumeMessageCounter(Meter meter);

        [Counter<long>("messaging.subscriber", "messaging.operation", Name = "messaging.subscriber.invocations")]
        internal static partial SubscriberInvocationCounter CreateSubscriberInvocationCounter(Meter meter);

        [Counter<long>("messaging.operation", "messaging.system", "error.type", Name = "messaging.publish.errors")]
        internal static partial PublishErrorCounter CreatePublishErrorCounter(Meter meter);

        [Counter<long>(
            "messaging.operation",
            "messaging.system",
            "error.type",
            "messaging.consumer.group",
            Name = "messaging.consume.errors"
        )]
        internal static partial ConsumeErrorCounter CreateConsumeErrorCounter(Meter meter);

        [Counter<long>(
            "messaging.subscriber",
            "messaging.operation",
            "error.type",
            Name = "messaging.subscriber.errors"
        )]
        internal static partial SubscriberErrorCounter CreateSubscriberErrorCounter(Meter meter);

        [Histogram<double>("messaging.operation", "messaging.system", Name = "messaging.publish.duration")]
        internal static partial PublishDurationHistogram CreatePublishDurationHistogram(Meter meter);

        [Histogram<double>(
            "messaging.operation",
            "messaging.system",
            "messaging.consumer.group",
            Name = "messaging.consume.duration"
        )]
        internal static partial ConsumeDurationHistogram CreateConsumeDurationHistogram(Meter meter);

        [Histogram<double>("messaging.subscriber", "messaging.operation", Name = "messaging.subscriber.duration")]
        internal static partial SubscriberDurationHistogram CreateSubscriberDurationHistogram(Meter meter);

        [Histogram<double>(
            "messaging.operation",
            "messaging.persistence.type",
            Name = "messaging.persistence.duration"
        )]
        internal static partial PersistenceDurationHistogram CreatePersistenceDurationHistogram(Meter meter);

        [Histogram<long>("messaging.operation", Name = "messaging.message.size")]
        internal static partial MessageSizeHistogram CreateMessageSizeHistogram(Meter meter);

        [Counter<long>("messaging.job.name", Name = "messaging.scheduled_job.executions")]
        internal static partial ScheduledJobExecutionCounter CreateScheduledJobExecutionCounter(Meter meter);

        [Histogram<double>("messaging.job.name", Name = "messaging.scheduled_job.duration")]
        internal static partial ScheduledJobDurationHistogram CreateScheduledJobDurationHistogram(Meter meter);

        [Counter<long>("messaging.job.name", "error.type", Name = "messaging.scheduled_job.errors")]
        internal static partial ScheduledJobErrorCounter CreateScheduledJobErrorCounter(Meter meter);
    }
}
