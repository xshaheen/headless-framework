// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.Blobs.SshNet;

/// <summary>Extension methods to register the SFTP/SSH blob storage provider.</summary>
[PublicAPI]
public static class SetupSsh
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers <see cref="SshBlobStorage"/> as <see cref="IBlobStorage"/> using the supplied delegate to configure <see cref="SshBlobStorageOptions"/>.
        /// </summary>
        public IServiceCollection AddSshBlobStorage(Action<SshBlobStorageOptions, IServiceProvider> setupAction)
        {
            services.Configure<SshBlobStorageOptions, SshBlobStorageOptionsValidator>(setupAction);

            return services._AddCore();
        }

        /// <summary>
        /// Registers <see cref="SshBlobStorage"/> as <see cref="IBlobStorage"/> using the supplied delegate to configure <see cref="SshBlobStorageOptions"/>.
        /// </summary>
        public IServiceCollection AddSshBlobStorage(Action<SshBlobStorageOptions> setupAction)
        {
            services.Configure<SshBlobStorageOptions, SshBlobStorageOptionsValidator>(setupAction);

            return services._AddCore();
        }

        /// <summary>
        /// Registers <see cref="SshBlobStorage"/> as <see cref="IBlobStorage"/>, binding <see cref="SshBlobStorageOptions"/> from <paramref name="config"/>.
        /// </summary>
        public IServiceCollection AddSshBlobStorage(IConfigurationSection config)
        {
            services.Configure<SshBlobStorageOptions, SshBlobStorageOptionsValidator>(config);

            return services._AddCore();
        }

        private IServiceCollection _AddCore()
        {
            services.TryAddSingleton<IBlobNamingNormalizer, CrossOsNamingNormalizer>();
            services.AddSingleton<SftpClientPool>();
            services.AddSingleton<IBlobStorage, SshBlobStorage>();

            return services;
        }
    }
}
