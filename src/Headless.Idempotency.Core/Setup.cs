// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Checks;
using Headless.Hosting.Initialization;
using Headless.MultiTenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.Idempotency;

/// <summary>Registers durable idempotency.</summary>
[PublicAPI]
public static class SetupHeadlessIdempotency
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers <see cref="IIdempotentOperations" />, the enlisted <c>unit.Idempotency</c> feature, the
        /// retention purge, and the chosen provider. Exactly one <c>setup.Use…</c> call (<c>UseInMemory</c>,
        /// <c>UsePostgreSql</c>, or <c>UseSqlServer</c>) is required.
        /// </summary>
        /// <param name="configure">Chooses the provider and configures admission defaults, the purge, and storage.</param>
        /// <returns>The service collection, to allow chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure" /> is <see langword="null" />.</exception>
        /// <exception cref="InvalidOperationException">
        /// No provider or more than one provider was chosen, or idempotency was already registered.
        /// </exception>
        public IServiceCollection AddHeadlessIdempotency(Action<HeadlessIdempotencySetupBuilder> configure)
        {
            Argument.IsNotNull(configure);

            var setup = new HeadlessIdempotencySetupBuilder(services);
            configure(setup);

            services.GuardSingleStorageProvider(
                setup.Extensions.Count,
                setup.Extensions.Count == 1 ? setup.Extensions[0].GetType().FullName ?? "unknown" : "unknown",
                "Headless.Idempotency",
                ["UseInMemory", "UsePostgreSql", "UseSqlServer"],
                static name => new IdempotencyProviderRegistration(name)
            );

            services.AddOptions<IdempotentOperationsOptions, IdempotentOperationsOptionsValidator>();

            if (setup.OptionsConfigurator is not null)
            {
                services.Configure(setup.OptionsConfigurator);
            }

            // Registered without a validator: the provider attaches the one that knows its identifier rules.
            services.AddOptions<IdempotencyStorageOptions>();

            // Records are keyed by the current tenant, so ICurrentTenant must follow ICurrentTenant.Change. The
            // NullCurrentTenant fallback ignores Change and would key every tenant's record on the host scope; this
            // swaps it for the AsyncLocal-backed tenant unless the host registered a real one.
            services.TryAddSingleton<ICurrentTenantAccessor>(AsyncLocalCurrentTenantAccessor.Instance);
            services.AddOrReplaceFallbackSingleton<ICurrentTenant, NullCurrentTenant, CurrentTenant>();

            services.TryAddSingleton(TimeProvider.System);
            services.TryAddSingleton<IdempotencyRequestResolver>();
            services.TryAddSingleton<IUnitOfWorkIdempotency, UnitOfWorkIdempotencyFeature>();
            services.TryAddSingleton<IIdempotentOperations, IdempotentOperations>();

            // Always registered; the service itself stays idle on a fixed re-check cadence when PurgeInterval is
            // null, so the switch stays an option that configuration can flip without re-registering services.
            services.AddHostedService<IdempotencyRetentionService>();

            foreach (var extension in setup.Extensions)
            {
                extension.AddServices(services);
            }

            return services;
        }
    }

    private sealed record IdempotencyProviderRegistration(string Provider);
}
