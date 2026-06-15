// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Headless.Coordination;

internal sealed class MembershipService(
    IMembershipStore store,
    INodeIdProvider nodeIdProvider,
    CoordinationOptions options,
    MembershipEventSource eventSource,
    IHostApplicationLifetime? hostApplicationLifetime,
    ILogger<MembershipService> logger
) : INodeMembership, IDisposable
{
    private readonly CancellationTokenSource _localMembershipLost = new();
    private int _membershipLost;

    public NodeIdentity? Identity { get; private set; }

    public CancellationToken LocalMembershipLostToken => _localMembershipLost.Token;

    public async ValueTask<NodeIdentity> RegisterAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var nodeId = await nodeIdProvider.GetNodeIdAsync(cancellationToken).ConfigureAwait(false);
        var incarnation = await store.AllocateIncarnationAsync(nodeId, cancellationToken).ConfigureAwait(false);
        var identity = new NodeIdentity(nodeId, incarnation);
        var descriptor = new NodeDescriptor
        {
            Identity = identity,
            HostName = Environment.MachineName,
            Role = options.Role,
            Metadata = options.Metadata,
        };

        // UpsertDescriptorAsync durably establishes both the cold descriptor and the initial guarded
        // liveness row, so registration writes once. The heartbeat loop owns every subsequent beat.
        await store.UpsertDescriptorAsync(descriptor, cancellationToken).ConfigureAwait(false);
        Identity = identity;

        return identity;
    }

    public async ValueTask<bool> HeartbeatAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (Identity is not { } identity)
        {
            return false;
        }

        var accepted = await store.HeartbeatAsync(identity, cancellationToken).ConfigureAwait(false);

        if (!accepted)
        {
            // Clear the local identity so the heartbeat guard stops re-issuing beats for a lost membership.
            Identity = null;
            await _SignalLocalMembershipLostAsync(identity).ConfigureAwait(false);
        }

        return accepted;
    }

    public async ValueTask LeaveAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (Identity is not { } identity)
        {
            return;
        }

        await store.LeaveAsync(identity, cancellationToken).ConfigureAwait(false);
        Identity = null;
    }

    public async ValueTask<bool> IsAliveAsync(NodeIdentity identity, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Targeted single-node read instead of a full cluster snapshot + filter: a node is alive only when its
        // current-generation liveness classifies as Alive. Absent (null) and any non-Alive state map to false,
        // exactly matching the prior snapshot-based predicate.
        var state = await store.ReadNodeLivenessAsync(identity, cancellationToken).ConfigureAwait(false);

        return state is NodeLivenessState.Alive;
    }

    public async ValueTask<IReadOnlyList<NodeIdentity>> GetLiveNodesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var snapshots = await GetLivenessSnapshotAsync(cancellationToken).ConfigureAwait(false);

        return snapshots
            .Where(static snapshot => snapshot.State == NodeLivenessState.Alive)
            .Select(static snapshot => snapshot.Identity)
            .ToArray();
    }

    public async ValueTask<IReadOnlyList<NodeLivenessSnapshot>> GetLivenessSnapshotAsync(
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        // The store SPI returns snapshots already sorted by identity; no service-side re-sort is needed.
        return await store.ReadLivenessAsync(cancellationToken).ConfigureAwait(false);
    }

    public IAsyncEnumerable<NodeMembershipEvent> WatchAsync(CancellationToken cancellationToken = default)
    {
        return eventSource.WatchAsync(cancellationToken);
    }

    private async ValueTask _SignalLocalMembershipLostAsync(NodeIdentity identity)
    {
        if (Interlocked.Exchange(ref _membershipLost, 1) != 0)
        {
            return;
        }

        try
        {
            await _localMembershipLost.CancelAsync().ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // Defensive: Dispose() can race this at-most-once path during shutdown. The Interlocked guard
            // above still ensures we proceed once; a disposed CTS just means no token observers remain.
        }

        eventSource.Publish(new LocalMembershipLost(identity));
        logger.LocalMembershipLost(identity);

        if (options.MembershipLostBehavior != MembershipLostBehavior.StopApplication)
        {
            return;
        }

        try
        {
            hostApplicationLifetime?.StopApplication();
        }
        catch (Exception ex)
        {
            logger.StopApplicationFailed(ex, identity);
        }
    }

    public void Dispose()
    {
        _localMembershipLost.Dispose();
    }
}
