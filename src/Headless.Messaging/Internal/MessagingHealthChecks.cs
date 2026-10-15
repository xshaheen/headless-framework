// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

namespace Headless.Messaging.Internal;

/// <summary>
/// The readiness checks every Messaging host contributes: <c>messaging</c> reports whether the host can move messages
/// at all, <c>messaging-consumers</c> whether any consumer is paused, and <c>messaging-outboxes</c> whether every
/// additional outbox is ready to take writes.
/// </summary>
/// <remarks>
/// The probes read state the host already keeps and never wait on it. They resolve the internal services rather than
/// the public interfaces in front of them, because an application may register its own implementation of a public
/// interface, and readiness must report on the instance the host runs.
/// </remarks>
internal static class MessagingHealthChecks
{
    public const string HostCheckName = "messaging";

    public const string ConsumersCheckName = "messaging-consumers";

    public const string OutboxesCheckName = "messaging-outboxes";

    public static void Add(IServiceCollection services, bool hasAdditionalOutboxes)
    {
        // Unhealthy until the bootstrapper has started, from the moment host shutdown begins, and while the broker is
        // unreachable. The connectivity state is fed by real receive and send outcomes, so a publish-only host answers
        // too, and a transport restart's optimistic reset does not read as a recovery.
        services.AddHeadlessHealthCheck(
            HostCheckName,
            static (provider, _) =>
            {
                if (!provider.GetRequiredService<Bootstrapper>().IsStarted)
                {
                    throw new InvalidOperationException("The Messaging host is not started.");
                }

                // Absent outside a generic host, where nothing stops the application either. The bootstrapper stops
                // only when its own hosted service stops, which can be well after shutdown began.
                if (
                    provider.GetService<IHostApplicationLifetime>()?.ApplicationStopping.IsCancellationRequested == true
                )
                {
                    throw new InvalidOperationException("The Messaging host is shutting down.");
                }

                return provider.GetRequiredService<TransportConnectivity>().IsReachable
                    ? Task.CompletedTask
                    : throw new InvalidOperationException("The message broker is unreachable.");
            },
            HeadlessHealthCheckTags.Messaging
        );

        // An Open circuit, tripped or forced by an operator, means the consumer takes nothing. HalfOpen is already
        // resuming, so reporting it would only make alerts flap.
        services.AddHeadlessHealthCheck(
            ConsumersCheckName,
            static (provider, _) =>
            {
                foreach (var state in provider.GetRequiredService<ICircuitBreakerStateManager>().GetAllStates().Values)
                {
                    if (state is CircuitBreakerState.Open)
                    {
                        throw new InvalidOperationException("A Messaging consumer circuit is open.");
                    }
                }

                return Task.CompletedTask;
            },
            HeadlessHealthCheckTags.Messaging
        );

        if (hasAdditionalOutboxes)
        {
            // An additional outbox whose database was down at startup only refuses units of work on that database,
            // and its initialization keeps retrying, so the host stays ready while the check flags the outbox.
            services.AddHeadlessHealthCheck(
                OutboxesCheckName,
                static (provider, _) =>
                {
                    foreach (var outbox in provider.GetRequiredService<MessagingOutboxes>().Secondaries)
                    {
                        if (!outbox.IsInitialized)
                        {
                            throw new InvalidOperationException("An additional Messaging outbox is not initialized.");
                        }
                    }

                    return Task.CompletedTask;
                },
                HeadlessHealthCheckTags.Messaging
            );
        }

        // Degraded rather than Unhealthy: a paused consumer or an unavailable additional outbox leaves the host able to
        // do the rest of its work, so it stays in rotation. A package default, so configuration and an application's
        // ConfigureHeadlessHealthCheck call override it.
        services.ConfigureHeadlessHealthCheckDefault(
            ConsumersCheckName,
            static options => options.FailureStatus = HealthStatus.Degraded
        );

        if (hasAdditionalOutboxes)
        {
            services.ConfigureHeadlessHealthCheckDefault(
                OutboxesCheckName,
                static options => options.FailureStatus = HealthStatus.Degraded
            );
        }
    }
}
