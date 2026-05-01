// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

/// <summary>
/// Configures a publish operation with explicit topic, correlation, and custom header overrides.
/// </summary>
public sealed class PublishOptions
{
    /// <summary>
    /// Maximum supported length for <see cref="MessageId"/> when publishing messages that may be stored durably.
    /// </summary>
    public const int MessageIdMaxLength = 200;

    /// <summary>
    /// Maximum supported length for <see cref="TenantId"/> when publishing messages that may be stored durably.
    /// </summary>
    public const int TenantIdMaxLength = 200;

    /// <summary>
    /// Gets the explicit topic override. When <see langword="null"/>, the topic is resolved from mappings or conventions.
    /// </summary>
    public string? Topic { get; init; }

    /// <summary>
    /// Gets custom application headers. Reserved messaging headers are rejected.
    /// </summary>
    public IDictionary<string, string?>? Headers { get; init; }

    /// <summary>
    /// Gets the explicit logical message identifier override.
    /// </summary>
    /// <remarks>
    /// Durable outbox providers store this value in 200-character columns, so values longer than
    /// <see cref="MessageIdMaxLength"/> are rejected before persistence.
    /// </remarks>
    public string? MessageId { get; init; }

    /// <summary>
    /// Gets the explicit correlation identifier override.
    /// </summary>
    public string? CorrelationId { get; init; }

    /// <summary>
    /// Gets the explicit correlation sequence override.
    /// </summary>
    public int? CorrelationSequence { get; init; }

    /// <summary>
    /// Gets the callback topic override used for response messages.
    /// </summary>
    public string? CallbackName { get; init; }

    /// <summary>
    /// Gets the explicit multi-tenancy identifier for this message.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When set, the publish pipeline stamps the value into the <see cref="Headers.TenantId"/> wire header.
    /// When <see langword="null"/>, no header is written and consumers observe a <see langword="null"/>
    /// <see cref="ConsumeContext{TMessage}.TenantId"/>.
    /// </para>
    /// <para>
    /// The publish pipeline enforces a strict 4-case integrity policy. A raw write to
    /// <see cref="Headers.TenantId"/> through <see cref="Headers"/> without setting this typed property
    /// is rejected with <see cref="InvalidOperationException"/>. If the typed property and a matching
    /// raw header are both set, the publish is accepted as a no-op reconciliation; if they disagree,
    /// the publish is rejected. Inspect <see cref="System.Exception.Data"/> for the
    /// <c>Headless.Messaging.FailureCode</c> entry to distinguish failure modes programmatically.
    /// </para>
    /// <para>
    /// Values longer than <see cref="TenantIdMaxLength"/> or whitespace-only values are rejected at publish time.
    /// Charset sanitization (URL/SQL/log safety) is the consumer application's responsibility.
    /// </para>
    /// </remarks>
    public string? TenantId { get; init; }
}
