// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure.Storage.Blobs;
using Headless.Checks;
using Headless.Tus.Options;
using Headless.Tus.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using tusdotnet.Interfaces;

namespace Headless.Tus;

/// <summary>
/// DI registration helpers for the Azure Blob Storage TUS store.
/// </summary>
/// <remarks>
/// Registration is optional: tusdotnet composes stores inside <c>DefaultTusConfiguration</c>
/// factories, so constructing <c>TusAzureStore</c> manually remains fully supported. Use these
/// extensions when the store should participate in the host's options pipeline
/// (configuration binding, FluentValidation, <c>ValidateOnStart</c>) and be resolved from DI in
/// the configuration factory.
/// </remarks>
[PublicAPI]
public static class SetupTusAzureStore
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers <see cref="TusAzureStore"/> as a singleton with options bound from
        /// <paramref name="config"/> and validated at startup.
        /// </summary>
        /// <param name="config">configuration section to bind <see cref="TusAzureStoreOptions"/> from</param>
        /// <returns>the same service collection for chaining</returns>
        /// <remarks>
        /// Requires a <see cref="BlobServiceClient"/> registration (for example via
        /// <c>Microsoft.Extensions.Azure</c>'s <c>AddAzureClients</c>). An
        /// <c>ITusAzureBlobHttpHeadersProvider</c> or <c>ITusFileIdProvider</c> registered before
        /// this call is honored; otherwise the defaults apply. When
        /// <see cref="TusAzureStoreOptions.CreateContainerIfNotExists"/> is <see langword="true"/>,
        /// the container is created synchronously when the store is first resolved.
        /// </remarks>
        public IServiceCollection AddTusAzureStore(IConfiguration config)
        {
            Argument.IsNotNull(config);
            services.Configure<TusAzureStoreOptions, TusAzureStoreOptionsValidator>(config);

            return _AddTusAzureStoreCore(services);
        }

        /// <inheritdoc cref="AddTusAzureStore(IServiceCollection, IConfiguration)"/>
        /// <param name="setupAction">delegate configuring <see cref="TusAzureStoreOptions"/></param>
        public IServiceCollection AddTusAzureStore(Action<TusAzureStoreOptions> setupAction)
        {
            Argument.IsNotNull(setupAction);
            services.Configure<TusAzureStoreOptions, TusAzureStoreOptionsValidator>(setupAction);

            return _AddTusAzureStoreCore(services);
        }

        /// <inheritdoc cref="AddTusAzureStore(IServiceCollection, IConfiguration)"/>
        /// <param name="setupAction">delegate configuring <see cref="TusAzureStoreOptions"/> with access to the service provider</param>
        public IServiceCollection AddTusAzureStore(Action<TusAzureStoreOptions, IServiceProvider> setupAction)
        {
            Argument.IsNotNull(setupAction);
            services.Configure<TusAzureStoreOptions, TusAzureStoreOptionsValidator>(setupAction);

            return _AddTusAzureStoreCore(services);
        }
    }

    private static IServiceCollection _AddTusAzureStoreCore(IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ITusAzureBlobHttpHeadersProvider, DefaultTusAzureBlobHttpHeadersProvider>();

        services.TryAddSingleton(provider => new TusAzureStore(
            provider.GetRequiredService<BlobServiceClient>(),
            provider.GetRequiredService<IOptions<TusAzureStoreOptions>>().Value,
            provider.GetRequiredService<ITusAzureBlobHttpHeadersProvider>(),
            provider.GetService<ITusFileIdProvider>(),
            provider.GetService<ILoggerFactory>(),
            provider.GetService<TimeProvider>()
        ));

        return services;
    }
}
