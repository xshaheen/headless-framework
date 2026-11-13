// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure.Storage.Blobs;
using Headless.Checks;
using Headless.Hosting;
using Headless.Tus.Internal;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using tusdotnet.Interfaces;

namespace Headless.Tus;

/// <summary>Extension methods to select the Azure Blob Storage TUS store.</summary>
[PublicAPI]
public static class SetupTusAzure
{
    extension(HeadlessTusSetupBuilder setup)
    {
        /// <summary>
        /// Uses <see cref="TusAzureStore"/> as the host's tus store, with options bound from
        /// <paramref name="configuration"/> and validated at startup.
        /// </summary>
        /// <param name="configuration">The configuration section to bind <see cref="TusAzureStoreOptions"/> from.</param>
        /// <returns>The builder for chaining.</returns>
        /// <remarks>
        /// Requires a <see cref="BlobServiceClient"/> registration (for example via
        /// <c>Microsoft.Extensions.Azure</c>'s <c>AddAzureClients</c>). An
        /// <c>ITusAzureBlobHttpHeadersProvider</c> or <c>ITusFileIdProvider</c> the application registers is
        /// honored; otherwise the defaults apply. The container is registered as a provisioned resource of kind
        /// <c>AzureBlob</c>: a deploy step creates it with <c>resources provision</c>, and a starting host holds
        /// readiness until it exists.
        /// </remarks>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="configuration"/> is <see langword="null"/>.</exception>
        public HeadlessTusSetupBuilder UseAzure(IConfiguration configuration)
        {
            Argument.IsNotNull(configuration);

            setup.RegisterStore<TusAzureStore>(services =>
            {
                services.Configure<TusAzureStoreOptions, TusAzureStoreOptionsValidator>(configuration);
                _AddStoreCore(services);
            });

            return setup;
        }

        /// <inheritdoc cref="UseAzure(HeadlessTusSetupBuilder, IConfiguration)"/>
        /// <param name="setupAction">Delegate configuring <see cref="TusAzureStoreOptions"/>.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="setupAction"/> is <see langword="null"/>.</exception>
        public HeadlessTusSetupBuilder UseAzure(Action<TusAzureStoreOptions> setupAction)
        {
            Argument.IsNotNull(setupAction);

            setup.RegisterStore<TusAzureStore>(services =>
            {
                services.Configure<TusAzureStoreOptions, TusAzureStoreOptionsValidator>(setupAction);
                _AddStoreCore(services);
            });

            return setup;
        }

        /// <inheritdoc cref="UseAzure(HeadlessTusSetupBuilder, IConfiguration)"/>
        /// <param name="setupAction">Delegate configuring <see cref="TusAzureStoreOptions"/> with access to the service provider.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="setupAction"/> is <see langword="null"/>.</exception>
        public HeadlessTusSetupBuilder UseAzure(Action<TusAzureStoreOptions, IServiceProvider> setupAction)
        {
            Argument.IsNotNull(setupAction);

            setup.RegisterStore<TusAzureStore>(services =>
            {
                services.Configure<TusAzureStoreOptions, TusAzureStoreOptionsValidator>(setupAction);
                _AddStoreCore(services);
            });

            return setup;
        }
    }

    private static void _AddStoreCore(IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ITusAzureBlobHttpHeadersProvider, DefaultTusAzureBlobHttpHeadersProvider>();

        services.AddSingleton(provider => new TusAzureStore(
            provider.GetRequiredService<BlobServiceClient>(),
            provider.GetRequiredService<IOptions<TusAzureStoreOptions>>().Value,
            provider.GetRequiredService<ITusAzureBlobHttpHeadersProvider>(),
            provider.GetService<ITusFileIdProvider>(),
            provider.GetService<ILoggerFactory>(),
            provider.GetService<TimeProvider>()
        ));

        services.AddResourceProvisioner(static provider => new TusAzureContainerProvisioner(
            provider.GetRequiredService<BlobServiceClient>(),
            provider.GetRequiredService<IOptions<TusAzureStoreOptions>>().Value
        ));
    }
}
