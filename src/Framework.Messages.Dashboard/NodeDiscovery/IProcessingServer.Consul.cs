// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Messages.Internal;

namespace Framework.Messages.NodeDiscovery;

internal class ConsulProcessingNodeServer(INodeDiscoveryProvider discoveryProvider) : IProcessingServer
{
    public async ValueTask StartAsync(CancellationToken stoppingToken)
    {
        await discoveryProvider.RegisterNode(stoppingToken);
    }

    public void Dispose() { }
}
