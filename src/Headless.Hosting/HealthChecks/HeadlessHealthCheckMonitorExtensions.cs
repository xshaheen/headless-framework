// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Headless.Hosting;

/// <summary>Registers the opt-in health check monitor.</summary>
[PublicAPI]
public static class HeadlessHealthCheckMonitorExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers <see cref="IHealthCheckMonitor" />, which lists the host's health checks, probes them when a
        /// caller asks, and keeps the last 30 results of each check.
        /// </summary>
        /// <param name="aliveTag">
        /// The tag that marks a liveness check, reported as <see cref="HealthCheckDescriptor.IsLiveness" />. Match the
        /// tag your <c>/alive</c> endpoint filters on; the Headless API defaults use <c>live</c>.
        /// </param>
        /// <returns>The same <see cref="IServiceCollection" /> for chaining.</returns>
        /// <remarks>
        /// The monitor runs no check on its own: it adds no publisher, no startup probe, and no hosted service beyond
        /// the inert one <c>AddHealthChecks()</c> registers. Its history fills only from
        /// <see cref="IHealthCheckMonitor.ProbeAllAsync" /> and <see cref="IHealthCheckMonitor.ProbeAsync" />, never
        /// from a health endpoint request. A second call adds nothing and keeps the first call's
        /// <paramref name="aliveTag" />.
        /// </remarks>
        /// <exception cref="ArgumentException"><paramref name="aliveTag" /> is <see langword="null" />, empty, or whitespace.</exception>
        public IServiceCollection AddHeadlessHealthCheckMonitor(string aliveTag = "live")
        {
            Argument.IsNotNull(services);
            Argument.IsNotNullOrWhiteSpace(aliveTag);

            services.AddHealthChecks();
            services.TryAddSingleton(TimeProvider.System);
            services.TryAddSingleton<IHealthCheckMonitor>(provider => new HealthCheckMonitor(
                provider.GetRequiredService<HealthCheckService>(),
                provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>(),
                provider.GetRequiredService<TimeProvider>(),
                aliveTag
            ));

            return services;
        }
    }
}
