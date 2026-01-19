// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Messages.Messages;

namespace Framework.Messages;

/// <summary>
/// Provides context information for message consumption, including the message payload and metadata.
/// </summary>
/// <typeparam name="TMessage">The type of the message being consumed.</typeparam>
/// <remarks>
/// <para>
/// This context is created for each message consumption and provides access to:
/// <list type="bullet">
/// <item><description>The strongly-typed message payload</description></item>
/// <item><description>Message metadata (ID, correlation ID, timestamps)</description></item>
/// <item><description>Custom headers for cross-cutting concerns</description></item>
/// </list>
/// </para>
/// </remarks>
public sealed class ConsumeContext<TMessage>
    where TMessage : class
{
    /// <summary>
    /// Gets the strongly-typed message payload.
    /// </summary>
    /// <value>
    /// The deserialized message object that will be processed by the consumer.
    /// This property is never null.
    /// </value>
    public required TMessage Message { get; init; }

    /// <summary>
    /// Gets the unique identifier for this message.
    /// </summary>
    /// <value>
    /// A unique string identifier that identifies this specific message instance across the entire system.
    /// Used for deduplication, tracking, and correlation in logs.
    /// Supports GUID, ULID, snowflake IDs, or custom formats.
    /// </value>
    /// <exception cref="ArgumentException">
    /// Thrown when attempting to set a null or whitespace value.
    /// </exception>
    /// <remarks>
    /// This ID should be unique per message and persist across retries of the same message.
    /// The validation prevents common configuration errors where MessageId is not properly initialized.
    /// </remarks>
    public required string MessageId
    {
        get;
        init
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(
                    "MessageId cannot be null or whitespace. Each message must have a unique identifier.",
                    nameof(value)
                );
            }

            field = value;
        }
    }

    /// <summary>
    /// Gets the correlation identifier that links related messages together.
    /// </summary>
    /// <value>
    /// An optional string identifier that correlates this message with other related messages in a workflow or conversation.
    /// Returns <c>null</c> if no correlation is established.
    /// Supports GUID, ULID, snowflake IDs, or custom formats.
    /// </value>
    /// <exception cref="ArgumentException">
    /// Thrown when attempting to set an empty string.
    /// Use <c>null</c> instead to indicate no correlation.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Correlation IDs are used to trace messages across distributed systems, linking:
    /// <list type="bullet">
    /// <item><description>Request and reply messages</description></item>
    /// <item><description>Saga/workflow steps</description></item>
    /// <item><description>Related business events (e.g., OrderPlaced → OrderPaid → OrderShipped)</description></item>
    /// <item><description>Messages originating from the same HTTP request or user action</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// If this message was published from within another message handler, the CorrelationId should
    /// typically be set to the parent message's MessageId or CorrelationId to maintain the trace.
    /// </para>
    /// </remarks>
    public required string? CorrelationId
    {
        get;
        init
        {
            if (value is not null && string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(
                    "CorrelationId cannot be an empty string. Use null to indicate no correlation.",
                    nameof(value)
                );
            }

            field = value;
        }
    }

    /// <summary>
    /// Gets the collection of custom headers attached to this message.
    /// </summary>
    /// <value>
    /// A read-only dictionary containing key-value pairs of message headers.
    /// This collection is never null but may be empty if no headers were set.
    /// </value>
    /// <remarks>
    /// <para>
    /// Headers are used for cross-cutting concerns and metadata that doesn't belong in the message payload:
    /// <list type="bullet">
    /// <item><description>Authentication tokens or user context</description></item>
    /// <item><description>Tracing and telemetry information</description></item>
    /// <item><description>Routing decisions (e.g., region, tenant, feature flags)</description></item>
    /// <item><description>Message versioning or schema information</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// Common header keys (by convention):
    /// <list type="bullet">
    /// <item><description><c>FailureReason</c>: Exception type from previous failure</description></item>
    /// <item><description><c>Region</c>: Geographic region for routing</description></item>
    /// <item><description><c>TenantId</c>: Multi-tenancy identifier</description></item>
    /// <item><description><c>UserId</c>: Originating user for audit trails</description></item>
    /// </list>
    /// </para>
    /// </remarks>
    public required MessageHeader Headers { get; init; }

    /// <summary>
    /// Gets the timestamp when this message was originally published.
    /// </summary>
    /// <value>
    /// A UTC timestamp indicating when the message was first published to the message bus.
    /// This value does not change across retries.
    /// </value>
    /// <remarks>
    /// Use this timestamp to:
    /// <list type="bullet">
    /// <item><description>Calculate message age for SLA monitoring</description></item>
    /// <item><description>Implement time-based business logic (e.g., expiration)</description></item>
    /// <item><description>Audit and compliance tracking</description></item>
    /// <item><description>Performance analysis (end-to-end latency)</description></item>
    /// </list>
    /// </remarks>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>
    /// Gets the topic or queue name from which this message was consumed.
    /// </summary>
    /// <value>
    /// The name of the message topic, queue, or routing key.
    /// Used for filtering and routing decisions.
    /// </value>
    /// <remarks>
    /// The topic name is useful for:
    /// <list type="bullet">
    /// <item><description>Topic-based filtering in multi-type consumers</description></item>
    /// <item><description>Logging and telemetry (which topic is this message from)</description></item>
    /// <item><description>Routing decisions based on topic patterns</description></item>
    /// <item><description>Dead letter queue topic construction (e.g., <c>{Topic}.failed</c>)</description></item>
    /// </list>
    /// </remarks>
    public required string Topic { get; init; }
}
