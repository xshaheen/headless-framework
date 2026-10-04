// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Coordination;

/// <summary>Streams best-effort local membership observations.</summary>
/// <remarks>
/// Events accelerate recovery; they are not the authoritative recovery path. Consumers must reconcile
/// from <see cref="INodeMembership.GetLiveNodesAsync"/> or <see cref="INodeMembership.GetLivenessSnapshotAsync"/>
/// and ensure recovery is idempotent.
/// </remarks>
[PublicAPI]
public interface IMembershipEventSource
{
    /// <summary>
    /// Streams best-effort membership observations until the cancellation token triggers.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>An asynchronous stream of membership events.</returns>
    /// <remarks>
    /// Yields <see cref="NodeJoined"/>, <see cref="NodeSuspected"/>, <see cref="NodeRecovered"/>, <see cref="NodeLeft"/>,
    /// and <see cref="LocalMembershipLost"/> events. The no-op implementation emits no events and blocks until cancelled.
    /// Dispose the returned async enumerator or cancel <paramref name="cancellationToken"/> to release the subscription.
    /// Abandoning the enumerator without disposal retains the underlying buffer until the next publish cycle prunes it.
    /// </remarks>
    IAsyncEnumerable<NodeMembershipEvent> WatchAsync(CancellationToken cancellationToken = default);
}
