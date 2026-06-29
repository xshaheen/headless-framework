// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
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
    /// <summary>
    /// Streams best-effort membership observations until <paramref name="cancellationToken"/> is cancelled.
    /// </summary>
    /// <remarks>
    /// Yields the following <see cref="NodeMembershipEvent"/> types: <see cref="NodeJoined"/>,
    /// <see cref="NodeSuspected"/>, <see cref="NodeRecovered"/>, <see cref="NodeLeft"/>, and the local-only
    /// <see cref="LocalMembershipLost"/>. The no-op implementation emits none and simply blocks until cancelled.
    /// Consumers MUST dispose the returned async enumerator (or cancel <paramref name="cancellationToken"/>) to
    /// release the subscription; abandoning it without disposal leaks the underlying buffer until the next publish
    /// prunes it.
    /// </remarks>
    IAsyncEnumerable<NodeMembershipEvent> WatchAsync(CancellationToken cancellationToken = default);
}
