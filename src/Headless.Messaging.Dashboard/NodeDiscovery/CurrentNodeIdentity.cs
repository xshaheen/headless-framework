// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Dashboard;

/// <summary>
/// The name under which this node registers itself with a discovery backend that addresses nodes by name
/// alone, with no namespace (Consul). A discovery package registers it; its presence tells the gateway to
/// resolve nodes by name without a namespace cookie and never to proxy a request to the current node, and
/// tells the stats endpoint it may count nodes through discovery. Kubernetes discovery does not register it.
/// </summary>
/// <param name="name">The current node's registered name, or <see langword="null"/> when the backend assigns one.</param>
internal sealed class CurrentNodeIdentity(string? name)
{
    public string? Name { get; } = name;
}
