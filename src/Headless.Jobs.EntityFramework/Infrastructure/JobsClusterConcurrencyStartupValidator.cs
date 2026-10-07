// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Jobs.Infrastructure;

/// <summary>
/// Fails startup when a registered job sets <c>ClusterMaxConcurrency</c> but this host claims through the portable
/// optimistic-CAS strategy, which cannot honor a cluster-wide limit. Without the check the misconfiguration surfaces
/// only when the scheduler first claims, after the host reported a healthy start.
/// </summary>
/// <remarks>
/// A host with background services disabled never claims, so it passes: an enqueue-only node may run the generic EF
/// store while its workers run a native claim.
/// </remarks>
/// <param name="services">
/// Provider the check resolves from inside <c>ValidateAsync</c>: the job catalog that knows the cluster limits is built
/// on first resolution, after every contribution has been recorded.
/// </param>
internal sealed class JobsClusterConcurrencyStartupValidator<TContext, TTimeJob, TCronJob>(IServiceProvider services)
    : IHeadlessStartupValidator
    where TContext : DbContext
    where TTimeJob : TimeJobEntity<TTimeJob>, new()
    where TCronJob : CronJobEntity, new()
{
    public Task ValidateAsync(CancellationToken cancellationToken)
    {
        if (services.GetService<JobsOptionsBuilder<TTimeJob, TCronJob>>() is { RegisterBackgroundServices: false })
        {
            return Task.CompletedTask;
        }

        var clusterConcurrency = services.GetService<JobsClusterConcurrency>();

        if (clusterConcurrency is not { HasLimits: true })
        {
            return Task.CompletedTask;
        }

        var reason = services.GetRequiredService<IJobsClaimStrategy<TTimeJob, TCronJob>>() switch
        {
            EfCoreCasJobsClaimStrategy<TContext, TTimeJob, TCronJob> => "no native claim provider is configured",
            CompatibleJobsClaimStrategy<TContext, TTimeJob, TCronJob> compatible => compatible.FindCasFallbackReason(),
            _ => null,
        };

        if (reason is null)
        {
            return Task.CompletedTask;
        }

        throw new InvalidOperationException(
            $"Headless.Jobs: jobs {string.Join(", ", clusterConcurrency.LimitedFunctions)} set ClusterMaxConcurrency, "
                + $"which needs a native claim provider, but this host claims through the portable EF CAS path: {reason}. "
                + "Register the store with UsePostgreSql(...) (Headless.Jobs.EntityFramework.PostgreSql) or "
                + "UseSqlServer(...) (Headless.Jobs.EntityFramework.SqlServer), or call UsePostgreSqlClaims() or "
                + "UseSqlServerClaims() inside UseEntityFramework, and keep the jobs model compatible with the native "
                + "claim; otherwise remove ClusterMaxConcurrency."
        );
    }
}
