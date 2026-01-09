// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Framework.Blobs.SshNet;

[PublicAPI]
public static class SshSetup
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddSshBlobStorage(
            Action<SshBlobStorageOptions, IServiceProvider> setupAction
        )
        {
            services.Configure<SshBlobStorageOptions, SshBlobStorageOptionsValidator>(setupAction);

            return services._AddCore();
        }

        public IServiceCollection AddSshBlobStorage(
            Action<SshBlobStorageOptions> setupAction
        )
        {
            services.Configure<SshBlobStorageOptions, SshBlobStorageOptionsValidator>(setupAction);

            return services._AddCore();
        }

        public IServiceCollection AddSshBlobStorage(IConfigurationSection config)
        {
            services.Configure<SshBlobStorageOptions, SshBlobStorageOptionsValidator>(config);

            return services._AddCore();
        }

        private IServiceCollection _AddCore()
        {
            services.TryAddSingleton<IBlobNamingNormalizer, CrossOsNamingNormalizer>();
            services.AddSingleton<IBlobStorage, SshBlobStorage>();

            return services;
        }
    }
}
