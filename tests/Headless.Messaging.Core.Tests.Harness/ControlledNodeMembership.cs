// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Headless.Coordination;

namespace Tests;

/// <summary>
/// The single controllable <see cref="INodeMembership"/> double for messaging harness tests. It exposes a
/// settable local <see cref="Identity"/> (which stamps the owner column when a row is leased), a settable
/// liveness snapshot, and an injectable <see cref="NodeLeft"/> event source — so storage-provider conformance
/// tests can stamp an owner and drive <c>DeadOwnerRecoveryBridge</c> end-to-end conformance tests can plant
/// <c>Dead</c>/<c>Suspected</c>/<c>Alive</c> states and push membership events the watch and reconcile loops
/// react to. A test can seed a row owned by one identity and then reclaim it as another.
/// </summary>
[PublicAPI]
public sealed class ControlledNodeMembership : INodeMembership
{
    private readonly Lock _gate = new();
    private readonly Channel<NodeMembershipEvent> _events = Channel.CreateUnbounded<NodeMembershipEvent>(
        new UnboundedChannelOptions { SingleReader = false, SingleWriter = false }
    );

    private IReadOnlyList<NodeLivenessSnapshot> _snapshot = [];

    public NodeIdentity? Identity { get; private set; }

    public CancellationToken LocalMembershipLostToken => CancellationToken.None;

    /// <summary>Sets the local identity used to stamp <c>Owner</c> when leasing a row.</summary>
    public NodeIdentity SetIdentity(string nodeId, long incarnation = 1)
    {
        var identity = new NodeIdentity(new NodeId(nodeId), new NodeIncarnation(incarnation));
        Identity = identity;
        return identity;
    }

    /// <summary>Replaces the authoritative liveness snapshot the reconcile loop reads.</summary>
    public void SetSnapshot(params NodeLivenessSnapshot[] snapshot)
    {
        lock (_gate)
        {
            _snapshot = snapshot;
        }
    }

    /// <summary>Builds a snapshot entry for <paramref name="identity"/> in the given <paramref name="state"/>.</summary>
    public static NodeLivenessSnapshot Snapshot(NodeIdentity identity, NodeLivenessState state)
    {
        return new(identity, state, Role: null, Metadata: new Dictionary<string, string>(StringComparer.Ordinal));
    }

    /// <summary>Pushes a <see cref="NodeLeft"/> event onto the watch stream (best-effort acceleration path).</summary>
    public void EmitNodeLeft(NodeIdentity identity)
    {
        _events.Writer.TryWrite(new NodeLeft(identity));
    }

    /// <summary>Pushes any membership event onto the watch stream.</summary>
    public void Emit(NodeMembershipEvent membershipEvent)
    {
        _events.Writer.TryWrite(membershipEvent);
    }

    public ValueTask<NodeIdentity> RegisterAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Identity ?? SetIdentity("local-node"));
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

        lock (_gate)
        {
            var alive = _snapshot.Any(node => node.Identity == identity && node.State == NodeLivenessState.Alive);
            return ValueTask.FromResult(alive);
        }
    }

    public ValueTask<IReadOnlyList<NodeIdentity>> GetLiveNodesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            IReadOnlyList<NodeIdentity> live =
            [
                .. _snapshot.Where(node => node.State == NodeLivenessState.Alive).Select(node => node.Identity),
            ];
            return ValueTask.FromResult(live);
        }
    }

    public ValueTask<IReadOnlyList<NodeLivenessSnapshot>> GetLivenessSnapshotAsync(
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            return ValueTask.FromResult(_snapshot);
        }
    }

    public async IAsyncEnumerable<NodeMembershipEvent> WatchAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        // ReadAllAsync throws OperationCanceledException on cancellation, which the bridge's watch loop treats
        // as the expected host-stop signal.
        await foreach (var membershipEvent in _events.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return membershipEvent;
        }
    }
}
