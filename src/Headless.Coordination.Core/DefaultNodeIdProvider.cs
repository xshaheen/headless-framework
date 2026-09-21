// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;

namespace Headless.Coordination;

/// <summary>
/// Resolves the node id from <see cref="CoordinationOptions.ConfiguredNodeId"/>, falling back to the host name
/// the framework discovered for this process (<see cref="IHostIdentityAccessor.HostName"/>), so the node a
/// membership store sees is the same host that stamps its origin on messages and logs.
/// </summary>
internal sealed class DefaultNodeIdProvider(CoordinationOptions options, IHostIdentityAccessor hostIdentity)
    : INodeIdProvider
{
    public ValueTask<NodeId> GetNodeIdAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var value = string.IsNullOrWhiteSpace(options.ConfiguredNodeId)
            ? hostIdentity.HostName
            : options.ConfiguredNodeId;

        return ValueTask.FromResult(new NodeId(value));
    }
}
