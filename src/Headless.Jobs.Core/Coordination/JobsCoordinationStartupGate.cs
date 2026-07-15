// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Headless.Jobs.Coordination;

/// <summary>
/// Establishes the node's membership identity during host start so the durable scheduler stamps with a real
/// <c>node@incarnation</c> owner from its first acquisition.
/// </summary>
/// <remarks>
/// Runs in <see cref="IHostedLifecycleService.StartingAsync"/>, which executes before any
/// <see cref="IHostedService.StartAsync"/> — and, because the coordination provider is registered before the
/// durable Jobs store (enforced by the require-provider check), after the coordination schema initializer's
/// own <c>StartingAsync</c> has completed. Registration is therefore schema-safe without awaiting the
/// (internal) membership initializer directly. The call is idempotent with the substrate heartbeat service's
/// own registration (its <c>if (Identity is null)</c> guard makes a later attempt a no-op).
/// </remarks>
internal sealed class JobsCoordinationStartupGate(
    INodeMembership membership,
    ILogger<JobsCoordinationStartupGate> logger
) : IHostedLifecycleService
{
    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        if (membership.Identity is not null)
        {
            return;
        }

        var identity = await membership.RegisterAsync(cancellationToken).ConfigureAwait(false);
        logger.NodeRegistered(identity);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task StartedAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task StoppingAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task StoppedAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}

internal static partial class JobsCoordinationStartupGateLog
{
    [LoggerMessage(
        EventId = 1,
        EventName = "NodeRegistered",
        Level = LogLevel.Information,
        Message = "Jobs node registered with coordination membership as {Identity}"
    )]
    public static partial void NodeRegistered(this ILogger logger, NodeIdentity identity);
}
