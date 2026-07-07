// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Coordination.Redis;
using Headless.Redis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Coordination;

/// <summary>
/// Extension members on <see cref="HeadlessCoordinationSetupBuilder"/> for selecting Redis as the
/// coordination backing store.
/// </summary>
/// <remarks>
/// Requires a pre-registered <c>IConnectionMultiplexer</c> in the DI container (for example from
/// <c>AddHeadlessRedis</c>). The Redis store uses Lua scripts for atomic heartbeat and incarnation
/// allocation; scripts are loaded at startup by an initializer hosted service.
/// </remarks>
[PublicAPI]
public static class SetupRedisCoordination
{
    extension(HeadlessCoordinationSetupBuilder setup)
    {
        /// <summary>
        /// Selects Redis as the coordination backing store, binding <see cref="RedisCoordinationOptions"/>
        /// from the supplied <see cref="IConfiguration"/> section.
        /// </summary>
        /// <param name="configuration">The configuration section to bind provider options from.</param>
        /// <returns>The same builder for chaining.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="configuration"/> is <see langword="null"/>.</exception>
        public HeadlessCoordinationSetupBuilder UseRedis(IConfiguration configuration)
        {
            Argument.IsNotNull(configuration);

            setup.RegisterExtension(new RedisCoordinationOptionsExtension(configuration));

            return setup;
        }

        /// <summary>
        /// Selects Redis as the coordination backing store using the supplied options delegate.
        /// </summary>
        /// <param name="configure">Delegate that configures <see cref="RedisCoordinationOptions"/>.</param>
        /// <returns>The same builder for chaining.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="configure"/> is <see langword="null"/>.</exception>
        public HeadlessCoordinationSetupBuilder UseRedis(Action<RedisCoordinationOptions> configure)
        {
            Argument.IsNotNull(configure);

            setup.RegisterExtension(new RedisCoordinationOptionsExtension(configure));

            return setup;
        }

        /// <summary>
        /// Selects Redis as the coordination backing store using the supplied options delegate with access
        /// to the DI container.
        /// </summary>
        /// <param name="configure">Delegate that configures <see cref="RedisCoordinationOptions"/> with service-provider access.</param>
        /// <returns>The same builder for chaining.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="configure"/> is <see langword="null"/>.</exception>
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
            _configure = services =>
                services.Configure<RedisCoordinationOptions, RedisCoordinationOptionsValidator>(configure);
        }

        public RedisCoordinationOptionsExtension(Action<RedisCoordinationOptions, IServiceProvider> configure)
        {
            _configure = services =>
                services.Configure<RedisCoordinationOptions, RedisCoordinationOptionsValidator>(configure);
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
