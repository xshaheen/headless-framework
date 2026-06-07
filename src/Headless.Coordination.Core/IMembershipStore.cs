// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Coordination;

/// <summary>Provider SPI for store-authoritative membership operations.</summary>
public interface IMembershipStore
{
    ValueTask<NodeIncarnation> AllocateIncarnationAsync(NodeId nodeId, CancellationToken cancellationToken = default);

    ValueTask UpsertDescriptorAsync(NodeDescriptor descriptor, CancellationToken cancellationToken = default);

    ValueTask<bool> HeartbeatAsync(NodeIdentity identity, CancellationToken cancellationToken = default);

    ValueTask LeaveAsync(NodeIdentity identity, CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<NodeLivenessSnapshot>> ReadLivenessAsync(CancellationToken cancellationToken = default);
}
