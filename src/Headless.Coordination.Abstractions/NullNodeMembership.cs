// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.CompilerServices;

namespace Headless.Coordination;

/// <summary>No-op membership implementation used when no coordination provider is registered.</summary>
[PublicAPI]
public sealed class NullNodeMembership : INodeMembership
{
    private static readonly NodeIdentity _NullIdentity = new(new NodeId("null"), new NodeIncarnation(1));

    public NodeIdentity? Identity { get; private set; }

    public CancellationToken LocalMembershipLostToken => CancellationToken.None;

    public ValueTask<NodeIdentity> RegisterAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Identity ??= _NullIdentity;

        return ValueTask.FromResult(Identity.Value);
    }

    public ValueTask<bool> HeartbeatAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return ValueTask.FromResult(false);
    }

    public ValueTask LeaveAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Identity = null;

        return ValueTask.CompletedTask;
    }

    public ValueTask<bool> IsAliveAsync(NodeIdentity identity, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return ValueTask.FromResult(false);
    }

    public ValueTask<IReadOnlyList<NodeIdentity>> GetLiveNodesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return ValueTask.FromResult<IReadOnlyList<NodeIdentity>>([]);
    }

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
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);

        yield break;
    }
}
