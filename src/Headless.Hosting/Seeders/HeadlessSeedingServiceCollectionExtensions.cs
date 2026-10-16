// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Hosting;
using Microsoft.Extensions.Hosting;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Chooses how the registered <see cref="ISeeder"/>s run: at host start, or by the deployment's <c>seed</c> verb.</summary>
[PublicAPI]
public static class HeadlessSeedingServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Runs every seeder when the host starts, after the stateful resources are set up and before any hosted
        /// service starts, so a development or test host needs no separate <c>seed</c> step. A failed seeder fails the
        /// start and the seeders after it do not run.
        /// </summary>
        /// <returns>The same service collection.</returns>
        /// <exception cref="ArgumentNullException">The service collection is <see langword="null"/>.</exception>
        /// <remarks>
        /// Guard it on the environment to keep it out of production, where the deployment runs <c>seed</c> and calls
        /// <c>AddHeadlessSeedDuringDeploy</c> instead.
        /// </remarks>
        public IServiceCollection AddHeadlessSeedOnStartup()
        {
            Argument.IsNotNull(services);

            services.Configure<SeedingOptions>(static options => options.OnStartup = true);

            if (!services.Any(static d => d.ServiceType == typeof(SeedStartupService)))
            {
                services.AddSingleton<SeedStartupService>();
                services.AddSingleton<IHostedService>(static sp => sp.GetRequiredService<SeedStartupService>());
            }

            return services;
        }

        /// <summary>
        /// Declares that the deployment runs the <c>seed</c> verb of <c>RunHeadlessAsync</c>, so the host does not warn
        /// at startup that nothing runs its seeders. It runs nothing itself.
        /// </summary>
        /// <returns>The same service collection.</returns>
        /// <exception cref="ArgumentNullException">The service collection is <see langword="null"/>.</exception>
        public IServiceCollection AddHeadlessSeedDuringDeploy()
        {
            Argument.IsNotNull(services);

            services.Configure<SeedingOptions>(static options => options.DuringDeploy = true);

            return services;
        }
    }
}
