// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.Blobs.Redis;

[PublicAPI]
public static class RedisBlobSetup
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddRedisBlobStorage(Action<RedisBlobStorageOptions, IServiceProvider> setupAction)
        {
            services.Configure<RedisBlobStorageOptions, RedisBlobStorageOptionsValidator>(setupAction);

            return services._AddCore();
        }

        public IServiceCollection AddRedisBlobStorage(Action<RedisBlobStorageOptions> setupAction)
        {
            services.Configure<RedisBlobStorageOptions, RedisBlobStorageOptionsValidator>(setupAction);

            return services._AddCore();
        }

        public IServiceCollection AddRedisBlobStorage(IConfigurationSection config)
        {
            services.Configure<RedisBlobStorageOptions, RedisBlobStorageOptionsValidator>(config);

            return services._AddCore();
        }

        private IServiceCollection _AddCore()
        {
            services.TryAddSingleton<IBlobNamingNormalizer, CrossOsNamingNormalizer>();
            services.AddSingleton<IBlobStorage, RedisBlobStorage>();

            return services;
        }
    }
}
