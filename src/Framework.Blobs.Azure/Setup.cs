// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Framework.Blobs.Azure;

[PublicAPI]
public static class AddAzureBlobExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddAzureBlobStorage(Action<AzureStorageOptions, IServiceProvider> setupAction)
        {
            services.Configure<AzureStorageOptions, AzureStorageOptionsValidator>(setupAction);

            return services._AddCore();
        }

        public IServiceCollection AddAzureBlobStorage(Action<AzureStorageOptions> setupAction)
        {
            services.Configure<AzureStorageOptions, AzureStorageOptionsValidator>(setupAction);

            return services._AddCore();
        }

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
