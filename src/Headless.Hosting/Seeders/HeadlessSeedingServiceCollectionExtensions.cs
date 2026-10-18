// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Hosting;
using Microsoft.Extensions.Hosting;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Runs the registered <see cref="ISeeder"/>s at host start. Without it they run only through the deployment's
/// <c>seed</c> verb, which is the default.
/// </summary>
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
        /// Guard it on the environment to keep it out of production, where the deployment runs <c>seed</c> instead.
        /// </remarks>
        public IServiceCollection AddHeadlessSeedOnStartup()
        {
            Argument.IsNotNull(services);

            if (!services.Any(static d => d.ServiceType == typeof(SeedStartupService)))
            {
                services.AddSingleton<SeedStartupService>();
                services.AddSingleton<IHostedService>(static sp => sp.GetRequiredService<SeedStartupService>());
            }

            return services;
        }
    }
}
