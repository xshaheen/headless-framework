// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Coordination;

/// <summary>
/// Emitted when the local process loses its own membership identity because another node
/// superseded the incarnation or the backing store evicted the heartbeat.
/// </summary>
/// <remarks>
/// Handled before other events. See <see cref="CoordinationOptions.MembershipLostBehavior"/> for the configured response.
/// </remarks>
[PublicAPI]
public sealed record LocalMembershipLost : NodeMembershipEvent
{
    public LocalMembershipLost(NodeIdentity identity)
        : base(identity) { }
}
