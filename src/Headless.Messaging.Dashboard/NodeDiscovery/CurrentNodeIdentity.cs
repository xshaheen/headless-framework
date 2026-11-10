// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Dashboard;

/// <summary>
/// The name under which this node registers itself with a discovery backend that addresses nodes by name
/// alone, with no namespace (Consul). A discovery package registers it; forwarding reads it to recognize this node
/// in the list discovery returns and never call it as a peer. Kubernetes discovery does not register it, because a
/// Service spreads calls over its pods and names no single node.
/// </summary>
/// <param name="name">The current node's registered name, or <see langword="null"/> when the backend assigns one.</param>
internal sealed class CurrentNodeIdentity(string? name)
{
    public string? Name { get; } = name;
}
