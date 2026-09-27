// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Checks;
using Headless.Hosting.Initialization;
using Headless.MultiTenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.Sequences;

/// <summary>Registers tenant-scoped sequences.</summary>
[PublicAPI]
public static class SetupSequences
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers <see cref="ISequenceGenerator" />, the gap-free <c>unit.Sequences</c> feature, and the chosen
        /// provider. Exactly one <c>setup.Use…</c> call (<c>UsePostgreSql</c> or <c>UseSqlServer</c>) is required.
        /// </summary>
        /// <param name="configure">Chooses the provider and configures the numbering policies.</param>
        /// <returns>The service collection, to allow chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure" /> is <see langword="null" />.</exception>
        /// <exception cref="InvalidOperationException">
        /// No provider or more than one provider was chosen, or sequences were already registered.
        /// </exception>
        public IServiceCollection AddHeadlessSequences(Action<HeadlessSequencesSetupBuilder> configure)
        {
            Argument.IsNotNull(configure);

            var setup = new HeadlessSequencesSetupBuilder(services);
            configure(setup);

            services.GuardSingleStorageProvider(
                setup.Extensions.Count,
                setup.Extensions.Count == 1 ? setup.Extensions[0].GetType().FullName ?? "unknown" : "unknown",
                "Headless.Sequences",
                ["UsePostgreSql", "UseSqlServer"],
                static name => new SequencesProviderRegistration(name)
            );

            services.AddOptions<SequencesOptions, SequencesOptionsValidator>();

            if (setup.OptionsConfigurator is not null)
            {
                services.Configure(setup.OptionsConfigurator);
            }

            // Counters are keyed by the current tenant, so ICurrentTenant must follow ICurrentTenant.Change. The
            // NullCurrentTenant fallback ignores Change and would number every tenant on the host counter; this
            // swaps it for the AsyncLocal-backed tenant unless the host registered a real one.
            services.TryAddSingleton<ICurrentTenantAccessor>(AsyncLocalCurrentTenantAccessor.Instance);
            services.AddOrReplaceFallbackSingleton<ICurrentTenant, NullCurrentTenant, CurrentTenant>();

            services.TryAddSingleton<SequenceRequestResolver>();
            services.TryAddSingleton<ISequenceGenerator, SequenceGenerator>();
            services.TryAddSingleton<IUnitOfWorkSequences, UnitOfWorkSequencesFeature>();

            foreach (var extension in setup.Extensions)
            {
                extension.AddServices(services);
            }

            return services;
        }
    }

    private sealed record SequencesProviderRegistration(string Provider);
}
