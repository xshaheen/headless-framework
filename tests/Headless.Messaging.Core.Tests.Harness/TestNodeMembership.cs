// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;

namespace Tests;

[PublicAPI]
public sealed class TestNodeMembership(
    NodeIdentity? identity,
    IReadOnlyList<NodeIdentity> liveNodes,
    Exception? getLiveNodesException = null
) : INodeMembership
{
    public static TestNodeMembership Active(string nodeId, long incarnation, params NodeIdentity[] additionalLiveNodes)
    {
        var identity = new NodeIdentity(new NodeId(nodeId), new NodeIncarnation(incarnation));
        return new TestNodeMembership(identity, [identity, .. additionalLiveNodes]);
    }

    public static TestNodeMembership ThrowingGetLiveNodes(string nodeId, long incarnation, Exception exception)
    {
        var identity = new NodeIdentity(new NodeId(nodeId), new NodeIncarnation(incarnation));
        return new TestNodeMembership(identity, [identity], exception);
    }

    public NodeIdentity? Identity { get; } = identity;

    public int GetLiveNodesCallCount { get; private set; }

    public CancellationToken LocalMembershipLostToken => CancellationToken.None;

    public ValueTask<NodeIdentity> RegisterAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Identity!.Value);
    }

    public ValueTask<bool> HeartbeatAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Identity is not null);
    }

    public ValueTask LeaveAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }

    public ValueTask<bool> IsAliveAsync(NodeIdentity identity, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(liveNodes.Contains(identity));
    }

    public ValueTask<IReadOnlyList<NodeIdentity>> GetLiveNodesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        GetLiveNodesCallCount++;

        return getLiveNodesException is not null
            ? ValueTask.FromException<IReadOnlyList<NodeIdentity>>(getLiveNodesException)
            : ValueTask.FromResult(liveNodes);
    }

    public ValueTask<IReadOnlyList<NodeLivenessSnapshot>> GetLivenessSnapshotAsync(
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<IReadOnlyList<NodeLivenessSnapshot>>([]);
    }

    public async IAsyncEnumerable<NodeMembershipEvent> WatchAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        await Task.CompletedTask;
        cancellationToken.ThrowIfCancellationRequested();

        yield break;
    }
}
