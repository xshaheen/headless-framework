// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

/// <summary>
/// Defines the standard header names used in messaging.
/// These headers carry metadata and system information that controls message routing, tracking, and processing.
/// </summary>
[PublicAPI]
public static class Headers
{
    /// <summary>
    /// Unique identifier for the message.
    /// Can be set explicitly when publishing a message, or automatically assigned by the framework.
    /// This ID is used to track and correlate messages throughout their lifecycle.
    /// Value: "headless-msg-id"
    /// </summary>
    public const string MessageId = "headless-msg-id";

    /// <summary>
    /// The message name that identifies what kind of message this is.
    /// Used for routing to the correct subscribers.
    /// Value: "headless-msg-name"
    /// </summary>
    public const string MessageName = "headless-msg-name";

    /// <summary>
    /// Schema version of the logical message contract. This is independent from consumer identity and inbox generation.
    /// Value: "headless-contract-version"
    /// </summary>
    public const string ContractVersion = "headless-contract-version";

    /// <summary>Reserved provider-neutral routing key; use MessageOptions.RoutingAffinityKey when publishing.</summary>
    public const string RoutingAffinityKey = "headless-routing-affinity-key";

    /// <summary>
    /// The identity of the consumer that received this message. Stamped on receipt from the consumer the delivery
    /// was routed to, so a value set by the publisher is always replaced.
    /// Value: "headless-msg-consumer-identity"
    /// </summary>
    public const string ConsumerIdentity = "headless-msg-consumer-identity";

    /// <summary>
    /// The .NET type name of the message value/payload.
    /// Used during deserialization to reconstruct the original object type.
    /// Value: "headless-msg-type"
    /// </summary>
    public const string Type = "headless-msg-type";

    /// <summary>
    /// Correlation ID for linking related messages in a message flow or saga pattern.
    /// Allows tracing a chain of messages across different message names and services.
    /// Value: "headless-corr-id"
    /// </summary>
    public const string CorrelationId = "headless-corr-id";

    /// <summary>
    /// Identifier of the message that directly caused this message to be published.
    /// Value: "headless-causation-id"
    /// </summary>
    public const string CausationId = "headless-causation-id";

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
    /// Multi-tenancy identifier for the message, populated from <see cref="MessageOptions.TenantId"/> at publish time
    /// and exposed on <c>ConsumeContext&lt;TMessage&gt;.TenantId</c> at consume time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The publish pipeline enforces a strict 4-case integrity policy: a raw write to this key
    /// without a typed property is rejected with <see cref="InvalidOperationException"/>; a raw write
    /// that disagrees with the typed property is also rejected; a raw write that matches the typed
    /// property is accepted. Use <see cref="MessageOptions.TenantId"/> as the source of truth.
    /// </para>
    /// <para>
    /// Consume-side values are untrusted wire data. The framework does not sanitize the value's
    /// charset; consuming applications must validate it before use in URLs, SQL columns, log lines,
    /// OpenTelemetry tags, or any other sensitive sink.
    /// </para>
    /// <para>Value: "headless-tenant-id".</para>
    /// </remarks>
    public const string TenantId = "headless-tenant-id";

    /// <summary>
    /// The native address the delivery arrived on, such as the NATS subject or the Kafka topic. The consumer client
    /// sets it on receipt and overwrites any value that came over the wire, so a producer cannot choose it.
    /// </summary>
    /// <remarks>
    /// Read it through <c>ConsumeContext.TransportAddress</c>. It is stored with the received message, so a retry from
    /// storage sees the address of the original delivery. Publishing rejects it as a reserved header.
    /// <para>Value: "headless-transport-address".</para>
    /// </remarks>
    public const string TransportAddress = "headless-transport-address";

    /// <summary>
    /// Identifier of the application instance that executed or is executing the message.
    /// Useful in distributed systems to track which instance processed a message.
    /// Value: "headless-exec-instance-id"
    /// </summary>
    public const string ExecutionInstanceId = "headless-exec-instance-id";

