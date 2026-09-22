// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;

namespace Headless.Coordination;

/// <summary>
/// The node id is the host name the framework resolved for this process (<see cref="IHostIdentityAccessor.HostName"/>),
/// so the node a membership store sees is the same host that stamps its origin on messages and logs. Override it
/// through <c>HostIdentityOptions.HostName</c>, never per subsystem.
/// </summary>
internal sealed class DefaultNodeIdProvider(IHostIdentityAccessor hostIdentity) : INodeIdProvider
{
    public ValueTask<NodeId> GetNodeIdAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return ValueTask.FromResult(new NodeId(hostIdentity.HostName));
    }
}
