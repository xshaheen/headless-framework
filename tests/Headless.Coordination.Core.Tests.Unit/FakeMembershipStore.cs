// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;

namespace Tests;

internal sealed class FakeMembershipStore : IMembershipStore
{
    private readonly Queue<IReadOnlyList<NodeLivenessSnapshot>> _snapshots = new();

    public NodeIncarnation NextIncarnation { get; set; } = new(1);

    public bool HeartbeatAccepted { get; set; } = true;

    public bool ThrowOnRead { get; set; }

    public List<NodeDescriptor> Descriptors { get; } = [];

    public List<NodeIdentity> Heartbeats { get; } = [];

    public List<NodeIdentity> Leaves { get; } = [];

    public ValueTask<NodeIncarnation> AllocateIncarnationAsync(NodeId nodeId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

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

    public ValueTask LeaveAsync(NodeIdentity identity, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Leaves.Add(identity);

        return ValueTask.CompletedTask;
    }

    public ValueTask<IReadOnlyList<NodeLivenessSnapshot>> ReadLivenessAsync(
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (ThrowOnRead)
        {
            throw new InvalidOperationException("store unavailable");
        }

        // Mirror the real store SPI contract: snapshots are returned sorted by identity.
        IReadOnlyList<NodeLivenessSnapshot> snapshots =
            _snapshots.Count == 0
                ? Array.Empty<NodeLivenessSnapshot>()
                : _snapshots
                    .Dequeue()
                    .OrderBy(static snapshot => snapshot.Identity.ToString(), StringComparer.Ordinal)
                    .ToArray();

        return ValueTask.FromResult(snapshots);
    }

    public void EnqueueSnapshot(params NodeLivenessSnapshot[] snapshots)
    {
        _snapshots.Enqueue(snapshots);
    }
}
