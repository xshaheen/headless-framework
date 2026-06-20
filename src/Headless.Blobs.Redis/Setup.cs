// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Serializer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

#pragma warning disable CA1708 // multiple extension blocks emit marker members differing only by case
namespace Headless.Blobs.Redis;

[PublicAPI]
public static class SetupRedisBlob
{
    extension(HeadlessBlobsSetupBuilder setup)
    {
        /// <summary>Uses Redis as the default (unkeyed) <see cref="IBlobStorage"/>.</summary>
        public HeadlessBlobsSetupBuilder UseRedis(Action<RedisBlobStorageOptions> setupAction)
        {
            Argument.IsNotNull(setupAction);

            setup.RegisterDefaultProvider(services =>
            {
                services.Configure<RedisBlobStorageOptions, RedisBlobStorageOptionsValidator>(setupAction);
                services._AddBlobsDefaultCore();
            });

            return setup;
        }

        /// <summary>Uses Redis as the default (unkeyed) <see cref="IBlobStorage"/> with service provider-aware configuration.</summary>
        public HeadlessBlobsSetupBuilder UseRedis(Action<RedisBlobStorageOptions, IServiceProvider> setupAction)
        {
            Argument.IsNotNull(setupAction);

            setup.RegisterDefaultProvider(services =>
            {
                services.Configure<RedisBlobStorageOptions, RedisBlobStorageOptionsValidator>(setupAction);
                services._AddBlobsDefaultCore();
            });

            return setup;
        }

        /// <summary>Uses Redis as the default (unkeyed) <see cref="IBlobStorage"/>, binding options from configuration.</summary>
        public HeadlessBlobsSetupBuilder UseRedis(IConfiguration configuration)
        {
            Argument.IsNotNull(configuration);

            setup.RegisterDefaultProvider(services =>
            {
                services.Configure<RedisBlobStorageOptions, RedisBlobStorageOptionsValidator>(configuration);
                services._AddBlobsDefaultCore();
            });

            return setup;
        }
    }

    extension(HeadlessBlobInstanceBuilder instance)
    {
        /// <summary>Uses Redis for this named instance, resolvable as a keyed <see cref="IBlobStorage"/> or through <see cref="IBlobStorageProvider"/>.</summary>
        public HeadlessBlobInstanceBuilder UseRedis(Action<RedisBlobStorageOptions> setupAction)
        {
            Argument.IsNotNull(setupAction);

            var name = instance.Name;

            instance.RegisterProvider(services =>
            {
                services.Configure<RedisBlobStorageOptions, RedisBlobStorageOptionsValidator>(setupAction, name);
                services._AddBlobsNamedCore(name);
            });

            return instance;
        }

        /// <summary>Uses Redis for this named instance with service provider-aware configuration.</summary>
        public HeadlessBlobInstanceBuilder UseRedis(Action<RedisBlobStorageOptions, IServiceProvider> setupAction)
        {
            Argument.IsNotNull(setupAction);

            var name = instance.Name;

            instance.RegisterProvider(services =>
            {
                services.Configure<RedisBlobStorageOptions, RedisBlobStorageOptionsValidator>(setupAction, name);
                services._AddBlobsNamedCore(name);
            });

            return instance;
        }

        /// <summary>Uses Redis for this named instance, binding options from configuration.</summary>
        public HeadlessBlobInstanceBuilder UseRedis(IConfiguration configuration)
        {
            Argument.IsNotNull(configuration);

            var name = instance.Name;

            instance.RegisterProvider(services =>
            {
                services.Configure<RedisBlobStorageOptions, RedisBlobStorageOptionsValidator>(configuration, name);
                services._AddBlobsNamedCore(name);
            });

            return instance;
        }
    }

    extension(IServiceCollection services)
    {
        private IServiceCollection _AddBlobsDefaultCore()
        {
            services.AddBlobStorageProvider();
            services._AddBlobsCoreShared();

            services.AddSingleton<IBlobStorage>(serviceProvider => new RedisBlobStorage(
                serviceProvider.GetRequiredService<IOptions<RedisBlobStorageOptions>>(),
                serviceProvider.GetRequiredService<IJsonSerializer>(),
                new CrossOsNamingNormalizer(),
                serviceProvider.GetService<TimeProvider>() ?? TimeProvider.System
            ));

            return services;
        }

        private IServiceCollection _AddBlobsNamedCore(string name)
        {
            services.AddBlobStorageProvider();
            services._AddBlobsCoreShared();

            services.AddKeyedSingleton<IBlobStorage>(
                name,
                (serviceProvider, _) =>
                    new RedisBlobStorage(
                        Options.Create(
                            serviceProvider.GetRequiredService<IOptionsMonitor<RedisBlobStorageOptions>>().Get(name)
                        ),
                        serviceProvider.GetRequiredService<IJsonSerializer>(),
                        new CrossOsNamingNormalizer(),
                        serviceProvider.GetService<TimeProvider>() ?? TimeProvider.System
                    )
            );

            return services;
        }

        private IServiceCollection _AddBlobsCoreShared()
        {
            services.TryAddSingleton(TimeProvider.System);
            services.TryAddSingleton<IJsonOptionsProvider>(new DefaultJsonOptionsProvider());
            services.TryAddSingleton<IJsonSerializer>(sp => new SystemJsonSerializer(
                sp.GetRequiredService<IJsonOptionsProvider>()
            ));

            return services;
        }
    }
}
