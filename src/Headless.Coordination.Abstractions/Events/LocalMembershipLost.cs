// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Coordination;

/// <summary>
/// Signal emitted when the local process loses its own membership identity — either because it was
/// superseded by a higher incarnation, or because the store evicted the heartbeat. Handled before other
/// events; see <see cref="CoordinationOptions.MembershipLostBehavior"/> for the configured response.
/// </summary>
[PublicAPI]
public sealed record LocalMembershipLost : NodeMembershipEvent
{
    public LocalMembershipLost(NodeIdentity identity)
        : base(identity) { }
}
