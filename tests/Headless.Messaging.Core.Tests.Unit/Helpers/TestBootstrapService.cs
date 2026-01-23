using Headless.Messaging;
using Microsoft.Extensions.Hosting;

namespace Tests.Helpers;

public sealed class TestBootstrapService(IBootstrapper bootstrapper) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await bootstrapper.BootstrapAsync();
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
