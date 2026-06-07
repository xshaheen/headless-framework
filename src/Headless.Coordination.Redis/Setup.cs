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
        private readonly IConfiguration? _configuration;
        private readonly Action<RedisCoordinationOptions>? _configure;
        private readonly Action<RedisCoordinationOptions, IServiceProvider>? _configureWithServices;

        public RedisCoordinationOptionsExtension(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public RedisCoordinationOptionsExtension(Action<RedisCoordinationOptions> configure)
        {
            _configure = configure;
        }

        public RedisCoordinationOptionsExtension(Action<RedisCoordinationOptions, IServiceProvider> configure)
        {
            _configureWithServices = configure;
        }

        public void AddServices(IServiceCollection services)
        {
            if (_configuration is not null)
            {
                services.Configure<RedisCoordinationOptions, RedisCoordinationOptionsValidator>(_configuration);
            }
            else if (_configure is not null)
            {
                services.Configure<RedisCoordinationOptions, RedisCoordinationOptionsValidator>(_configure);
            }
            else
            {
                services.Configure<RedisCoordinationOptions, RedisCoordinationOptionsValidator>(
                    _configureWithServices
                );
            }

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
