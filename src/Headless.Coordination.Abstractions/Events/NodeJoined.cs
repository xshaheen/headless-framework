// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Coordination;

/// <summary>Emitted when a node incarnation successfully registers and is first seen as alive.</summary>
[PublicAPI]
public sealed record NodeJoined : NodeMembershipEvent
{
    public NodeJoined(NodeIdentity identity)
        : base(identity) { }
}
