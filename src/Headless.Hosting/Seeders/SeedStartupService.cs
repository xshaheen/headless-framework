// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.Initialization.Schema;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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
/// Warns once at startup when seeders are registered but nothing runs them: a skipped seed fails silently, unlike a
/// missing table, so the log is the only signal. It reads the registrations and constructs no seeder.
/// </summary>
internal sealed partial class SeedingConfigurationCheck(
    IEnumerable<SeederRegistration> registrations,
    IOptions<SeedingOptions> options,
    ILogger<SeedingConfigurationCheck> logger
) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (options.Value is { OnStartup: false, DuringDeploy: false })
        {
            var names = registrations.Select(static r => r.SeederType.Name).ToList();

            if (names.Count > 0)
            {
                LogSeedingNotConfigured(logger, string.Join(", ", names));
            }
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(
        EventId = 7,
        EventName = "SeedingNotConfigured",
        Level = LogLevel.Warning,
        Message = "Seeders {Seeders} are registered but nothing runs them, so code-declared data (definitions, cron jobs) "
            + "is not written or updated. Run the 'seed' verb in the deployment and call AddHeadlessSeedDuringDeploy(), or "
            + "call AddHeadlessSeedOnStartup() in development."
    )]
    private static partial void LogSeedingNotConfigured(ILogger logger, string seeders);
}

/// <summary>How the application has said its seeders run.</summary>
internal sealed class SeedingOptions
{
    public bool OnStartup { get; set; }

    public bool DuringDeploy { get; set; }
}
