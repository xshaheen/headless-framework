// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Headless.Dashboards.Sandbox;

/// <summary>The status the sandbox's own health check reports, which the health scenarios set.</summary>
public sealed class SandboxHealthToggle
{
    public const string CheckName = "sandbox-dependency";

    private volatile int _status = (int)HealthStatus.Healthy;

    public HealthStatus Status
    {
        get => (HealthStatus)_status;
        set => _status = (int)value;
    }

    public HealthCheckResult Check() => new(Status, $"The sandbox dependency reports {Status}.");
}

/// <summary>A scenario that cannot run on this sandbox's store, answered as <c>409</c>.</summary>
public sealed class SandboxScenarioUnavailableException(string message) : Exception(message);

/// <summary>
/// Peer incarnations written straight to the Coordination membership store, so the Cluster view shows every node
/// state without starting more hosts: one peer that keeps beating, one that stops and turns suspected then dead, and one
/// that never beats again, and leaves the snapshot once the store prunes it.
/// </summary>
/// <remarks>
/// The peers own no work, so their eventual dead and left events reach the Jobs and Messaging dead-owner bridges
/// harmlessly. The beat loop is the sandbox's own task, stopped with the host; it is not a framework service.
/// </remarks>
public static class SandboxCluster
{
    public static async Task<IReadOnlyList<string>> AddPeersAsync(
        IServiceProvider services,
        CancellationToken cancellationToken
    )
    {
        var store =
            services.GetService<IMembershipStore>()
            ?? throw new SandboxScenarioUnavailableException(
                "The cluster-peers scenario needs STORE=postgres: the memory store runs no Coordination."
            );
        var summary = services.GetRequiredService<CoordinationClusterSummary>();
        var lifetime = services.GetRequiredService<IHostApplicationLifetime>();

        var api = await _RegisterAsync(store, "orders-api-1", "api", "4.2.1", cancellationToken);
        var stopping = await _RegisterAsync(store, "orders-worker-1", "worker", "4.2.0", cancellationToken);
        var silent = await _RegisterAsync(store, "orders-worker-2", "worker", version: null, cancellationToken);

        _ = Task.Run(
            () => _BeatAsync(store, api, stopping, summary.HeartbeatInterval, lifetime.ApplicationStopping),
            CancellationToken.None
        );

        return [api.ToString(), stopping.ToString(), silent.ToString()];
    }

    private static async Task<NodeIdentity> _RegisterAsync(
        IMembershipStore store,
        string nodeId,
        string role,
        string? version,
        CancellationToken cancellationToken
    )
    {
        var id = new NodeId(nodeId);
        var identity = new NodeIdentity(id, await store.AllocateIncarnationAsync(id, cancellationToken));
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal);

        if (version is not null)
        {
            metadata["app.version"] = version;
        }

        await store.UpsertDescriptorAsync(
            new NodeDescriptor
            {
                Identity = identity,
                Role = role,
                Metadata = metadata,
            },
            cancellationToken
        );

        return identity;
    }

    private static async Task _BeatAsync(
        IMembershipStore store,
        NodeIdentity steady,
        NodeIdentity stopping,
        TimeSpan interval,
        CancellationToken cancellationToken
    )
    {
        try
        {
            using var timer = new PeriodicTimer(interval);
            var beats = 0;

            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await store.HeartbeatAsync(steady, cancellationToken);

                // One beat after registering, then silence: it turns suspected, then dead, while the steady peer stays.
                if (beats++ == 0)
                {
                    await store.HeartbeatAsync(stopping, cancellationToken);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The host is stopping, which ends the peers' beats.
        }
    }
}
