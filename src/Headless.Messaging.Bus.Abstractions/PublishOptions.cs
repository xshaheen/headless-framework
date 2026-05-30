// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

/// <summary>
/// Configures a broadcast (bus) publish operation with explicit message name, correlation, custom header,
/// and (for outbox-backed publishes) delivery-delay overrides.
/// </summary>
/// <remarks>
/// <para>
/// This type is a record so publish-side middleware can mutate a single property via a <c>with</c>
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
[PublicAPI]
public sealed record PublishOptions : MessagePublishOptionsBase
{
    /// <summary>
    /// Gets the delay applied before the persisted message is dispatched.
    /// </summary>
    /// <remarks>
    /// Honored only by outbox-backed bus publishers. Ignored by fire-and-forget publishers.
    /// </remarks>
    public TimeSpan? Delay { get; init; }

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

        return base.Equals(other) && Nullable.Equals(Delay, other.Delay);
    }

    /// <summary>Returns the hash code for this instance using structural <see cref="Headers"/> hashing.</summary>
    public override int GetHashCode()
    {
        return HashCode.Combine(base.GetHashCode(), Delay);
    }
}
