// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;

namespace Tests;

internal sealed class FakeMembershipStore : IMembershipStore
{
    private readonly Queue<IReadOnlyList<NodeLivenessSnapshot>> _snapshots = new();

    public NodeIncarnation NextIncarnation { get; set; } = new(1);

    public bool HeartbeatAccepted { get; set; } = true;

    public bool ThrowOnRegister { get; set; }

    public bool ThrowOnRead { get; set; }

    public bool ThrowOnReadNodeLiveness { get; set; }

    public bool ThrowOnLeave { get; set; }

    public bool BlockOnLeave { get; set; }

    public int AllocateIncarnationCalls { get; private set; }

    public int ReadLivenessCalls { get; private set; }

    public int ReadNodeLivenessCalls { get; private set; }

    public int ReadLiveNodesCalls { get; private set; }

    public Dictionary<NodeIdentity, NodeLivenessState?> NodeStates { get; } = [];

    public List<NodeDescriptor> Descriptors { get; } = [];

    public List<NodeIdentity> Heartbeats { get; } = [];

    public List<NodeIdentity> Leaves { get; } = [];

    public ValueTask<NodeIncarnation> AllocateIncarnationAsync(
        NodeId nodeId,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        AllocateIncarnationCalls++;

        if (ThrowOnRegister)
        {
            throw new InvalidOperationException("registration unavailable");
        }

        return ValueTask.FromResult(NextIncarnation);
    }

    public ValueTask UpsertDescriptorAsync(NodeDescriptor descriptor, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Descriptors.Add(descriptor);

        return ValueTask.CompletedTask;
    }

    public ValueTask<bool> HeartbeatAsync(NodeIdentity identity, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Heartbeats.Add(identity);

        return ValueTask.FromResult(HeartbeatAccepted);
    }

    public async ValueTask LeaveAsync(NodeIdentity identity, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (ThrowOnLeave)
        {
            throw new InvalidOperationException("leave unavailable");
        }

        if (BlockOnLeave)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
        }

        Leaves.Add(identity);
    }

    public ValueTask<IReadOnlyList<NodeLivenessSnapshot>> ReadLivenessAsync(
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        ReadLivenessCalls++;

        if (ThrowOnRead)
        {
            throw new InvalidOperationException("store unavailable");
        }

        // Mirror the real store SPI contract: snapshots are returned sorted by identity.
        IReadOnlyList<NodeLivenessSnapshot> snapshots =
            _snapshots.Count == 0
                ? []
                : _snapshots
                    .Dequeue()
                    .OrderBy(static snapshot => snapshot.Identity.ToString(), StringComparer.Ordinal)
                    .ToArray();

        return ValueTask.FromResult(snapshots);
    }

    public ValueTask<NodeLivenessState?> ReadNodeLivenessAsync(
        NodeIdentity identity,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        ReadNodeLivenessCalls++;

        if (ThrowOnReadNodeLiveness)
        {
            throw new InvalidOperationException("store unavailable");
        }

        // Absent identities (never configured) resolve to null, matching the SPI's absent contract.
        return ValueTask.FromResult(NodeStates.GetValueOrDefault(identity));
    }

    public ValueTask<IReadOnlyList<NodeIdentity>> ReadLiveNodesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ReadLiveNodesCalls++;

        if (ThrowOnRead)
        {
            throw new InvalidOperationException("store unavailable");
        }

        // Derive the Alive set from the next enqueued snapshot, ordered by identity — mirroring the real store
        // SPI, where ReadLiveNodesAsync equals filtering ReadLivenessAsync to Alive and ordering by identity.
        IReadOnlyList<NodeIdentity> live =
            _snapshots.Count == 0
                ? []
                : _snapshots
                    .Dequeue()
                    .Where(static snapshot => snapshot.State == NodeLivenessState.Alive)
                    .Select(static snapshot => snapshot.Identity)
                    .OrderBy(static identity => identity.ToString(), StringComparer.Ordinal)
                    .ToArray();

        return ValueTask.FromResult(live);
    }

    public void EnqueueSnapshot(params NodeLivenessSnapshot[] snapshots)
    {
        _snapshots.Enqueue(snapshots);
    }
}
