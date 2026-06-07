// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Coordination;

/// <summary>Streams best-effort local membership observations.</summary>
/// <remarks>
/// Events accelerate recovery; they are not the authoritative recovery path. Consumers must still
/// reconcile from <see cref="INodeMembership.GetLiveNodesAsync"/> or
/// <see cref="INodeMembership.GetLivenessSnapshotAsync"/> and make recovery idempotent.
/// </remarks>
[PublicAPI]
public interface IMembershipEventSource
{
    IAsyncEnumerable<NodeMembershipEvent> WatchAsync(CancellationToken cancellationToken = default);
}
