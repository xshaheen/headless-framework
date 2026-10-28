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

/// <summary>
/// DI registration helpers for the S3 TUS store.
/// </summary>
/// <remarks>
/// Registration is optional: tusdotnet composes stores inside <c>DefaultTusConfiguration</c> factories, so
/// constructing <c>TusS3Store</c> manually remains supported. Use these extensions when the store should participate
/// in the host's options pipeline (configuration binding, FluentValidation, <c>ValidateOnStart</c>) and be resolved
/// from DI in the configuration factory.
/// </remarks>
[PublicAPI]
public static class SetupTusS3Store
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers <see cref="TusS3Store"/> as a singleton with options bound from <paramref name="config"/> and
        /// validated at startup.
        /// </summary>
        /// <param name="config">configuration section to bind <see cref="TusS3StoreOptions"/> from</param>
        /// <returns>the same service collection for chaining</returns>
        /// <remarks>
        /// Requires an <see cref="IAmazonS3"/> registration configured for the provider (for R2: path style,
        /// authentication region <c>auto</c>, and <c>WHEN_REQUIRED</c> checksums). The bucket is registered as a
        /// provisioned resource of kind <c>S3Bucket</c>: a deploy step creates it with <c>resources provision</c>,
        /// and a starting host holds readiness until it exists.
        /// </remarks>
        public IServiceCollection AddTusS3Store(IConfiguration config)
        {
            Argument.IsNotNull(config);
            services.Configure<TusS3StoreOptions, TusS3StoreOptionsValidator>(config);

            return _AddTusS3StoreCore(services);
        }

        /// <inheritdoc cref="AddTusS3Store(IServiceCollection, IConfiguration)"/>
        /// <param name="setupAction">delegate configuring <see cref="TusS3StoreOptions"/></param>
        public IServiceCollection AddTusS3Store(Action<TusS3StoreOptions> setupAction)
        {
            Argument.IsNotNull(setupAction);
            services.Configure<TusS3StoreOptions, TusS3StoreOptionsValidator>(setupAction);

            return _AddTusS3StoreCore(services);
        }

        /// <inheritdoc cref="AddTusS3Store(IServiceCollection, IConfiguration)"/>
        /// <param name="setupAction">delegate configuring <see cref="TusS3StoreOptions"/> with access to the service provider</param>
        public IServiceCollection AddTusS3Store(Action<TusS3StoreOptions, IServiceProvider> setupAction)
        {
            Argument.IsNotNull(setupAction);
            services.Configure<TusS3StoreOptions, TusS3StoreOptionsValidator>(setupAction);

            return _AddTusS3StoreCore(services);
        }
    }

    private static IServiceCollection _AddTusS3StoreCore(IServiceCollection services)
    {
        // A repeated call keeps the first store (TryAdd), so it must not register a second provisioner either.
        var storeAlreadyRegistered = services.Any(static d => d.ServiceType == typeof(TusS3Store));

        services.TryAddSingleton(TimeProvider.System);
        // Lets the store read an upload's state once per tus request instead of once per store call.
        services.AddHttpContextAccessor();

        services.TryAddSingleton(provider => new TusS3Store(
            provider.GetRequiredService<IAmazonS3>(),
            provider.GetRequiredService<IOptions<TusS3StoreOptions>>().Value,
            provider.GetService<ILoggerFactory>(),
            provider.GetService<TimeProvider>(),
            provider.GetService<IHttpContextAccessor>()
        ));

        if (!storeAlreadyRegistered)
        {
            services.AddResourceProvisioner(static provider => new TusS3BucketProvisioner(
                provider.GetRequiredService<IAmazonS3>(),
                provider.GetRequiredService<IOptions<TusS3StoreOptions>>().Value,
                provider.GetService<ILoggerFactory>()
            ));
        }

        // Lets cross-cutting consumers (e.g. AddTusExpiredUploadsCleanup) resolve the store by its tusdotnet capability
        // interface without knowing the provider.
        services.TryAddSingleton<ITusExpirationStore>(provider => provider.GetRequiredService<TusS3Store>());

        return services;
    }
}
