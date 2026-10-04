// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Coordination;

/// <summary>Provides the stable node identifier used before allocating a new incarnation.</summary>
/// <remarks>
/// Implementations should return a value that is unique among concurrently running processes in the same cluster.
/// In Kubernetes, pod name and namespace provide stability for Deployments, and pod names provide ordinal identities
/// for StatefulSets. Generated identifiers are suitable for local development only.
/// </remarks>
[PublicAPI]
public interface INodeIdProvider
{
    /// <summary>Returns the stable <see cref="NodeId"/> for this process.</summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A <see cref="NodeId"/> that is unique within the coordination cluster.</returns>
    ValueTask<NodeId> GetNodeIdAsync(CancellationToken cancellationToken = default);
}
