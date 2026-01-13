// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Framework.Blobs.Azure;

/// <summary>
/// Extension methods to register Azure Blob Storage services.
/// <para>
/// <b>Important:</b> You must register <c>BlobServiceClient</c> in DI before calling these methods.
/// </para>
/// <example>
/// <code>
/// // Using connection string:
/// services.AddSingleton(new BlobServiceClient(connectionString));
///
/// // Using Azure Identity (DefaultAzureCredential):
/// services.AddSingleton(new BlobServiceClient(new Uri(storageUri), new DefaultAzureCredential()));
///
/// // Using Aspire Azure Storage integration:
/// builder.AddAzureBlobClient("blobs");
///
/// // Then register blob storage:
/// services.AddAzureBlobStorage(options => { });
/// </code>
/// </example>
/// </summary>
[PublicAPI]
public static class AddAzureBlobExtensions
{
    extension(IServiceCollection services)
    {
        /// <inheritdoc cref="AddAzureBlobExtensions"/>
        public IServiceCollection AddAzureBlobStorage(Action<AzureStorageOptions, IServiceProvider> setupAction)
        {
            services.Configure<AzureStorageOptions, AzureStorageOptionsValidator>(setupAction);

            return services._AddCore();
        }

        /// <inheritdoc cref="AddAzureBlobExtensions"/>
        public IServiceCollection AddAzureBlobStorage(Action<AzureStorageOptions> setupAction)
        {
            services.Configure<AzureStorageOptions, AzureStorageOptionsValidator>(setupAction);

            return services._AddCore();
        }

        /// <inheritdoc cref="AddAzureBlobExtensions"/>
        public IServiceCollection AddAzureBlobStorage(IConfiguration config)
        {
            services.Configure<AzureStorageOptions, AzureStorageOptionsValidator>(config);

            return services._AddCore();
        }

        private IServiceCollection _AddCore()
        {
            services.TryAddSingleton<IBlobNamingNormalizer, AzureBlobNamingNormalizer>();
            services.AddSingleton<IBlobStorage, AzureBlobStorage>();

            return services;
        }
    }
}
