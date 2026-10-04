// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Coordination;

/// <summary>Represents a stable node identifier within a coordination cluster.</summary>
/// <remarks>
/// Node identifier stability determines incarnation behavior. Prefer Kubernetes pod name and namespace for
/// Deployments, and pod names for StatefulSets. Set configured identifiers explicitly only when uniqueness
/// is externally guaranteed. Generated process identifiers make each process restart a new node, growing the
/// incarnation keyspace over time.
/// </remarks>
[PublicAPI]
public readonly record struct NodeId
{
    /// <summary>
    /// Gets the maximum node identifier length in characters. Membership stores key rows by cluster name
    /// and node identifier. The combined length fits within the 900-byte clustered index limit of SQL Server.
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
