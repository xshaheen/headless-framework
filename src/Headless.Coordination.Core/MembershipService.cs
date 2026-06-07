// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Headless.Coordination;

[PublicAPI]
public sealed class MembershipService(
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

        await store.UpsertDescriptorAsync(descriptor, cancellationToken).ConfigureAwait(false);
        Identity = identity;

        await HeartbeatAsync(cancellationToken).ConfigureAwait(false);

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
            _SignalLocalMembershipLost(identity);
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

        var snapshots = await GetLivenessSnapshotAsync(cancellationToken).ConfigureAwait(false);

        return snapshots.Any(snapshot => snapshot.Identity == identity && snapshot.State == NodeLivenessState.Alive);
    }

    public async ValueTask<IReadOnlyList<NodeIdentity>> GetLiveNodesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var snapshots = await GetLivenessSnapshotAsync(cancellationToken).ConfigureAwait(false);

        return snapshots
            .Where(static snapshot => snapshot.State == NodeLivenessState.Alive)
            .Select(static snapshot => snapshot.Identity)
            .OrderBy(static identity => identity.ToString(), StringComparer.Ordinal)
            .ToArray();
    }

    public async ValueTask<IReadOnlyList<NodeLivenessSnapshot>> GetLivenessSnapshotAsync(
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var snapshots = await store.ReadLivenessAsync(cancellationToken).ConfigureAwait(false);

        return snapshots.OrderBy(static snapshot => snapshot.Identity.ToString(), StringComparer.Ordinal).ToArray();
    }

    public IAsyncEnumerable<NodeMembershipEvent> WatchAsync(CancellationToken cancellationToken = default)
    {
        return eventSource.WatchAsync(cancellationToken);
    }

    private void _SignalLocalMembershipLost(NodeIdentity identity)
    {
        if (Interlocked.Exchange(ref _membershipLost, 1) != 0)
        {
            return;
        }

        _localMembershipLost.Cancel();
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
