// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Couchbase.Clusters;
using Headless.Couchbase.ContextProvider;
using Headless.Couchbase.Managers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.Couchbase;

/// <summary>
/// Registration extensions for the Headless Couchbase integration.
/// </summary>
[PublicAPI]
public static class SetupCouchbase
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers the framework-owned Couchbase services: <see cref="ICouchbaseClustersProvider"/>,
        /// <see cref="IBucketContextProvider"/>, <see cref="ICouchbaseManager"/>, and
        /// <see cref="ICouchbaseAssemblyCollectionsReader"/>, each as a singleton, using the default
        /// <see cref="CouchbaseManagerOptions"/> values.
        /// </summary>
        /// <remarks>
        /// The consumer must still register the two application-specific providers that supply per-cluster
        /// connection and transaction settings — <see cref="ICouchbaseClusterOptionsProvider"/> and
        /// <see cref="ICouchbaseTransactionConfigProvider"/>. The package ships ready-made implementations
        /// (<see cref="CouchbaseClusterOptionsProvider"/>, <see cref="CouchbaseTransactionConfigProvider"/>,
        /// <see cref="DefaultCouchbaseTransactionConfigProvider"/>) that can be registered directly.
        /// <para>
        /// All services are registered with <c>TryAdd</c> so a consumer can override any of them by
        /// registering their own implementation before calling this method.
        /// </para>
        /// </remarks>
        /// <returns>The same <paramref name="services"/> collection for chaining.</returns>
        public IServiceCollection AddHeadlessCouchbase()
        {
            services.AddOptions<CouchbaseManagerOptions, CouchbaseManagerOptionsValidator>();

            return _AddCouchbaseCore(services);
        }

        /// <inheritdoc cref="AddHeadlessCouchbase(IServiceCollection)"/>
        /// <param name="configuration">The configuration section bound to <see cref="CouchbaseManagerOptions"/>.</param>
        public IServiceCollection AddHeadlessCouchbase(IConfiguration configuration)
        {
            Argument.IsNotNull(configuration);
            services.Configure<CouchbaseManagerOptions, CouchbaseManagerOptionsValidator>(configuration);

            return _AddCouchbaseCore(services);
        }

        /// <inheritdoc cref="AddHeadlessCouchbase(IServiceCollection)"/>
        /// <param name="setupAction">An action to configure <see cref="CouchbaseManagerOptions"/>.</param>
        public IServiceCollection AddHeadlessCouchbase(Action<CouchbaseManagerOptions> setupAction)
        {
            Argument.IsNotNull(setupAction);
            services.Configure<CouchbaseManagerOptions, CouchbaseManagerOptionsValidator>(setupAction);

            return _AddCouchbaseCore(services);
        }

        /// <inheritdoc cref="AddHeadlessCouchbase(IServiceCollection)"/>
        /// <param name="setupAction">
        /// An action to configure <see cref="CouchbaseManagerOptions"/> with access to the <see cref="IServiceProvider"/>.
        /// </param>
        public IServiceCollection AddHeadlessCouchbase(Action<CouchbaseManagerOptions, IServiceProvider> setupAction)
        {
            Argument.IsNotNull(setupAction);
            services.Configure<CouchbaseManagerOptions, CouchbaseManagerOptionsValidator>(setupAction);

            return _AddCouchbaseCore(services);
        }
    }

    private static IServiceCollection _AddCouchbaseCore(IServiceCollection services)
    {
        services.TryAddSingleton<ICouchbaseClustersProvider, CouchbaseClustersProvider>();
        services.TryAddSingleton<IBucketContextProvider, BucketContextProvider>();
        services.TryAddSingleton<ICouchbaseManager, CouchbaseManager>();
        services.TryAddSingleton<ICouchbaseAssemblyCollectionsReader, CouchbaseAssemblyCollectionsReader>();

        return services;
    }
}
