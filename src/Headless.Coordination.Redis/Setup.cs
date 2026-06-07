// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Redis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Headless.Coordination.Redis;

[PublicAPI]
public static class SetupRedisCoordination
{
    extension(HeadlessCoordinationSetupBuilder setup)
    {
        public HeadlessCoordinationSetupBuilder UseRedis(IConfiguration configuration)
        {
            Argument.IsNotNull(configuration);

            setup.RegisterExtension(new RedisCoordinationOptionsExtension(configuration));

            return setup;
        }

        public HeadlessCoordinationSetupBuilder UseRedis(Action<RedisCoordinationOptions> configure)
        {
            Argument.IsNotNull(configure);

            setup.RegisterExtension(new RedisCoordinationOptionsExtension(configure));

            return setup;
        }

        public HeadlessCoordinationSetupBuilder UseRedis(Action<RedisCoordinationOptions, IServiceProvider> configure)
        {
            Argument.IsNotNull(configure);

            setup.RegisterExtension(new RedisCoordinationOptionsExtension(configure));

            return setup;
        }
    }

    private sealed class RedisCoordinationOptionsExtension : ICoordinationProviderOptionsExtension
    {
        private readonly Action<IServiceCollection> _configure;

        public RedisCoordinationOptionsExtension(IConfiguration configuration)
        {
            _configure = services =>
                services.Configure<RedisCoordinationOptions, RedisCoordinationOptionsValidator>(configuration);
        }

        public RedisCoordinationOptionsExtension(Action<RedisCoordinationOptions> configure)
        {
            _configure = services => services.Configure<RedisCoordinationOptions, RedisCoordinationOptionsValidator>(configure);
        }

        public RedisCoordinationOptionsExtension(Action<RedisCoordinationOptions, IServiceProvider> configure)
        {
            _configure = services => services.Configure<RedisCoordinationOptions, RedisCoordinationOptionsValidator>(configure);
        }

        public void AddServices(IServiceCollection services)
        {
            _configure(services);
            services.AddCoordinationCore<RedisMembershipStore>();
            _AddRedisCoordinationProviderCore(services);
        }
    }

    private static void _AddRedisCoordinationProviderCore(IServiceCollection services)
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

        services.TryAddSingleton<IMembershipStore>(static sp => sp.GetRequiredService<RedisMembershipStore>());
        services.AddInitializerHostedService<CoordinationRedisScriptsInitializer>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, RedisMembershipCleanupService>(static sp =>
                sp.GetRequiredService<RedisMembershipCleanupService>()
            )
        );
        services.TryAddSingleton<RedisMembershipCleanupService>();
    }
}
