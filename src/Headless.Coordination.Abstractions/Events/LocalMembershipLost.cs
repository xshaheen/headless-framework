// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Coordination;

/// <summary>Signal emitted when the local process loses its own membership identity.</summary>
[PublicAPI]
public sealed record LocalMembershipLost : NodeMembershipEvent
{
    public LocalMembershipLost(NodeIdentity identity)
        : base(identity) { }
}
