// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Coordination;

/// <summary>Serves as the base record for best-effort local membership observations derived from snapshots.</summary>
[PublicAPI]
public abstract record NodeMembershipEvent
{
    private protected NodeMembershipEvent(NodeIdentity identity)
    {
        Identity = identity;
    }

    /// <summary>Gets the node and incarnation identity for this event.</summary>
    public NodeIdentity Identity { get; }
}

/// <summary>Emitted when a node incarnation registers and is first observed as alive.</summary>
[PublicAPI]
public sealed record NodeJoined : NodeMembershipEvent
{
    public NodeJoined(NodeIdentity identity)
        : base(identity) { }
}

/// <summary>
/// Emitted when the heartbeat age of a node exceeds <see cref="CoordinationOptions.SuspicionThreshold"/>
/// and the node transitions to <see cref="NodeLivenessState.Suspected"/>.
/// </summary>
[PublicAPI]
public sealed record NodeSuspected : NodeMembershipEvent
{
    public NodeSuspected(NodeIdentity identity)
        : base(identity) { }
}

/// <summary>
/// Emitted when a suspected node resumes heartbeats and transitions back to <see cref="NodeLivenessState.Alive"/>.
/// </summary>
[PublicAPI]
public sealed record NodeRecovered : NodeMembershipEvent
{
    public NodeRecovered(NodeIdentity identity)
        : base(identity) { }
}

/// <summary>
/// Emitted when a node calls <see cref="INodeMembership.LeaveAsync"/> or transitions to
/// <see cref="NodeLivenessState.Dead"/> and is removed from the store.
/// </summary>
[PublicAPI]
public sealed record NodeLeft : NodeMembershipEvent
{
    public NodeLeft(NodeIdentity identity)
        : base(identity) { }
}
