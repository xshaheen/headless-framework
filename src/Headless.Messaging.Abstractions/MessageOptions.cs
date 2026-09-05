// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

/// <summary>
/// Shared base for outbound message option records. Carries the delivery mode and intent-agnostic metadata fields
/// (message name, identifiers, tenancy, headers, callback) that every send path accepts.
/// </summary>
/// <remarks>
/// <para>
/// Bus and queue publisher interfaces accept records derived from this base. Each derived record adds the
/// lane-specific publishing options while the invoked publisher verb remains the only lane authority.
/// </para>
/// <para>
/// This type is a record so middleware can mutate a single property via a <c>with</c> expression
/// (for example, <c>options with { TenantId = "acme" }</c>) without manually copying every other property.
/// Equality is value-based across every scalar property; <see cref="Headers"/> uses structural comparison
/// (key/value sequence with <see cref="StringComparer.Ordinal"/> on keys).
/// </para>
/// </remarks>
[PublicAPI]
public abstract record MessageOptions
{
    /// <summary>
    /// Maximum supported length for <see cref="MessageId"/> when publishing messages that may be stored durably.
    /// </summary>
    public const int MessageIdMaxLength = 200;

    /// <summary>The initial schema version used when a message contract does not specify one.</summary>
    public const string InitialContractVersion = "1";

    /// <summary>Maximum supported message contract version length for durable inbox storage.</summary>
    public const int ContractVersionMaxLength = 100;

    /// <summary>
    /// Maximum supported length for <see cref="TenantId"/> when publishing messages that may be stored durably.
    /// </summary>
    public const int TenantIdMaxLength = 200;

    /// <summary>Gets the requested delivery behavior. The default is <see cref="DeliveryMode.Durable"/>.</summary>
    public DeliveryMode DeliveryMode { get; init; } = DeliveryMode.Durable;

    /// <summary>Gets the relative delay applied before the durably captured message is dispatched.</summary>
    /// <remarks>
    /// A delay requires durable delivery. With <see cref="DeliveryMode.Auto"/> it selects durable capture;
    /// with <see cref="DeliveryMode.TransportDirect"/> the operation is rejected.
    /// </remarks>
    public TimeSpan? Delay { get; init; }

    /// <summary>
    /// Gets the explicit message name override. When <see langword="null"/>, the message name is resolved from mappings or conventions.
    /// </summary>
    public string? MessageName { get; init; }

    /// <summary>Gets the explicit message contract schema version override.</summary>
    public string? ContractVersion { get; init; }

    /// <summary>Optional logical affinity key mapped to the configured provider native routing mechanism.</summary>
    /// <remarks>The destination must be registered and support affinity. This does not promise FIFO or exclusive handling.</remarks>
    public string? RoutingAffinityKey { get; init; }

    /// <summary>
    /// Gets custom application headers. Reserved messaging headers are rejected, and header names/values
    /// cannot contain control characters.
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

    /// <summary>Gets the identifier of the message that directly caused this message.</summary>
    public string? CausationId { get; init; }

    /// <summary>Suppresses ambient correlation, causation, and tenant defaults when forwarding a captured business snapshot.</summary>
    /// <remarks>
    /// Defaults to false. Contract resolution and diagnostic trace propagation are unchanged.
    /// Required tenancy still rejects a missing explicit tenant; suppression never bypasses tenant enforcement.
    /// </remarks>
    public bool SuppressAmbientBusinessContext { get; init; }

    /// <summary>
    /// Gets the explicit correlation sequence override.
    /// </summary>
    public int? CorrelationSequence { get; init; }

    /// <summary>
    /// Gets the callback message name override used for response messages.
    /// </summary>
    public string? CallbackName { get; init; }

    internal Type? MessageType { get; init; }

    /// <summary>
    /// Gets the explicit multi-tenancy identifier for this message.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When set, the publish pipeline stamps the value into the <c>Headers.TenantId</c> wire header.
    /// When <see langword="null"/>, configured tenant propagation or required-tenancy resolution may use the
    /// ambient tenant unless <see cref="SuppressAmbientBusinessContext"/> is enabled. Without a resolved tenant,
    /// no header is written; required tenancy rejects the publish instead.
    /// </para>
    /// <para>
    /// The publish pipeline enforces a strict 4-case integrity policy. A raw write to
    /// <c>Headers.TenantId</c> through <see cref="Headers"/> without setting this typed property
    /// is rejected with <see cref="InvalidOperationException"/>. If the typed property and a matching
    /// raw header are both set, the publish is accepted as a no-op reconciliation; if they disagree,
    /// the publish is rejected.
    /// </para>
    /// <para>
    /// Values longer than <see cref="TenantIdMaxLength"/> or whitespace-only values are rejected at publish time.
    /// Charset sanitization (URL/SQL/log safety) is the consumer application's responsibility.
    /// </para>
    /// </remarks>
    public string? TenantId { get; init; }

