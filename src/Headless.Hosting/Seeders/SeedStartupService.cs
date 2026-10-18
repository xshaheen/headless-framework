// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.Initialization.Schema;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Headless.Hosting;

/// <summary>
/// Runs every seeder in <see cref="IHostedLifecycleService.StartingAsync"/>, after the stateful resources are set up
/// and verified and before any hosted service's <c>StartAsync</c>, so a scheduler or a consumer starts on seeded data.
/// </summary>
internal sealed class SeedStartupService(
    IServiceProvider services,
    SchemaRunner? schemaRunner = null,
    StatefulResourceStartupService? resourceSetup = null
) : IHostedLifecycleService
{
    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        if (resourceSetup is not null)
        {
            // Seeders write into the tables the setup creates; a concurrently started host runs both at once.
            await resourceSetup.WaitForInitializationAsync(cancellationToken).ConfigureAwait(false);
        }

        if (schemaRunner is not null)
        {
            // The host's own verification may start after this service, so check here too: a missing table then fails
            // with the message that names 'resources setup', not with a seeder's raw database error. Cheap on a warm
            // database: one history read per schema.
            await schemaRunner.RunAsync(SchemaRunnerMode.Verify, cancellationToken).ConfigureAwait(false);
        }

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

/// <summary>
/// Fails the host start when seeders are registered but the application has not said what runs them: a skipped seed
/// leaves definitions and cron rows silently out of date, unlike a missing table, so it is refused before any hosted
/// service starts. It reads the registrations only, so it costs no I/O and constructs no seeder. It is a hosted service
/// rather than a startup validator because the host verbs run the validators too, and the <c>seed</c> and
/// <c>resources</c> verbs are the deployment steps this check asks for.
/// </summary>
internal sealed class SeedingConfigurationCheck(
    IEnumerable<SeederRegistration> registrations,
    IOptions<SeedingOptions> options
) : IHostedLifecycleService
{
    public Task StartingAsync(CancellationToken cancellationToken)
    {
        if (options.Value is { OnStartup: false, DuringDeploy: false })
        {
            var names = registrations.Select(static r => r.SeederType.Name).ToList();

            if (names.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Seeders {string.Join(", ", names)} are registered but nothing runs them, so code-declared data "
                        + "(definitions, cron jobs) would never be written. Run the 'seed' verb in the deployment and call "
                        + "AddHeadlessSeedDuringDeploy(), or call AddHeadlessSeedOnStartup() in development."
                );
            }
        }

        return Task.CompletedTask;
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>How the application has said its seeders run.</summary>
internal sealed class SeedingOptions
{
    public bool OnStartup { get; set; }

    public bool DuringDeploy { get; set; }
}
