// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Framework.Blobs.FileSystem;

[PublicAPI]
public static class FileSystemBlobSetup
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddFileSystemBlobStorage(
            Action<FileSystemBlobStorageOptions, IServiceProvider> setupAction
        )
        {
            services.Configure<FileSystemBlobStorageOptions, FileSystemBlobStorageOptionsValidator>(setupAction);

            return services._AddCore();
        }

        public IServiceCollection AddFileSystemBlobStorage(Action<FileSystemBlobStorageOptions> setupAction)
        {
            services.Configure<FileSystemBlobStorageOptions, FileSystemBlobStorageOptionsValidator>(setupAction);

            return services._AddCore();
        }

        public IServiceCollection AddFileSystemBlobStorage(IConfigurationSection config)
        {
            services.Configure<FileSystemBlobStorageOptions, FileSystemBlobStorageOptionsValidator>(config);

            return services._AddCore();
        }

        private IServiceCollection _AddCore()
        {
            services.TryAddSingleton<IBlobNamingNormalizer, CrossOsNamingNormalizer>();
            services.AddSingleton<IBlobStorage, FileSystemBlobStorage>();

            return services;
        }
    }
}
