// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.CompilerServices;

namespace Headless.Coordination;

/// <summary>
/// Provides a no-op <see cref="INodeMembership"/> implementation when no coordination provider is registered.
/// </summary>
/// <remarks>
/// All liveness queries return empty collections or <see langword="false"/>.
/// <see cref="INodeMembership.HeartbeatAsync"/> returns <see langword="false"/>.
/// <see cref="IMembershipEventSource.WatchAsync"/> emits no events and blocks until cancelled.
/// <see cref="INodeMembership.LocalMembershipLostToken"/> is never cancelled.
/// </remarks>
[PublicAPI]
public sealed class NullNodeMembership : INodeMembership
{
    private static readonly NodeIdentity _NullIdentity = new(new NodeId("null"), new NodeIncarnation(1));

    /// <inheritdoc/>
    public NodeIdentity? Identity { get; private set; }

    /// <summary>Gets a cancellation token that is never cancelled.</summary>
    public CancellationToken LocalMembershipLostToken => CancellationToken.None;

    /// <summary>
    /// Sets <see cref="Identity"/> to a fixed sentinel value and returns it. Repeated calls return the same identity.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The sentinel node identity.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public ValueTask<NodeIdentity> RegisterAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Identity ??= _NullIdentity;

        return ValueTask.FromResult(Identity.Value);
    }

    /// <summary>Returns <see langword="false"/> because no backing store is present.</summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns><see langword="false"/>.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public ValueTask<bool> HeartbeatAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return ValueTask.FromResult(false);
    }

    /// <summary>Clears <see cref="Identity"/>.</summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A value task that represents the asynchronous operation.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public ValueTask LeaveAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Identity = null;

        return ValueTask.CompletedTask;
    }

    /// <summary>Returns <see langword="false"/> because no nodes are tracked.</summary>
    /// <param name="identity">The node identity to check.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns><see langword="false"/>.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public ValueTask<bool> IsAliveAsync(NodeIdentity identity, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return ValueTask.FromResult(false);
    }

    /// <summary>Returns an empty list because no nodes are tracked.</summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>An empty list of live identities.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public ValueTask<IReadOnlyList<NodeIdentity>> GetLiveNodesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return ValueTask.FromResult<IReadOnlyList<NodeIdentity>>([]);
    }

    /// <summary>Returns an empty list because no nodes are tracked.</summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>An empty list of liveness snapshots.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public ValueTask<IReadOnlyList<NodeLivenessSnapshot>> GetLivenessSnapshotAsync(
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        return ValueTask.FromResult<IReadOnlyList<NodeLivenessSnapshot>>([]);
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<NodeMembershipEvent> WatchAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        // The null provider never emits events; block until cancellation without allocating a timer.
        await new TaskCompletionSource().Task.WaitAsync(cancellationToken).ConfigureAwait(false);

        yield break;
    }
}
