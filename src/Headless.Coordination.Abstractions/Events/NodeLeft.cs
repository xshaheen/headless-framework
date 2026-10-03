// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Coordination;

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
