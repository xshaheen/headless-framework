// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Fencing;
using Headless.Hosting.Initialization;
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
        /// retention purge, and the chosen provider. Exactly one <c>setup.Use…</c> call (<c>UsePostgreSql</c> or
        /// <c>UseSqlServer</c>) is required, and fenced leases must already be registered with
        /// <c>AddHeadlessFencing</c> on the same database.
        /// </summary>
        /// <param name="configure">Chooses the provider and configures admission defaults, the purge, and storage.</param>
        /// <returns>The service collection, to allow chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure" /> is <see langword="null" />.</exception>
        /// <exception cref="InvalidOperationException">
        /// Fenced leases are not registered, no provider or more than one provider was chosen, or idempotency was
        /// already registered.
        /// </exception>
        public IServiceCollection AddHeadlessIdempotency(Action<HeadlessIdempotencySetupBuilder> configure)
        {
            Argument.IsNotNull(configure);

            // Every admission holds a fenced lease written in the same transaction as its record, so without fencing
            // no admission can run. Failing here names the fix; failing at the first admission would only name a
            // missing service.
            if (!services.Any(static d => d.ServiceType == typeof(IFencedLeases)))
            {
                throw new InvalidOperationException(
                    "Headless.Idempotency admits every operation under a fenced lease, but no fencing is registered. "
                        + "Call AddHeadlessFencing (with UsePostgreSql or UseSqlServer, on the same database) before "
                        + "AddHeadlessIdempotency."
                );
            }

            var setup = new HeadlessIdempotencySetupBuilder(services);
            configure(setup);

            services.GuardSingleStorageProvider(
                setup.Extensions.Count,
                setup.Extensions.Count == 1 ? setup.Extensions[0].GetType().FullName ?? "unknown" : "unknown",
                "Headless.Idempotency",
                ["UsePostgreSql", "UseSqlServer"],
                static name => new IdempotencyProviderRegistration(name)
            );

            services.AddOptions<IdempotentOperationsOptions, IdempotentOperationsOptionsValidator>();

            if (setup.OptionsConfigurator is not null)
            {
                services.Configure(setup.OptionsConfigurator);
            }

            // Registered without a validator: the provider attaches the one that knows its identifier rules.
            services.AddOptions<IdempotencyStorageOptions>();

            services.TryAddSingleton(TimeProvider.System);
            services.TryAddSingleton<IdempotencyRequestResolver>();
            services.TryAddSingleton<IUnitOfWorkIdempotency, UnitOfWorkIdempotencyFeature>();
            services.TryAddSingleton<IIdempotentOperations, IdempotentOperations>();

            // Always registered; the service itself exits at once when PurgeInterval is null, so the switch stays an
            // option that configuration can flip without re-registering services.
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
