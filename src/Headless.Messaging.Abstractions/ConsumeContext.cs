// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Messaging;

/// <summary>
/// Provides object-typed context information for message consumption.
/// </summary>
/// <remarks>
/// This base type is used by object-typed consume middleware that should apply to every message type.
/// Use <see cref="ConsumeContext{TMessage}"/> when the middleware or consumer needs strongly-typed payload access.
/// </remarks>
[PublicAPI]
public record ConsumeContext
{
    private CancellationToken _cancellationToken;
    private volatile bool _isCompleted;

    /// <summary>
    /// Gets the deserialized message object. May be <see langword="null"/> only for invalid custom construction.
    /// </summary>
    public object? Message { get; init; }

    /// <summary>
    /// Gets the runtime type of <see cref="Message"/>.
    /// </summary>
    public Type MessageType
    {
        get;
        init { field = Argument.IsNotNull(value); }
    } = typeof(object);

    /// <summary>
    /// Gets the cancellation token currently active for this consume operation.
    /// </summary>
    public CancellationToken CancellationToken
    {
        get => _cancellationToken;
        internal init => _cancellationToken = value;
    }

    internal object? Response { get; private set; }

    internal Type? ResponseType { get; private set; }

    internal string? ResponseCallbackName { get; private set; }

    /// <summary>
    /// Replaces the active cancellation token for downstream middleware and the inner consumer invocation.
    /// </summary>
    public void SetCancellationToken(CancellationToken cancellationToken)
    {
        if (_isCompleted)
        {
            throw new InvalidOperationException("ConsumeContext is read-only after the consumer has completed.");
        }

        _cancellationToken = cancellationToken;
    }

    /// <summary>
    /// Captures a typed response payload to publish to the current message callback.
    /// </summary>
    /// <typeparam name="TResponse">
    /// The response contract type to stamp on the callback message. Must be a reference type
    /// (<c>where TResponse : class</c>); wrap value types in a record if needed.
    /// </typeparam>
    /// <param name="value">The response value. May be <see langword="null"/> for typed-null responses.</param>
    /// <remarks>
    /// The callback rides the durable bus path, which is <strong>at-least-once</strong>: if the process crashes
    /// — or the success-mark write transiently fails — after the response is written to the outbox but before the
    /// request is marked succeeded, the request is redelivered and the response is published again. Make response
    /// consumers idempotent (e.g. dedupe on
    /// <c>(CorrelationId, CorrelationSequence)</c> — <c>CorrelationId</c> alone is ambiguous across hops because
    /// it is set to the immediate parent message id per hop, not the chain root); the framework does not
    /// deduplicate callback deliveries.
    /// </remarks>
    public void SetResponse<TResponse>(TResponse value)
        where TResponse : class
    {
        if (_isCompleted)
        {
            throw new InvalidOperationException("ConsumeContext is read-only after the consumer has completed.");
        }

        Response = value;
        ResponseType = typeof(TResponse);
    }

    /// <summary>
    /// Stamps the response callback name that the published response message will carry, routing the captured
    /// response to the next hop in a callback chain.
    /// </summary>
    /// <remarks>
    /// This is the first-class way to chain to a callback that the originating message did not declare.
    /// It targets the reserved <c>Headers.CallbackName</c> through a typed surface so the value is mapped
    /// to <see cref="MessageOptions.CallbackName"/> on the response publish, rather than being
    /// pushed through <see cref="MessageHeader.AddResponseHeader"/> (which the publish pipeline rejects for
    /// reserved keys). The framework does not cap callback hops, so callback chains must be kept acyclic and
    /// bounded by the consumer — a self-referential or cyclic chain produces an unbounded callback storm.
    /// </remarks>
    /// <param name="callbackName">The response callback name to attach to the published response.</param>
    public void SetResponseCallbackName(string callbackName)
    {
        if (_isCompleted)
        {
            throw new InvalidOperationException("ConsumeContext is read-only after the consumer has completed.");
        }

        ResponseCallbackName = Argument.IsNotNullOrWhiteSpace(callbackName);
    }

    internal void MarkCompleted()
    {
        _isCompleted = true;
    }

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
            field = Argument.IsNotNullOrWhiteSpace(
                value,
                "MessageId cannot be null or whitespace. Each message must have a unique identifier."
            );
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
    /// Gets the multi-tenancy identifier for this message.
    /// </summary>
    /// <value>
    /// The tenant identifier carried on the <c>Headers.TenantId</c> wire header
    /// (<c>"headless-tenant-id"</c>), populated from <see cref="MessageOptions.TenantId"/> at publish time.
    /// Returns <see langword="null"/> when the header is absent, empty, whitespace, or longer than
    /// <see cref="MessageOptions.TenantIdMaxLength"/> (lenient consume-side handling).
    /// </value>
    /// <exception cref="ArgumentException">
    /// Thrown when attempting to set an empty or whitespace string.
    /// Use <see langword="null"/> instead to indicate no tenant.
    /// </exception>
    /// <remarks>
    /// This property is the typed surface for tenancy on the consume side. Reading the wire header
    /// directly via <see cref="Headers"/> is supported but discouraged — the typed property is canonical.
    /// </remarks>
    public string? TenantId
    {
        get;
        init
        {
            if (value is not null && string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(
                    "TenantId cannot be an empty or whitespace string. Use null to indicate no tenant.",
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
    /// <item><description><c>UserId</c>: Originating user for audit trails</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// Multi-tenancy is first-class: prefer <c>ConsumeContext&lt;TMessage&gt;.TenantId</c> over reading the
    /// <c>Headers.TenantId</c> header directly.
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
    /// Gets the message name or queue name from which this message was consumed.
    /// </summary>
    /// <value>
    /// The message name, queue, or routing key.
    /// Used for filtering and routing decisions.
    /// </value>
    /// <remarks>
    /// The message name is useful for:
    /// <list type="bullet">
    /// <item><description>Message-name-based filtering in multi-type consumers</description></item>
    /// <item><description>Logging and telemetry (which message name this message is from)</description></item>
    /// <item><description>Routing decisions based on message-name patterns</description></item>
    /// <item><description>Dead-letter queue name construction (e.g., <c>{MessageName}.failed</c>)</description></item>
    /// </list>
    /// </remarks>
    public required string MessageName { get; init; }

    /// <summary>
    /// Gets the delivery intent that produced this consume call: <see cref="IntentType.Bus"/> for
    /// broadcast (publish/subscribe) dispatch, <see cref="IntentType.Queue"/> for point-to-point
    /// (work-queue) dispatch.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The intent is registration-derived, not envelope-derived: the framework stamps this value
    /// from the consumer registration (<c>OnBus&lt;TConsumer&gt;()</c> vs
    /// <c>OnQueue&lt;TConsumer&gt;()</c>) that delivered the message. No on-wire header carries
    /// intent; the receiving runtime knows the dispatch path because it owns it.
    /// </para>
    /// <para>
    /// A handler type registered under both intents (one bus, one queue) is invoked once per
    /// dispatch path, and each call observes the matching <see cref="IntentType"/> value.
    /// </para>
    /// </remarks>
    public required IntentType IntentType { get; init; }
}

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
[PublicAPI]
public record ConsumeContext<TMessage> : ConsumeContext
    where TMessage : class
{
    /// <summary>
    /// Gets the strongly-typed message payload.
    /// </summary>
    /// <value>
    /// The deserialized message object that will be processed by the consumer.
    /// This property is never null.
    /// </value>
    public new required TMessage Message
    {
        get => (TMessage)base.Message!;
        init
        {
            base.Message = value;
            MessageType = typeof(TMessage);
        }
    }
}
