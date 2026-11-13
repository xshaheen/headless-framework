// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Amazon.S3;
using Headless.Checks;
using Headless.Hosting;
using Headless.Tus.Internal;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using tusdotnet.Interfaces;

namespace Headless.Tus;

/// <summary>Extension methods to select the S3 TUS store.</summary>
[PublicAPI]
public static class SetupTusS3
{
    extension(HeadlessTusSetupBuilder setup)
    {
        /// <summary>
        /// Uses <see cref="TusS3Store"/> as the host's tus store, with options bound from
        /// <paramref name="configuration"/> and validated at startup.
        /// </summary>
        /// <param name="configuration">The configuration section to bind <see cref="TusS3StoreOptions"/> from.</param>
        /// <returns>The builder for chaining.</returns>
        /// <remarks>
        /// Requires an <see cref="IAmazonS3"/> registration configured for the provider (for R2: path style,
        /// authentication region <c>auto</c>, and <c>WHEN_REQUIRED</c> checksums). An
        /// <c>ITusS3ObjectHeadersProvider</c> or <c>ITusFileIdProvider</c> the application registers is honored;
        /// otherwise the defaults apply. The bucket is registered as a provisioned resource of kind
        /// <c>S3Bucket</c>: a deploy step creates it with <c>resources provision</c>, and a starting host holds
        /// readiness until it exists.
        /// </remarks>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="configuration"/> is <see langword="null"/>.</exception>
        public HeadlessTusSetupBuilder UseS3(IConfiguration configuration)
        {
            Argument.IsNotNull(configuration);

            setup.RegisterStore<TusS3Store>(services =>
            {
                services.Configure<TusS3StoreOptions, TusS3StoreOptionsValidator>(configuration);
                _AddStoreCore(services);
            });

            return setup;
        }

        /// <inheritdoc cref="UseS3(HeadlessTusSetupBuilder, IConfiguration)"/>
        /// <param name="setupAction">Delegate configuring <see cref="TusS3StoreOptions"/>.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="setupAction"/> is <see langword="null"/>.</exception>
        public HeadlessTusSetupBuilder UseS3(Action<TusS3StoreOptions> setupAction)
        {
            Argument.IsNotNull(setupAction);

            setup.RegisterStore<TusS3Store>(services =>
            {
                services.Configure<TusS3StoreOptions, TusS3StoreOptionsValidator>(setupAction);
                _AddStoreCore(services);
            });

            return setup;
        }

        /// <inheritdoc cref="UseS3(HeadlessTusSetupBuilder, IConfiguration)"/>
        /// <param name="setupAction">Delegate configuring <see cref="TusS3StoreOptions"/> with access to the service provider.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="setupAction"/> is <see langword="null"/>.</exception>
        public HeadlessTusSetupBuilder UseS3(Action<TusS3StoreOptions, IServiceProvider> setupAction)
        {
            Argument.IsNotNull(setupAction);

            setup.RegisterStore<TusS3Store>(services =>
            {
                services.Configure<TusS3StoreOptions, TusS3StoreOptionsValidator>(setupAction);
                _AddStoreCore(services);
            });

            return setup;
        }
    }

    private static void _AddStoreCore(IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ITusS3ObjectHeadersProvider, DefaultTusS3ObjectHeadersProvider>();
        // Lets the store read an upload's state once per tus request instead of once per store call.
        services.AddHttpContextAccessor();

        services.AddSingleton(provider => new TusS3Store(
            provider.GetRequiredService<IAmazonS3>(),
            provider.GetRequiredService<IOptions<TusS3StoreOptions>>().Value,
            // Registered by AddHeadlessTus: the store keeps each tenant's uploads apart once the application's
            // tenancy setup scopes uploads by tenant.
            provider.GetRequiredService<TusTenantScope>(),
            provider.GetRequiredService<ITusS3ObjectHeadersProvider>(),
            provider.GetService<ITusFileIdProvider>(),
            provider.GetService<ILoggerFactory>(),
            provider.GetService<TimeProvider>(),
            provider.GetService<IHttpContextAccessor>()
        ));

        services.AddResourceProvisioner(static provider => new TusS3BucketProvisioner(
            provider.GetRequiredService<IAmazonS3>(),
            provider.GetRequiredService<IOptions<TusS3StoreOptions>>().Value,
            provider.GetService<ILoggerFactory>()
        ));
    }
}
