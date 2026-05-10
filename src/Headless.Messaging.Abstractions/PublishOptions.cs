// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

/// <summary>
/// Configures a publish operation with explicit topic, correlation, and custom header overrides.
/// </summary>
/// <remarks>
/// <para>
/// This type is a record so publish-side filters can mutate a single property via a <c>with</c>
/// expression (for example, <c>options with { TenantId = "acme" }</c>) without manually copying
/// every other property. Equality is value-based across all scalar properties; <see cref="Headers"/>
/// uses structural comparison (key/value sequence with <see cref="StringComparer.Ordinal"/> on keys).
/// </para>
/// <para>
/// Two <see cref="PublishOptions"/> instances are equal when every scalar field matches and their
/// <see cref="Headers"/> dictionaries contain the same key/value pairs — independent of the
/// underlying dictionary instance, ordering, or comparer. This avoids the reference-equality footgun
/// that the synthesized record equality would otherwise introduce on the dictionary-typed property.
/// </para>
/// </remarks>
public sealed record PublishOptions
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
    /// the publish is rejected.
    /// </para>
    /// <para>
    /// Values longer than <see cref="TenantIdMaxLength"/> or whitespace-only values are rejected at publish time.
    /// Charset sanitization (URL/SQL/log safety) is the consumer application's responsibility.
    /// </para>
    /// </remarks>
    public string? TenantId { get; init; }

    /// <summary>
    /// Determines whether the specified <see cref="PublishOptions"/> equals this instance using
    /// value semantics across every scalar field plus structural comparison on <see cref="Headers"/>.
    /// </summary>
    public bool Equals(PublishOptions? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return string.Equals(Topic, other.Topic, StringComparison.Ordinal)
            && string.Equals(MessageId, other.MessageId, StringComparison.Ordinal)
            && string.Equals(CorrelationId, other.CorrelationId, StringComparison.Ordinal)
            && CorrelationSequence == other.CorrelationSequence
            && string.Equals(CallbackName, other.CallbackName, StringComparison.Ordinal)
            && string.Equals(TenantId, other.TenantId, StringComparison.Ordinal)
            && _HeadersEqual(Headers, other.Headers);
    }

    /// <summary>Returns the hash code for this instance using structural <see cref="Headers"/> hashing.</summary>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Topic, StringComparer.Ordinal);
        hash.Add(MessageId, StringComparer.Ordinal);
        hash.Add(CorrelationId, StringComparer.Ordinal);
        hash.Add(CorrelationSequence);
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

    private static bool _HeadersEqual(IDictionary<string, string?>? left, IDictionary<string, string?>? right)
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

        foreach (var kvp in left)
        {
            if (!right.TryGetValue(kvp.Key, out var rightValue))
            {
                return false;
            }

            if (!string.Equals(kvp.Value, rightValue, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }
}
