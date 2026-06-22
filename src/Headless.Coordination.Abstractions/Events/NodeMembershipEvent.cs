// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Coordination;

/// <summary>Base type for best-effort local membership observations derived from authoritative snapshots.</summary>
[PublicAPI]
public abstract record NodeMembershipEvent
{
    private protected NodeMembershipEvent(NodeIdentity identity)
    {
        Identity = identity;
    }

    /// <summary>The <c>node@incarnation</c> identity the event pertains to.</summary>
    public NodeIdentity Identity { get; }
}

/// <summary>Emitted when a node incarnation successfully registers and is first seen as alive.</summary>
[PublicAPI]
public sealed record NodeJoined : NodeMembershipEvent
{
    public NodeJoined(NodeIdentity identity)
        : base(identity) { }
}

/// <summary>
/// Emitted when a node's last heartbeat age crosses <see cref="CoordinationOptions.SuspicionThreshold"/>
/// and the node transitions to <see cref="NodeLivenessState.Suspected"/>.
/// </summary>
[PublicAPI]
public sealed record NodeSuspected : NodeMembershipEvent
{
    public NodeSuspected(NodeIdentity identity)
        : base(identity) { }
}

/// <summary>
/// Emitted when a previously suspected node resumes heartbeating and transitions back to
/// <see cref="NodeLivenessState.Alive"/>.
/// </summary>
[PublicAPI]
public sealed record NodeRecovered : NodeMembershipEvent
{
    public NodeRecovered(NodeIdentity identity)
        : base(identity) { }
}

/// <summary>
/// Emitted when a node gracefully calls <see cref="INodeMembership.LeaveAsync"/> or is permanently
/// classified as <see cref="NodeLivenessState.Dead"/> and purged from the store.
/// </summary>
[PublicAPI]
public sealed record NodeLeft : NodeMembershipEvent
{
    public NodeLeft(NodeIdentity identity)
        : base(identity) { }
}
