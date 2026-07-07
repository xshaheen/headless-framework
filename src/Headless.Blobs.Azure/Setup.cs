// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure.Storage.Blobs;
using Headless.Abstractions;
using Headless.Blobs.Azure;
using Headless.Checks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Blobs;

/// <summary>
/// Extension members that contribute the Azure Blob Storage provider to the Headless blob setup builder.
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="BlobServiceClient"/> is resolved from DI by default. When no <c>clientFactory</c> is supplied,
/// a <see cref="BlobServiceClient"/> must be registered in DI before the store is resolved; otherwise resolution
/// throws when the store is first used, not at startup (<c>ValidateOnStart</c> cannot detect a missing DI
/// prerequisite). If you need different Azure accounts for different named stores, supply a per-store
/// <c>clientFactory</c> delegate:
/// </para>
/// <code>
/// services.AddHeadlessBlobs(blobs =>
/// {
///     // Default store — uses the ambient BlobServiceClient from DI:
///     blobs.UseAzure(options => options.MaxBulkParallelism = 8);
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
                _AddBlobsDefaultCore(services, clientFactory);
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
                _AddBlobsDefaultCore(services, clientFactory);
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
                _AddBlobsDefaultCore(services, clientFactory);
            });

            return setup;
        }
    }

    private static IServiceCollection _AddBlobsDefaultCore(
        IServiceCollection services,
        Func<IServiceProvider, BlobServiceClient>? clientFactory
    )
    {
        services.AddSingleton<IBlobStorage>(sp =>
        {
            var mimeTypeProvider = sp.GetRequiredService<IMimeTypeProvider>();
            var clock = sp.GetRequiredService<IClock>();
            var options = sp.GetRequiredService<IOptions<AzureStorageOptions>>();
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

        // Container lifecycle is a separately-resolved capability (not a cast from IBlobStorage), so the Azure
        // provider registers a dedicated manager. It shares the storage's BlobServiceClient (ambient DI or the
        // supplied client factory) but never disposes it (U12 / KTD5).
        services.AddSingleton<IBlobContainerManager>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<AzureStorageOptions>>().Value;
            var client = clientFactory is not null ? clientFactory(sp) : sp.GetRequiredService<BlobServiceClient>();

            return new AzureBlobContainerManager(
                client,
                new AzureBlobNamingNormalizer(),
                options.ContainerPublicAccessType
            );
        });

        return services;
    }

    internal static IServiceCollection AddBlobsNamedCore(
        IServiceCollection services,
        string name,
        Func<IServiceProvider, BlobServiceClient>? clientFactory
    )
    {
        services.AddKeyedSingleton<IBlobStorage>(
            name,
            (sp, _) =>
            {
                var mimeTypeProvider = sp.GetRequiredService<IMimeTypeProvider>();
                var clock = sp.GetRequiredService<IClock>();
                var options = Options.Create(sp.GetRequiredService<IOptionsMonitor<AzureStorageOptions>>().Get(name));
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
            }
        );

        services.AddKeyedSingleton<IPresignedUrlBlobStorage>(
            name,
            (sp, _) => (IPresignedUrlBlobStorage)sp.GetRequiredKeyedService<IBlobStorage>(name)
        );

        // Keyed container-management capability for this named instance, registered as a separate service (not a
        // cast from the keyed storage) so it shares the per-instance BlobServiceClient and per-instance options.
        services.AddKeyedSingleton<IBlobContainerManager>(
            name,
            (sp, _) =>
            {
                var options = sp.GetRequiredService<IOptionsMonitor<AzureStorageOptions>>().Get(name);
                var client = clientFactory is not null ? clientFactory(sp) : sp.GetRequiredService<BlobServiceClient>();

                return new AzureBlobContainerManager(
                    client,
                    new AzureBlobNamingNormalizer(),
                    options.ContainerPublicAccessType
                );
            }
        );

        return services;
    }
}

/// <summary>
/// Extension members that contribute the Azure Blob Storage provider as a named store on
/// <see cref="HeadlessBlobInstanceBuilder"/>. See <see cref="SetupAzureBlob"/> for the client-resolution and
/// presigned-forwarding behavior.
/// </summary>
[PublicAPI]
public static class SetupAzureBlobNamed
{
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
                SetupAzureBlob.AddBlobsNamedCore(services, name, clientFactory);
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
                SetupAzureBlob.AddBlobsNamedCore(services, name, clientFactory);
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
                SetupAzureBlob.AddBlobsNamedCore(services, name, clientFactory);
            });

            return instance;
        }
    }
}
