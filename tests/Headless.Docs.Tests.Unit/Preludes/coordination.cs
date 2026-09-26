// Copyright (c) Mahmoud Shaheen. All rights reserved.

// Stand-ins for the custom membership store and the variables the coordination guide's examples assume.

global using static CoordinationAmbient;
// Provider SDK type the examples name; a consumer's IDE adds this using.
global using IConnectionMultiplexer = StackExchange.Redis.IConnectionMultiplexer;

public sealed class MyMembershipStore : IMembershipStore
{
    public ValueTask<NodeIncarnation> AllocateIncarnationAsync(
        NodeId nodeId,
        CancellationToken cancellationToken = default
    ) => throw new NotImplementedException();

    public ValueTask UpsertDescriptorAsync(NodeDescriptor descriptor, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public ValueTask<bool> HeartbeatAsync(NodeIdentity identity, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public ValueTask LeaveAsync(NodeIdentity identity, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public ValueTask<IReadOnlyList<NodeLivenessSnapshot>> ReadLivenessAsync(
        CancellationToken cancellationToken = default
    ) => throw new NotImplementedException();

    public ValueTask<NodeLivenessState?> ReadNodeLivenessAsync(
        NodeIdentity identity,
        CancellationToken cancellationToken = default
    ) => throw new NotImplementedException();

    public ValueTask<IReadOnlyList<NodeIdentity>> ReadLiveNodesAsync(CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
}

#pragma warning disable IDE1006 // Ambient members mirror the camelCase locals the examples use.
public static class CoordinationAmbient
{
    public static string connectionString => null!;

    public static IConnectionMultiplexer multiplexer => null!;
}
