// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Messages.Internal;

namespace DotNetCore.CAP.Dashboard.NodeDiscovery;

internal class ConsulProcessingNodeServer(INodeDiscoveryProvider discoveryProvider) : IProcessingServer
{
    public async ValueTask StartAsync(CancellationToken stoppingToken)
    {
        await discoveryProvider.RegisterNode(stoppingToken);
    }

    public void Dispose() { }
}
