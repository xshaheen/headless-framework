// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.Blobs.FileSystem;

/// <summary>Extension methods to register the file-system blob storage provider.</summary>
[PublicAPI]
public static class SetupFileSystemBlob
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers <see cref="FileSystemBlobStorage"/> as <see cref="IBlobStorage"/> using the supplied delegate to configure <see cref="FileSystemBlobStorageOptions"/>.
        /// </summary>
        public IServiceCollection AddFileSystemBlobStorage(
            Action<FileSystemBlobStorageOptions, IServiceProvider> setupAction
        )
        {
            services.Configure<FileSystemBlobStorageOptions, FileSystemBlobStorageOptionsValidator>(setupAction);

            return services._AddCore();
        }

        /// <summary>
        /// Registers <see cref="FileSystemBlobStorage"/> as <see cref="IBlobStorage"/> using the supplied delegate to configure <see cref="FileSystemBlobStorageOptions"/>.
        /// </summary>
        public IServiceCollection AddFileSystemBlobStorage(Action<FileSystemBlobStorageOptions> setupAction)
        {
            services.Configure<FileSystemBlobStorageOptions, FileSystemBlobStorageOptionsValidator>(setupAction);

            return services._AddCore();
        }

        /// <summary>
        /// Registers <see cref="FileSystemBlobStorage"/> as <see cref="IBlobStorage"/>, binding <see cref="FileSystemBlobStorageOptions"/> from <paramref name="config"/>.
        /// </summary>
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
