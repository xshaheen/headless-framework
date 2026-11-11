// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Hosting;
using Headless.LiveQueries.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.LiveQueries;

/// <summary>Registers live queries.</summary>
[PublicAPI]
public static class SetupHeadlessLiveQueries
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers the in-process live-query hub with <see cref="ILiveQueries" />,
        /// <see cref="ILiveQueryInvalidator" /> and <see cref="ILiveQueryDependencies" />. It starts no background
        /// work and does no I/O. Calling it again keeps the first registration and applies the new options on top.
        /// </summary>
        /// <param name="configure">Configures the options and the family's other packages.</param>
        /// <returns>The service collection, to allow chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure" /> is <see langword="null" />.</exception>
        public IServiceCollection AddHeadlessLiveQueries(Action<HeadlessLiveQueriesSetupBuilder> configure)
        {
            Argument.IsNotNull(configure);

            var setup = new HeadlessLiveQueriesSetupBuilder(services);
            configure(setup);

            services.AddOptions<LiveQueriesOptions, LiveQueriesOptionsValidator>();

            if (setup.OptionsConfigurator is not null)
            {
                services.Configure(setup.OptionsConfigurator);
            }

            services.TryAddSingleton<LiveQueriesMetrics>();
            services.TryAddSingleton<LiveQueryHub>();
            services.TryAddSingleton<ILiveQueryInvalidator, LiveQueryInvalidator>();
            services.TryAddSingleton<ILiveQueryDependencies, LiveQueryDependencies>();
            services.TryAddSingleton<ILiveQueries, LiveQueryWatcher>();

            return services;
        }
    }
}