    /// <summary>
    /// Timestamp indicating when the message was sent/published, in UTC ISO 8601 format.
    /// Value: "headless-sent-time"
    /// </summary>
    public const string SentTime = "headless-sent-time";

    /// <summary>
    /// Timestamp indicating when a delayed message should be published, in UTC ISO 8601 format.
    /// This header is only present for messages scheduled for delayed delivery.
    /// Value: "headless-delay-time"
    /// </summary>
    public const string DelayTime = "headless-delay-time";

    /// <summary>
    /// Delivery mode requested by the caller before coordination resolution.
    /// Values are stable <see cref="DeliveryMode"/> names. This header is framework-owned and cannot be overridden.
    /// Value: "headless-delivery-requested"
    /// </summary>
    public const string RequestedDeliveryMode = "headless-delivery-requested";

    /// <summary>
    /// Delivery mode selected by the framework after coordination resolution.
    /// Values are stable <see cref="DeliveryMode"/> names. This header is framework-owned and cannot be overridden.
    /// Value: "headless-delivery-resolved"
    /// </summary>
    public const string ResolvedDeliveryMode = "headless-delivery-resolved";

    /// <summary>
    /// Whether the framework wrote this message inside the caller's unit-of-work transaction, so that it is
    /// discarded when that transaction rolls back. Values are the lowercase literals <c>"true"</c> and
    /// <c>"false"</c>. An absent header means the question was never recorded, not <c>"false"</c>.
    /// This header is framework-owned and cannot be overridden.
    /// Value: "headless-delivery-coordinated"
    /// </summary>
    public const string DeliveryCoordinated = "headless-delivery-coordinated";

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

    /// <summary>
    /// The publish intent of the message, stamped by the framework at publish time.
    /// Value is the stable legacy wire representation of <see cref="MessageLane"/> (<c>"Bus"</c> or <c>"Queue"</c>).
    /// On the consume side the framework rejects unknown values before dispatch and warns when a recognized
    /// value disagrees with the registered consumer lane. The registration-owned lane remains authoritative.
    /// Value: "headless-intent"
    /// </summary>
    public const string Intent = "headless-intent";

    /// <summary>
    /// Framework-generated identifier that correlates a request with its reply. Each request gets a new value, so it is
    /// not the message identifier or the correlation identifier, which callers and middleware can set. This header is
    /// framework-owned and cannot be overridden.
    /// Value: "headless-request-id"
    /// </summary>
    public const string RequestId = "headless-request-id";

    /// <summary>
    /// Opaque reply address of the process that sent a request. Only the sending process listens on it, and a
    /// responder writes only to an address inside the transport's reserved reply namespace. This header is
    /// framework-owned and cannot be overridden.
    /// Value: "headless-reply-to"
    /// </summary>
    public const string ReplyTo = "headless-reply-to";

    /// <summary>
    /// Absolute UTC instant, in ISO 8601 format, after which the responder neither starts nor replies to the request.
    /// The responder checks it against its own clock. This header is framework-owned and cannot be overridden.
    /// Value: "headless-request-deadline"
    /// </summary>
    public const string RequestDeadline = "headless-request-deadline";

    /// <summary>
    /// The <see cref="RequestId"/> of the request a reply answers. A reply carries its response contract in
    /// <see cref="MessageName"/> and <see cref="ContractVersion"/>. This header is framework-owned and cannot be
    /// overridden.
    /// Value: "headless-in-reply-to"
    /// </summary>
    public const string InReplyTo = "headless-in-reply-to";

    /// <summary>
    /// Outcome a reply reports: <c>"ok"</c> when the body is the response, or <c>"fault"</c> when the body describes a
    /// failure with a <c>RequestFaultCodes</c> code. This header is framework-owned and cannot be overridden.
    /// Value: "headless-reply-status"
    /// </summary>
    public const string ReplyStatus = "headless-reply-status";
}
