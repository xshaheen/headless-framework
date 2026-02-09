// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Headless.Messaging.Scheduling;

/// <summary>
/// Logs a startup warning when scheduled job definitions are registered but no
/// <see cref="IScheduledJobStorage"/> provider is configured.
/// </summary>
internal sealed class SchedulerStorageMissingWarningService(
    ScheduledJobDefinitionRegistry definitionRegistry,
    ILogger<SchedulerStorageMissingWarningService> logger
) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var definitions = definitionRegistry.GetAll();

        if (definitions.Count > 0)
        {
            logger.LogWarning(
                "{Count} scheduled job definition(s) registered but no IScheduledJobStorage provider is configured — "
                    + "scheduled jobs will not run. Register a storage provider (e.g., UseSqlServer, UsePostgreSql) to enable scheduling",
                definitions.Count
            );
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
