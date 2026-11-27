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
    private static readonly Func<IServiceProvider, object> _ConditionalFactory = _CreateIfRegistered;

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

            // An explicit registration always takes precedence over a conditional fallback registered earlier,
            // while a second explicit call keeps the first call's aliveTag.
            for (var index = services.Count - 1; index >= 0; index--)
            {
                var descriptor = services[index];

                if (
                    descriptor.ServiceType == typeof(IHealthCheckMonitor)
                    && ReferenceEquals(descriptor.ImplementationFactory, _ConditionalFactory)
                )
                {
                    services.RemoveAt(index);
                }
            }

            services.TryAddSingleton<IHealthCheckMonitor>(provider => new HealthCheckMonitor(
                provider.GetRequiredService<HealthCheckService>(),
                provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>(),
                provider.GetRequiredService<TimeProvider>(),
                aliveTag
            ));

            return services;
        }

        /// <summary>
        /// Registers <see cref="IHealthCheckMonitor" /> so it resolves whenever <see cref="HealthCheckService" /> is
        /// registered anywhere in the service collection (regardless of registration order), without calling
        /// <c>AddHealthChecks()</c> itself. When no health checks are registered, resolving
        /// <see cref="IHealthCheckMonitor" /> returns <see langword="null" />.
        /// </summary>
        /// <returns>The same <see cref="IServiceCollection" /> for chaining.</returns>
        public IServiceCollection TryAddHeadlessHealthCheckMonitorIfRegistered()
        {
            Argument.IsNotNull(services);

            services.TryAddSingleton(TimeProvider.System);
            services.TryAdd(ServiceDescriptor.Singleton(typeof(IHealthCheckMonitor), _ConditionalFactory));

            return services;
        }
    }

    private static object _CreateIfRegistered(IServiceProvider provider) =>
        provider.GetService<HealthCheckService>() is { } healthCheckService
            ? new HealthCheckMonitor(
                healthCheckService,
                provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>(),
                provider.GetRequiredService<TimeProvider>(),
                "live"
            )
            : null!;
}
