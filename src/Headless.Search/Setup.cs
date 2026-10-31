// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Search;

/// <summary>Registers search.</summary>
[PublicAPI]
public static class SetupSearch
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers the chosen search provider, which ships the database function that builds search keys. Exactly
        /// one <c>setup.Use…</c> call (such as <c>UsePostgreSql</c>) is required.
        /// </summary>
        /// <param name="configure">Chooses the provider.</param>
        /// <returns>The service collection to support method chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure" /> is <see langword="null" />.</exception>
        /// <exception cref="InvalidOperationException">
        /// No provider or more than one provider was chosen, or search was already registered.
        /// </exception>
        public IServiceCollection AddHeadlessSearch(Action<HeadlessSearchSetupBuilder> configure)
        {
            Argument.IsNotNull(configure);

            var setup = new HeadlessSearchSetupBuilder(services);
            configure(setup);

            services.GuardSingleStorageProvider(
                setup.Extensions.Count,
                setup.Extensions.Count == 1 ? setup.Extensions[0].GetType().FullName ?? "unknown" : "unknown",
                "Headless.Search",
                ["UsePostgreSql"],
                static name => new SearchProviderRegistration(name)
            );

            foreach (var extension in setup.Extensions)
            {
                extension.AddServices(services);
            }

            return services;
        }
    }

    private sealed record SearchProviderRegistration(string Provider);
}
