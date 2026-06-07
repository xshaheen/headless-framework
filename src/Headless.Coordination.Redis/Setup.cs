// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Redis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using StackExchange.Redis;

namespace Headless.Coordination.Redis;

[PublicAPI]
public static class SetupRedisCoordination
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddRedisCoordination(IConfiguration configuration)
        {
            services.Configure<RedisCoordinationOptions, RedisCoordinationOptionsValidator>(configuration);

            return services._AddRedisCoordinationCore(configuration.GetSection("Headless:Coordination"));
        }

        public IServiceCollection AddRedisCoordination(Action<RedisCoordinationOptions> setupAction)
        {
            services.Configure<RedisCoordinationOptions, RedisCoordinationOptionsValidator>(setupAction);

            return services._AddRedisCoordinationCore(static _ => { });
        }

        public IServiceCollection AddRedisCoordination(Action<RedisCoordinationOptions, IServiceProvider> setupAction)
        {
            services.Configure<RedisCoordinationOptions, RedisCoordinationOptionsValidator>(setupAction);

            return services._AddRedisCoordinationCore(static _ => { });
        }

        private IServiceCollection _AddRedisCoordinationCore(IConfiguration configuration)
        {
            services.TryAddSingleton<ProviderCapabilities>(new ProviderCapabilities(FailoverEligible: true));
            services.AddCoordinationCore<RedisMembershipStore>(configuration);

            return services._AddRedisCoordinationProviderCore();
        }

        private IServiceCollection _AddRedisCoordinationCore(Action<CoordinationOptions> setupAction)
        {
            services.TryAddSingleton<ProviderCapabilities>(new ProviderCapabilities(FailoverEligible: true));
            services.AddCoordinationCore<RedisMembershipStore>(setupAction);

            return services._AddRedisCoordinationProviderCore();
        }

        private IServiceCollection _AddRedisCoordinationProviderCore()
        {
            services.TryAddKeyedSingleton(
                RedisCoordinationServiceKeys.ScriptsLoader,
                (sp, _) =>
                    new HeadlessRedisScriptsLoader(
                        sp.GetRequiredService<IConnectionMultiplexer>(),
                        sp.GetService<TimeProvider>(),
                        sp.GetService<ILogger<HeadlessRedisScriptsLoader>>()
                    )
            );

            services.TryAddSingleton<RedisMembershipStore>();
            services.TryAddSingleton<IMembershipStore>(static sp => sp.GetRequiredService<RedisMembershipStore>());
            services.AddInitializerHostedService<CoordinationRedisScriptsInitializer>();
            services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IHostedService, RedisMembershipCleanupService>(static sp =>
                    sp.GetRequiredService<RedisMembershipCleanupService>()
                )
            );
            services.TryAddSingleton<RedisMembershipCleanupService>();

            return services;
        }
    }
}
