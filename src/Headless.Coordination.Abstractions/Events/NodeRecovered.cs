// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Coordination;

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
