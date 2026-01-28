// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Internal;

namespace Headless.Messaging.Dashboard.NodeDiscovery;

internal sealed class ConsulProcessingNodeServer(INodeDiscoveryProvider discoveryProvider) : IProcessingServer
{
    public async ValueTask StartAsync(CancellationToken stoppingToken)
    {
        await discoveryProvider.RegisterNode(stoppingToken);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