    /// <summary>
    /// Determines whether the specified <see cref="MessageOptions"/> equals this instance
    /// using value semantics across every scalar field plus structural comparison on <see cref="Headers"/>.
    /// </summary>
    public virtual bool Equals(MessageOptions? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (other.GetType() != GetType())
        {
            return false;
        }

        return DeliveryMode == other.DeliveryMode
            && Nullable.Equals(Delay, other.Delay)
            && string.Equals(MessageName, other.MessageName, StringComparison.Ordinal)
            && string.Equals(ContractVersion, other.ContractVersion, StringComparison.Ordinal)
            && string.Equals(RoutingAffinityKey, other.RoutingAffinityKey, StringComparison.Ordinal)
            && string.Equals(MessageId, other.MessageId, StringComparison.Ordinal)
            && string.Equals(CorrelationId, other.CorrelationId, StringComparison.Ordinal)
            && string.Equals(CausationId, other.CausationId, StringComparison.Ordinal)
            && SuppressAmbientBusinessContext == other.SuppressAmbientBusinessContext
            && CorrelationSequence == other.CorrelationSequence
            // MessageType is internal-init and participates in equality so internally-produced options carrying a
            // captured response type are not silently treated as equal to otherwise-identical options (prevents
            // type-header loss in equality/cache contexts). External callers always have it null, so it is a no-op.
            && MessageType == other.MessageType
            && string.Equals(CallbackName, other.CallbackName, StringComparison.Ordinal)
            && string.Equals(TenantId, other.TenantId, StringComparison.Ordinal)
            && HeadersEqual(Headers, other.Headers);
    }

    /// <summary>Returns the hash code for this instance using structural <see cref="Headers"/> hashing.</summary>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(DeliveryMode);
        hash.Add(Delay);
        hash.Add(MessageName, StringComparer.Ordinal);
        hash.Add(ContractVersion, StringComparer.Ordinal);
        hash.Add(RoutingAffinityKey, StringComparer.Ordinal);
        hash.Add(MessageId, StringComparer.Ordinal);
        hash.Add(CorrelationId, StringComparer.Ordinal);
        hash.Add(CausationId, StringComparer.Ordinal);
        hash.Add(SuppressAmbientBusinessContext);
        hash.Add(CorrelationSequence);
        // MessageType is internal-init and participates in equality so internally-produced options carrying a
        // captured response type are not silently treated as equal to otherwise-identical options (prevents
        // type-header loss in equality/cache contexts). External callers always have it null, so it is a no-op.
        hash.Add(MessageType);
        hash.Add(CallbackName, StringComparer.Ordinal);
        hash.Add(TenantId, StringComparer.Ordinal);

        if (Headers is not null)
        {
            foreach (var kvp in Headers.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                hash.Add(kvp.Key, StringComparer.Ordinal);
                hash.Add(kvp.Value, StringComparer.Ordinal);
            }
        }

        return hash.ToHashCode();
    }

    private protected static bool HeadersEqual(IDictionary<string, string?>? left, IDictionary<string, string?>? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        if (left.Count != right.Count)
        {
            return false;
        }

        var leftHeaders = left.OrderBy(p => p.Key, StringComparer.Ordinal).ToArray();
        var rightHeaders = right.OrderBy(p => p.Key, StringComparer.Ordinal).ToArray();

        for (var i = 0; i < leftHeaders.Length; i++)
        {
            var leftKvp = leftHeaders[i];
            var rightKvp = rightHeaders[i];

            if (
                !string.Equals(leftKvp.Key, rightKvp.Key, StringComparison.Ordinal)
                || !string.Equals(leftKvp.Value, rightKvp.Value, StringComparison.Ordinal)
            )
            {
                return false;
            }
        }

        return true;
    }
}
