// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Dashboard;

namespace Tests.Forwarding;

/// <summary>A discovery provider that lists the entries a test sets.</summary>
internal sealed class StaticDiscovery : INodeDiscoveryProvider
{
    private IList<Node> _nodes = [];

    public void Set(params Node[] nodes) => Volatile.Write(ref _nodes, nodes);

    public Task<IList<Node>> GetNodesAsync(string? ns = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(Volatile.Read(ref _nodes));

    public Task<Node?> GetNodeAsync(
        string nodeName,
        string? ns = null,
        CancellationToken cancellationToken = default
    ) =>
        Task.FromResult(
            Volatile
                .Read(ref _nodes)
                .FirstOrDefault(node => string.Equals(node.Name, nodeName, StringComparison.Ordinal))
        );
}
