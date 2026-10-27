// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Headless.Messaging.InMemory;

/// <summary>
/// Warns at host start when the in-memory transport runs outside Development and Test, where it is most likely a
/// missing broker configuration rather than a choice.
/// </summary>
/// <remarks>
/// A warning, not a startup failure: a single-process deployment can choose this transport on purpose, and the
/// environment name alone cannot tell that apart from a forgotten broker. The warning names the cost so the choice is
/// visible in the logs: messages in flight, including those an outbox already handed over, are lost on restart.
/// </remarks>
internal sealed class InMemoryTransportEnvironmentWarning(
    IHostEnvironment environment,
    ILogger<InMemoryTransportEnvironmentWarning> logger
) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopmentOrTest())
        {
            logger.InMemoryTransportOutsideDevelopment(environment.EnvironmentName);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

internal static partial class InMemoryTransportEnvironmentWarningLog
{
    [LoggerMessage(
        EventId = 3013,
        EventName = "InMemoryTransportOutsideDevelopment",
        Level = LogLevel.Warning,
        Message = "The in-memory messaging transport is running in the {EnvironmentName} environment. It keeps no "
            + "state across a restart and never reaches a broker, so every message in flight is lost when the process "
            + "stops. Configure a broker transport unless this single-process deployment accepts that loss."
    )]
    internal static partial void InMemoryTransportOutsideDevelopment(this ILogger logger, string environmentName);
}
