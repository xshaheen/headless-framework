// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Messages.Messages;

/// <summary>
/// Defines the standard header names used in CAP messages.
/// These headers carry metadata and system information that controls message routing, tracking, and processing.
/// </summary>
public static class Headers
{
    /// <summary>
    /// Unique identifier for the message.
    /// Can be set explicitly when publishing a message, or automatically assigned by CAP.
    /// This ID is used to track and correlate messages throughout their lifecycle.
    /// Value: "headless-msg-id"
    /// </summary>
    public const string MessageId = "headless-msg-id";

    /// <summary>
    /// The topic or message name that identifies what kind of message this is.
    /// Used for routing to the correct subscribers.
    /// Value: "headless-msg-name"
    /// </summary>
    public const string MessageName = "headless-msg-name";

    /// <summary>
    /// The consumer group that should receive this message.
    /// In Kafka, this maps to the consumer group; in RabbitMQ, it maps to the queue name.
    /// Value: "headless-msg-group"
    /// </summary>
    public const string Group = "headless-msg-group";

    /// <summary>
    /// The .NET type name of the message value/payload.
    /// Used during deserialization to reconstruct the original object type.
    /// Value: "headless-msg-type"
    /// </summary>
    public const string Type = "headless-msg-type";

    /// <summary>
    /// Correlation ID for linking related messages in a message flow or saga pattern.
    /// Allows tracing a chain of messages across different topics and services.
    /// Value: "headless-corr-id"
    /// </summary>
    public const string CorrelationId = "headless-corr-id";

    /// <summary>
    /// Sequence number for ordering correlated messages.
    /// Indicates the position of this message in a correlated sequence.
    /// Value: "headless-corr-seq"
    /// </summary>
    public const string CorrelationSequence = "headless-corr-seq";

    /// <summary>
    /// Name of the subscriber callback handler that should process the response to this message.
    /// Used in request-response patterns where a subscriber needs to send a reply.
    /// Value: "headless-callback-name"
    /// </summary>
    public const string CallbackName = "headless-callback-name";

    /// <summary>
    /// Identifier of the application instance that executed or is executing the message.
    /// Useful in distributed systems to track which instance processed a message.
    /// Value: "headless-exec-instance-id"
    /// </summary>
    public const string ExecutionInstanceId = "headless-exec-instance-id";

    /// <summary>
    /// Timestamp indicating when the message was sent/published, in UTC ISO 8601 format.
    /// Value: "headless-senttime"
    /// </summary>
    public const string SentTime = "headless-senttime";

    /// <summary>
    /// Timestamp indicating when a delayed message should be published, in UTC ISO 8601 format.
    /// This header is only present for messages scheduled for delayed delivery.
    /// Value: "headless-delaytime"
    /// </summary>
    public const string DelayTime = "headless-delaytime";

    /// <summary>
    /// Exception information if the message processing failed.
    /// Contains the exception type name and message formatted as "ExceptionTypeName-->ExceptionMessage".
    /// Value: "headless-exception"
    /// </summary>
    public const string Exception = "headless-exception";

    /// <summary>
    /// W3C Trace Context parent trace ID for distributed tracing and OpenTelemetry integration.
    /// Enables correlation of messages with the broader application trace.
    /// Value: "traceparent"
    /// </summary>
    public const string TraceParent = "traceparent";
}
