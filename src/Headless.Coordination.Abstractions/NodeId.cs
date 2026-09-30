// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Coordination;

/// <summary>Stable node identifier within a coordination cluster.</summary>
/// <remarks>
/// Node-id stability determines incarnation semantics. Prefer Kubernetes pod name plus namespace for
/// deployments, StatefulSet pod name for stable ordinal workloads, and explicit configured ids only when
/// uniqueness is externally guaranteed. Generated process ids are appropriate for local development but
/// make each start a brand-new node — and because the store never purges a node id's generation counter
/// (purging it would let a returning node reuse an incarnation), every distinct id leaves a permanent
/// entry, so high-cardinality or generated ids grow the generation keyspace without bound.
/// </remarks>
[PublicAPI]
public readonly record struct NodeId
{
    /// <summary>Initializes a <see cref="NodeId"/> with the given string value.</summary>
    /// <param name="value">
    /// The node identifier string. Must not be null or blank, and must be text every membership store keeps unchanged
    /// as a key.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="value"/> is empty or whitespace-only, or is text some store would merge, reject, or
    /// rewrite (see <see cref="Argument.IsPortableKey"/>).
    /// </exception>
    public NodeId(string value)
    {
        Argument.IsNotNullOrWhiteSpace(value);
        Value = Argument.IsPortableKey(value);
    }

    /// <summary>The underlying string value of this node identifier.</summary>
    public string Value { get; }

    /// <inheritdoc/>
    public override string ToString()
    {
        return Value;
    }
}
