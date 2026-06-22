// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure.Storage.Blobs;
using Headless.Abstractions;
using Headless.Checks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

#pragma warning disable CA1708 // multiple extension blocks emit marker members differing only by case
namespace Headless.Blobs.Azure;

/// <summary>
/// Extension members that contribute the Azure Blob Storage provider to the Headless blob setup builder.
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="BlobServiceClient"/> is resolved from DI by default. If you need different Azure accounts
/// for different named stores, supply a per-store <c>clientFactory</c> delegate:
/// </para>
/// <code>
/// services.AddHeadlessBlobs(blobs =>
/// {
///     // Default store — uses the ambient BlobServiceClient from DI:
///     blobs.UseAzure(options => options.AutoCreateContainer = true);
///
///     // Named store — uses a dedicated client for a second account:
///     blobs.AddNamed("archive", instance => instance.UseAzure(
///         setupAction: options => { },
///         clientFactory: sp => new BlobServiceClient(archiveConnectionString)));
/// });
/// </code>
/// <para>
/// Named stores also expose keyed <see cref="IPresignedUrlBlobStorage"/> forwards. Default presigned support is
/// discovered by casting the resolved <see cref="IBlobStorage"/>.
/// </para>
/// </remarks>
[PublicAPI]
public static class SetupAzureBlob
{
    extension(HeadlessBlobsSetupBuilder setup)
    {
        /// <summary>
        /// Uses Azure Blob Storage as the default (unkeyed) <see cref="IBlobStorage"/>.
        /// </summary>
        /// <param name="setupAction">Options configuration delegate.</param>
        /// <param name="clientFactory">
        /// Optional factory that supplies the <see cref="BlobServiceClient"/> for the default store. When
        /// <see langword="null"/> (the default), the ambient <see cref="BlobServiceClient"/> registered in DI is
        /// used. Provide a factory to point the default store at a specific storage account.
        /// </param>
        public HeadlessBlobsSetupBuilder UseAzure(
            Action<AzureStorageOptions> setupAction,
            Func<IServiceProvider, BlobServiceClient>? clientFactory = null
        )
        {
            Argument.IsNotNull(setupAction);

            setup.RegisterDefaultProvider(services =>
            {
                services.Configure<AzureStorageOptions, AzureStorageOptionsValidator>(setupAction);
                services._AddBlobsDefaultCore(clientFactory);
            });

            return setup;
        }

        /// <summary>
        /// Uses Azure Blob Storage as the default (unkeyed) <see cref="IBlobStorage"/> with
        /// service-provider-aware configuration.
        /// </summary>
        /// <param name="setupAction">Options configuration delegate.</param>
        /// <param name="clientFactory">
        /// Optional factory that supplies the <see cref="BlobServiceClient"/> for the default store. When
        /// <see langword="null"/> (the default), the ambient <see cref="BlobServiceClient"/> registered in DI is
        /// used. Provide a factory to point the default store at a specific storage account.
        /// </param>
        public HeadlessBlobsSetupBuilder UseAzure(
            Action<AzureStorageOptions, IServiceProvider> setupAction,
            Func<IServiceProvider, BlobServiceClient>? clientFactory = null
        )
        {
            Argument.IsNotNull(setupAction);

            setup.RegisterDefaultProvider(services =>
            {
                services.Configure<AzureStorageOptions, AzureStorageOptionsValidator>(setupAction);
                services._AddBlobsDefaultCore(clientFactory);
            });

            return setup;
        }

        /// <summary>
        /// Uses Azure Blob Storage as the default (unkeyed) <see cref="IBlobStorage"/>, binding options
        /// from configuration.
        /// </summary>
        /// <param name="configuration">Configuration section to bind.</param>
        /// <param name="clientFactory">
        /// Optional factory that supplies the <see cref="BlobServiceClient"/> for the default store. When
        /// <see langword="null"/> (the default), the ambient <see cref="BlobServiceClient"/> registered in DI is
        /// used. Provide a factory to point the default store at a specific storage account.
        /// </param>
        public HeadlessBlobsSetupBuilder UseAzure(
            IConfiguration configuration,
            Func<IServiceProvider, BlobServiceClient>? clientFactory = null
        )
        {
            Argument.IsNotNull(configuration);

            setup.RegisterDefaultProvider(services =>
            {
                services.Configure<AzureStorageOptions, AzureStorageOptionsValidator>(configuration);
                services._AddBlobsDefaultCore(clientFactory);
            });

            return setup;
        }
    }

