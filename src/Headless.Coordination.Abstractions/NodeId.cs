// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Coordination;

/// <summary>Stable node identifier within a coordination cluster.</summary>
/// <remarks>
/// Node-id stability determines incarnation semantics. Prefer Kubernetes pod name plus namespace for
/// deployments, StatefulSet pod name for stable ordinal workloads, and explicit configured ids only when
/// uniqueness is externally guaranteed. Generated process ids are appropriate for local development but
/// make each start a brand-new node.
/// </remarks>
[PublicAPI]
public readonly record struct NodeId
{
    /// <summary>Initializes a <see cref="NodeId"/> with the given string value.</summary>
    /// <param name="value">The node identifier string. Must not be null, empty, or whitespace.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="value"/> is empty or contains only whitespace.
    /// </exception>
    public NodeId(string value)
    {
        Value = Argument.IsNotNullOrWhiteSpace(value);
    }

    /// <summary>The underlying string value of this node identifier.</summary>
    public string Value { get; }

    /// <inheritdoc/>
    public override string ToString()
    {
        return Value;
    }
}
