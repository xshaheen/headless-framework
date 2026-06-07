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

    public NodeIdentity Identity { get; }
}

[PublicAPI]
public sealed record NodeJoined : NodeMembershipEvent
{
    public NodeJoined(NodeIdentity identity)
        : base(identity) { }
}

[PublicAPI]
public sealed record NodeSuspected : NodeMembershipEvent
{
    public NodeSuspected(NodeIdentity identity)
        : base(identity) { }
}

[PublicAPI]
public sealed record NodeRecovered : NodeMembershipEvent
{
    public NodeRecovered(NodeIdentity identity)
        : base(identity) { }
}

[PublicAPI]
public sealed record NodeLeft : NodeMembershipEvent
{
    public NodeLeft(NodeIdentity identity)
        : base(identity) { }
}
