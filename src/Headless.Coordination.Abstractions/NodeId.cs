// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Coordination;

/// <summary>Represents a stable node identifier within a coordination cluster.</summary>
/// <remarks>
/// Node identifier stability determines incarnation behavior. Prefer Kubernetes pod name plus namespace for
/// Deployments, StatefulSet pod names for stable ordinal workloads, and explicitly configured identifiers only
/// when uniqueness is externally guaranteed. Generated process identifiers are appropriate for local development,
/// but they make each start a brand-new node. The store never purges a node identifier's generation counter,
/// because purging it would let a returning node reuse an incarnation, so every distinct identifier leaves a
/// permanent entry and high-cardinality or generated identifiers grow the generation keyspace without bound.
/// </remarks>
[PublicAPI]
public readonly record struct NodeId
{
    /// <summary>
    /// Gets the maximum node identifier length in characters. Every membership store keys rows by the cluster
    /// name and the node identifier together, and SQL Server caps a clustered key at 900 bytes: two bytes per
    /// character for both, plus the incarnation, must fit.
    /// </summary>
    public const int MaxLength = 256;

    /// <summary>Initializes a new instance of the <see cref="NodeId"/> struct.</summary>
    /// <param name="value">The node identifier string.</param>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="value"/> is empty, contains only whitespace, or is not a portable key.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> exceeds <see cref="MaxLength"/> characters.</exception>
    public NodeId(string value)
    {
        Argument.IsNotNullOrWhiteSpace(value);
        Argument.HasMaxLength(value, MaxLength);
        Value = Argument.IsPortableKey(value);
    }

    /// <summary>Gets the underlying string value of this node identifier.</summary>
    public string Value { get; }

    /// <inheritdoc/>
    public override string ToString()
    {
        return Value;
    }
}
