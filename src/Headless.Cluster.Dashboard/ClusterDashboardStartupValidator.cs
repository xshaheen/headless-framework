// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;
using Headless.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Headless.Cluster;

/// <summary>
/// Fails the host at start when it serves neither half the module shows, so a module with nothing to display is caught
/// at deploy time instead of as two empty views.
/// </summary>
/// <remarks>
/// It checks the built container, not the service collection at <c>AddCluster</c> time, so Coordination and health
/// checks may be registered in any order relative to the module. It resolves two services and does no I/O.
/// </remarks>
internal sealed class ClusterDashboardStartupValidator(IServiceProvider services) : IHeadlessStartupValidator
{
    public Task ValidateAsync(CancellationToken cancellationToken)
    {
        if (
            services.GetService<CoordinationClusterSummary>() is null
            && services.GetService<HealthCheckService>() is null
        )
        {
            throw new InvalidOperationException(
                "The Cluster dashboard module (AddCluster) shows Coordination membership and health checks, and this "
                    + "host registers neither. Call services.AddHeadlessCoordination(...) with a provider, or "
                    + "services.AddHealthChecks() with the host's checks."
            );
        }

        return Task.CompletedTask;
    }
}
