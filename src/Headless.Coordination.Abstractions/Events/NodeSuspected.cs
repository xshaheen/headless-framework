// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Coordination;

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
