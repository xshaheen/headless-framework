// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.Blobs.Redis;

/// <summary>Extension methods to register the Redis blob storage provider.</summary>
[PublicAPI]
public static class SetupRedisBlob
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers <see cref="RedisBlobStorage"/> as <see cref="IBlobStorage"/> using the supplied delegate to configure <see cref="RedisBlobStorageOptions"/>.
        /// </summary>
        public IServiceCollection AddRedisBlobStorage(Action<RedisBlobStorageOptions, IServiceProvider> setupAction)
        {
            services.Configure<RedisBlobStorageOptions, RedisBlobStorageOptionsValidator>(setupAction);

            return services._AddCore();
        }

        /// <summary>
        /// Registers <see cref="RedisBlobStorage"/> as <see cref="IBlobStorage"/> using the supplied delegate to configure <see cref="RedisBlobStorageOptions"/>.
        /// </summary>
        public IServiceCollection AddRedisBlobStorage(Action<RedisBlobStorageOptions> setupAction)
        {
            services.Configure<RedisBlobStorageOptions, RedisBlobStorageOptionsValidator>(setupAction);

            return services._AddCore();
        }

        /// <summary>
        /// Registers <see cref="RedisBlobStorage"/> as <see cref="IBlobStorage"/>, binding <see cref="RedisBlobStorageOptions"/> from <paramref name="config"/>.
        /// </summary>
        public IServiceCollection AddRedisBlobStorage(IConfigurationSection config)
        {
            services.Configure<RedisBlobStorageOptions, RedisBlobStorageOptionsValidator>(config);

            return services._AddCore();
        }

        private IServiceCollection _AddCore()
        {
            services.TryAddSingleton(TimeProvider.System);
            services.TryAddSingleton<IBlobNamingNormalizer, CrossOsNamingNormalizer>();
            services.AddSingleton<IBlobStorage, RedisBlobStorage>();

            return services;
        }
    }
}