    extension(HeadlessBlobInstanceBuilder instance)
    {
        /// <summary>
        /// Uses Azure Blob Storage for this named instance, resolvable as a keyed <see cref="IBlobStorage"/>
        /// or through <see cref="IBlobStorageProvider"/>.
        /// </summary>
        /// <param name="setupAction">Options configuration delegate.</param>
        /// <param name="clientFactory">
        /// Optional factory that supplies the <see cref="BlobServiceClient"/> for this specific store.
        /// When <see langword="null"/> (the default), the ambient <see cref="BlobServiceClient"/> registered in
        /// DI is used. Provide a factory when two named Azure stores target different storage accounts.
        /// </param>
        public HeadlessBlobInstanceBuilder UseAzure(
            Action<AzureStorageOptions> setupAction,
            Func<IServiceProvider, BlobServiceClient>? clientFactory = null
        )
        {
            Argument.IsNotNull(setupAction);

            var name = instance.Name;

            instance.RegisterProvider(services =>
            {
                services.Configure<AzureStorageOptions, AzureStorageOptionsValidator>(setupAction, name);
                services._AddBlobsNamedCore(name, clientFactory);
            });

            return instance;
        }

        /// <summary>
        /// Uses Azure Blob Storage for this named instance with service-provider-aware configuration.
        /// </summary>
        /// <param name="setupAction">Options configuration delegate.</param>
        /// <param name="clientFactory">
        /// Optional factory that supplies the <see cref="BlobServiceClient"/> for this specific store.
        /// When <see langword="null"/> (the default), the ambient <see cref="BlobServiceClient"/> registered in
        /// DI is used. Provide a factory when two named Azure stores target different storage accounts.
        /// </param>
        public HeadlessBlobInstanceBuilder UseAzure(
            Action<AzureStorageOptions, IServiceProvider> setupAction,
            Func<IServiceProvider, BlobServiceClient>? clientFactory = null
        )
        {
            Argument.IsNotNull(setupAction);

            var name = instance.Name;

            instance.RegisterProvider(services =>
            {
                services.Configure<AzureStorageOptions, AzureStorageOptionsValidator>(setupAction, name);
                services._AddBlobsNamedCore(name, clientFactory);
            });

            return instance;
        }

        /// <summary>
        /// Uses Azure Blob Storage for this named instance, binding options from configuration.
        /// </summary>
        /// <param name="configuration">Configuration section to bind.</param>
        /// <param name="clientFactory">
        /// Optional factory that supplies the <see cref="BlobServiceClient"/> for this specific store.
        /// When <see langword="null"/> (the default), the ambient <see cref="BlobServiceClient"/> registered in
        /// DI is used. Provide a factory when two named Azure stores target different storage accounts.
        /// </param>
        public HeadlessBlobInstanceBuilder UseAzure(
            IConfiguration configuration,
            Func<IServiceProvider, BlobServiceClient>? clientFactory = null
        )
        {
            Argument.IsNotNull(configuration);

            var name = instance.Name;

            instance.RegisterProvider(services =>
            {
                services.Configure<AzureStorageOptions, AzureStorageOptionsValidator>(configuration, name);
                services._AddBlobsNamedCore(name, clientFactory);
            });

            return instance;
        }
    }

    extension(IServiceCollection services)
    {
        private IServiceCollection _AddBlobsDefaultCore(Func<IServiceProvider, BlobServiceClient>? clientFactory)
        {
            services.AddBlobStorageProvider();

            services.AddSingleton<IBlobStorage>(sp =>
            {
                var mimeTypeProvider = sp.GetRequiredService<IMimeTypeProvider>();
                var clock = sp.GetRequiredService<IClock>();
                var options = sp.GetRequiredService<IOptions<AzureStorageOptions>>();
                _ = options.Value;
                var logger = sp.GetService<ILogger<AzureBlobStorage>>() ?? NullLogger<AzureBlobStorage>.Instance;
                var client = clientFactory is not null ? clientFactory(sp) : sp.GetRequiredService<BlobServiceClient>();

                return new AzureBlobStorage(
                    client,
                    mimeTypeProvider,
                    clock,
                    options,
                    new AzureBlobNamingNormalizer(),
                    logger
                );
            });

            return services;
        }

        private IServiceCollection _AddBlobsNamedCore(
            string name,
            Func<IServiceProvider, BlobServiceClient>? clientFactory
        )
        {
            services.AddBlobStorageProvider();

            services.AddKeyedSingleton<IBlobStorage>(
                name,
                (sp, _) =>
                {
                    var mimeTypeProvider = sp.GetRequiredService<IMimeTypeProvider>();
                    var clock = sp.GetRequiredService<IClock>();
                    var options = Options.Create(
                        sp.GetRequiredService<IOptionsMonitor<AzureStorageOptions>>().Get(name)
                    );
                    var logger = sp.GetService<ILogger<AzureBlobStorage>>() ?? NullLogger<AzureBlobStorage>.Instance;
                    var client = clientFactory is not null
                        ? clientFactory(sp)
                        : sp.GetRequiredService<BlobServiceClient>();

                    return new AzureBlobStorage(
                        client,
                        mimeTypeProvider,
                        clock,
                        options,
                        new AzureBlobNamingNormalizer(),
                        logger
                    );
                }
            );

            services.AddKeyedSingleton<IPresignedUrlBlobStorage>(
                name,
                (sp, _) => (IPresignedUrlBlobStorage)sp.GetRequiredKeyedService<IBlobStorage>(name)
            );

            return services;
        }
    }
}
