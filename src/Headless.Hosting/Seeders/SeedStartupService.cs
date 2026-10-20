// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Headless.Hosting;

/// <summary>
/// Runs every seeder in <see cref="IHostedLifecycleService.StartingAsync"/>, after the resources are provisioned and
/// verified and before any hosted service's <c>StartAsync</c>, so a scheduler or a consumer starts on seeded data.
/// </summary>
internal sealed class SeedStartupService(
    IServiceProvider services,
    IOptions<DeployStepsOnStartupOptions> options,
    ResourceProvisioningStartupService? resourceSetup = null
) : IHostedLifecycleService
{
    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.Seed)
        {
            return;
        }

        if (resourceSetup is not null)
        {
            // Seeders write into the tables the provisioning creates; a concurrently started host runs both at once.
            await resourceSetup.WaitForInitializationAsync(cancellationToken).ConfigureAwait(false);
        }

        // The host's own startup check may run after this service, so run it here too: a missing table then fails with
        // the same ResourceProvisioningException that names 'resources provision', not with a seeder's raw database
        // error. Cheap on a warm database: one history read per schema.
        await ResourceStartupChecks
            .VerifyAsync(services, ResourceStartupCheck.FailStart, cancellationToken)
            .ConfigureAwait(false);

        await using var scope = services.CreateAsyncScope();
        var outcomes = await SeedRunner
            .RunAsync(SeedRunner.Ordered(scope.ServiceProvider), timeout: null, cancellationToken)
            .ConfigureAwait(false);
        SeedRunner.ThrowIfFailed(outcomes);
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
