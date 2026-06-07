// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Coordination;

/// <summary>Chooses the stable node id used before the store allocates a new incarnation.</summary>
/// <remarks>
/// Implementations should return a value that is unique among concurrently-running processes in the same
/// cluster. In Kubernetes, pod name plus namespace is a good default for Deployments, while StatefulSet
/// pod names provide stable ordinal identities. Generated ids are suitable for local development only.
/// </remarks>
[PublicAPI]
public interface INodeIdProvider
{
    ValueTask<NodeId> GetNodeIdAsync(CancellationToken cancellationToken = default);
}
