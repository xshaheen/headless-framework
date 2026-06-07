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
    public NodeId(string value)
    {
        Value = Argument.IsNotNullOrWhiteSpace(value);
    }

    public string Value { get; }

    public override string ToString()
    {
        return Value;
    }
}
