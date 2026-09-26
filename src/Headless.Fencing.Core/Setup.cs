// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Checks;
using Headless.Hosting.Initialization;
using Headless.MultiTenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.Fencing;

/// <summary>Registers fenced leases.</summary>
[PublicAPI]
public static class SetupFencing
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers <see cref="IFencedLeases" />, the enlisted <c>unit.Leases</c> feature, and the chosen provider.
        /// Exactly one <c>setup.Use…</c> call (<c>UsePostgreSql</c> or <c>UseSqlServer</c>) is required.
        /// </summary>
        /// <param name="configure">Chooses the provider and configures duration bounds and storage.</param>
        /// <returns>The service collection, to allow chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure" /> is <see langword="null" />.</exception>
        /// <exception cref="InvalidOperationException">
        /// No provider or more than one provider was chosen, or fencing was already registered.
        /// </exception>
        public IServiceCollection AddHeadlessFencing(Action<HeadlessFencingSetupBuilder> configure)
        {
            Argument.IsNotNull(configure);

            var setup = new HeadlessFencingSetupBuilder(services);
            configure(setup);

            services.GuardSingleStorageProvider(
                setup.Extensions.Count,
                setup.Extensions.Count == 1 ? setup.Extensions[0].GetType().FullName ?? "unknown" : "unknown",
                "Headless.Fencing",
                ["UsePostgreSql", "UseSqlServer"],
                static name => new FencingProviderRegistration(name)
            );

            services.AddOptions<FencingOptions, FencingOptionsValidator>();

            if (setup.OptionsConfigurator is not null)
            {
                services.Configure(setup.OptionsConfigurator);
            }

            // Registered without a validator: the provider attaches the one that knows its identifier rules.
            services.AddOptions<FencingStorageOptions>();

            // Leases are keyed by the current tenant, so ICurrentTenant must follow ICurrentTenant.Change. The
            // NullCurrentTenant fallback ignores Change and would key every tenant's grant on the host scope; this
            // swaps it for the AsyncLocal-backed tenant unless the host registered a real one.
            services.TryAddSingleton<ICurrentTenantAccessor>(AsyncLocalCurrentTenantAccessor.Instance);
            services.AddOrReplaceFallbackSingleton<ICurrentTenant, NullCurrentTenant, CurrentTenant>();

            services.TryAddSingleton<LeaseRequestResolver>();
            services.TryAddSingleton<IFencedLeases, FencedLeases>();
            services.TryAddSingleton<IUnitOfWorkLeases, UnitOfWorkLeasesFeature>();

            foreach (var extension in setup.Extensions)
            {
                extension.AddServices(services);
            }

            return services;
        }
    }

    private sealed record FencingProviderRegistration(string Provider);
}
