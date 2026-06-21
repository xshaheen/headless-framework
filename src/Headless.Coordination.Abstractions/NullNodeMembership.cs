// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.CompilerServices;

namespace Headless.Coordination;

/// <summary>
/// No-op <see cref="INodeMembership"/> implementation used when no coordination provider is registered.
/// </summary>
/// <remarks>
/// All liveness queries return empty or <see langword="false"/>; <see cref="INodeMembership.HeartbeatAsync"/>
/// always returns <see langword="false"/>; <see cref="IMembershipEventSource.WatchAsync"/> never emits
/// events and blocks until the cancellation token is cancelled. <see cref="INodeMembership.LocalMembershipLostToken"/>
/// is never cancelled. This implementation is useful as a safe fallback when coordination is optional.
/// </remarks>
[PublicAPI]
public sealed class NullNodeMembership : INodeMembership
{
    private static readonly NodeIdentity _NullIdentity = new(new NodeId("null"), new NodeIncarnation(1));

    /// <inheritdoc/>
    public NodeIdentity? Identity { get; private set; }

    /// <summary>Always <see cref="CancellationToken.None"/>; this implementation never loses membership.</summary>
    public CancellationToken LocalMembershipLostToken => CancellationToken.None;

    /// <summary>
    /// Sets <see cref="Identity"/> to a fixed sentinel value and returns it. Idempotent — repeated calls
    /// return the same identity.
    /// </summary>
    /// <exception cref="OperationCanceledException">
    /// Thrown when <paramref name="cancellationToken"/> is already cancelled before the call.
    /// </exception>
    public ValueTask<NodeIdentity> RegisterAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Identity ??= _NullIdentity;

        return ValueTask.FromResult(Identity.Value);
    }

    /// <summary>Always returns <see langword="false"/>; no backing store to heartbeat.</summary>
    /// <exception cref="OperationCanceledException">
    /// Thrown when <paramref name="cancellationToken"/> is already cancelled before the call.
    /// </exception>
    public ValueTask<bool> HeartbeatAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return ValueTask.FromResult(false);
    }

    /// <summary>Clears <see cref="Identity"/>.</summary>
    /// <exception cref="OperationCanceledException">
    /// Thrown when <paramref name="cancellationToken"/> is already cancelled before the call.
    /// </exception>
    public ValueTask LeaveAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Identity = null;

        return ValueTask.CompletedTask;
    }

    /// <summary>Always returns <see langword="false"/>; no nodes are tracked.</summary>
    /// <exception cref="OperationCanceledException">
    /// Thrown when <paramref name="cancellationToken"/> is already cancelled before the call.
    /// </exception>
    public ValueTask<bool> IsAliveAsync(NodeIdentity identity, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return ValueTask.FromResult(false);
    }

    /// <summary>Always returns an empty list; no nodes are tracked.</summary>
    /// <exception cref="OperationCanceledException">
    /// Thrown when <paramref name="cancellationToken"/> is already cancelled before the call.
    /// </exception>
    public ValueTask<IReadOnlyList<NodeIdentity>> GetLiveNodesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return ValueTask.FromResult<IReadOnlyList<NodeIdentity>>([]);
    }

    /// <summary>Always returns an empty list; no nodes are tracked.</summary>
    /// <exception cref="OperationCanceledException">
    /// Thrown when <paramref name="cancellationToken"/> is already cancelled before the call.
    /// </exception>
    public ValueTask<IReadOnlyList<NodeLivenessSnapshot>> GetLivenessSnapshotAsync(
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        return ValueTask.FromResult<IReadOnlyList<NodeLivenessSnapshot>>([]);
    }

    public async IAsyncEnumerable<NodeMembershipEvent> WatchAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        // The null provider never emits events; block until cancellation without allocating a timer.
        await new TaskCompletionSource().Task.WaitAsync(cancellationToken).ConfigureAwait(false);

        yield break;
    }
}
