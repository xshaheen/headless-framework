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
