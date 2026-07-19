// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Dashboard.NodeDiscovery;

/// <summary>
/// Provides service-discovery operations for the Messaging Dashboard node federation.
/// Implementations resolve the set of peer messaging nodes (e.g., from Consul or Kubernetes)
/// so the dashboard can proxy API requests to remote nodes.
/// </summary>
public interface INodeDiscoveryProvider
{
    /// <summary>Returns all registered messaging nodes, optionally scoped to a namespace.</summary>
    Task<IList<Node>> GetNodesAsync(string? ns = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the node registered under <paramref name="nodeName"/>, or <see langword="null"/> if not found.
    /// </summary>
    Task<Node?> GetNodeAsync(string nodeName, string? ns = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Registers the current node with the discovery backend so that peer dashboards can find it.
    /// The default implementation throws <see cref="NotSupportedException"/>; override when the
    /// backend supports self-registration.
    /// </summary>
    Task RegisterNodeAsync(CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    /// <summary>
    /// Returns the list of available namespaces. Returns an empty list by default.
    /// Useful for multi-namespace Kubernetes deployments.
    /// </summary>
    Task<List<string>> GetNamespacesAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new List<string>());
    }

    /// <summary>
    /// Returns all messaging services/nodes within the given namespace.
    /// The default implementation throws <see cref="NotSupportedException"/>.
    /// </summary>
    Task<IList<Node>> ListServicesAsync(string? ns = null, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }
}
