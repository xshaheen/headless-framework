// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Dashboard;

internal sealed class ConsulProcessingNodeServer(INodeDiscoveryProvider discoveryProvider) : IProcessingServer
{
    public async ValueTask StartAsync(CancellationToken stoppingToken)
    {
        await discoveryProvider.RegisterNodeAsync(stoppingToken).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}
