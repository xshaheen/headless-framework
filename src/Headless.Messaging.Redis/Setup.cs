// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Checks;
using Headless.Messaging.Configuration;
using Headless.Messaging.Redis;
using Headless.Messaging.Transport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Messaging;

/// <summary>
/// Extension members that register Redis as the message transport.
/// </summary>
/// <remarks>
/// Redis Streams provide durable, at-least-once Bus and Queue delivery. Lane-qualified stream keys
/// isolate the same logical contract name, while Bus subscriber groups and Queue replicas use Redis
/// consumer groups for fan-out and competition.
/// </remarks>
public static class SetupRedisMessaging
{
    extension(MessagingSetupBuilder setup)
    {
        /// <summary>
        /// Registers Redis Streams as the Bus and Queue transport, connecting to localhost on the default
        /// Redis port with all other options at their defaults.
        /// </summary>
        /// <returns>The same <paramref name="setup"/> builder for chaining.</returns>
        public MessagingSetupBuilder UseRedis()
        {
            return setup.UseRedis(_ => { });
        }

        /// <summary>
        /// Registers Redis Streams as the Bus and Queue transport using a StackExchange.Redis
        /// comma-delimited configuration string.
        /// </summary>
        /// <param name="connection">
        /// A StackExchange.Redis <c>ConfigurationOptions</c> configuration string
        /// (for example <c>"localhost:6379,abortConnect=false"</c>).
        /// </param>
        /// <returns>The same <paramref name="setup"/> builder for chaining.</returns>
        public MessagingSetupBuilder UseRedis(string connection)
        {
            return setup.UseRedis(opt => opt.Configuration = ConfigurationOptions.Parse(connection));
        }

        /// <summary>
        /// Registers Redis Streams as the Bus and Queue transport, binding and validating
        /// <see cref="RedisMessagingOptions"/> from configuration.
        /// </summary>
        /// <param name="config">Configuration section containing <see cref="RedisMessagingOptions"/> values.</param>
        /// <returns>The same <paramref name="setup"/> builder for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="config"/> is <see langword="null"/>.</exception>
        public MessagingSetupBuilder UseRedis(IConfiguration config)
        {
            Argument.IsNotNull(config);

            return _RegisterRedis(
                setup,
                services => services.Configure<RedisMessagingOptions, RedisMessagingOptionsValidator>(config)
            );
        }

        /// <summary>
        /// Registers Redis Streams as the Bus and Queue transport with full programmatic configuration.
        /// </summary>
        /// <param name="configure">A delegate that configures <see cref="RedisMessagingOptions"/>.</param>
        /// <returns>The same <paramref name="setup"/> builder for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure" /> is <see langword="null"/>.</exception>
        public MessagingSetupBuilder UseRedis(Action<RedisMessagingOptions> configure)
        {
            Argument.IsNotNull(configure);

            return _RegisterRedis(
                setup,
                services => services.Configure<RedisMessagingOptions, RedisMessagingOptionsValidator>(configure)
            );
        }

        /// <summary>
        /// Registers Redis Streams as the Bus and Queue transport, configuring <see cref="RedisMessagingOptions"/>
        /// with access to the resolved service provider.
        /// </summary>
        /// <param name="configure">
        /// A delegate that configures <see cref="RedisMessagingOptions"/> using the service provider
        /// (for example to resolve secrets or connection settings from DI).
        /// </param>
        /// <returns>The same <paramref name="setup"/> builder for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure" /> is <see langword="null"/>.</exception>
        public MessagingSetupBuilder UseRedis(Action<RedisMessagingOptions, IServiceProvider> configure)
        {
            Argument.IsNotNull(configure);

            return _RegisterRedis(
                setup,
                services => services.Configure<RedisMessagingOptions, RedisMessagingOptionsValidator>(configure)
            );
        }
    }

    private static MessagingSetupBuilder _RegisterRedis(
        MessagingSetupBuilder setup,
        Action<IServiceCollection> configureOptions
    )
    {
        setup.RegisterExtension(new RedisMessagingOptionsExtension(configureOptions));

        return setup;
    }

    private sealed class RedisMessagingOptionsExtension(Action<IServiceCollection> configureOptions)
        : IMessagesOptionsExtension
    {
        private readonly Action<IServiceCollection> _configureOptions = Argument.IsNotNull(configureOptions);

        public void AddServices(IServiceCollection services)
        {
            services.TryAddSingleton(new MessageQueueMarkerService("Redis"));
            services.AddMessagingProviderCapabilities(
                MessagingProviderCapabilities.Transport(
                    "Redis",
                    [MessageLane.Bus, MessageLane.Queue],
                    supportsIndependentLaneTopology: true
                )
            );
            services.AddSingleton<IRedisStreamManager, RedisStreamManager>();
            services.AddSingleton<IConsumerClientFactory, RedisConsumerClientFactory>();
            services.AddSingleton<RedisBusTransport>();
            services.AddSingleton<IBusTransport>(sp => sp.GetRequiredService<RedisBusTransport>());
            services.AddSingleton<IQueueTransport, RedisTransport>();
            services.AddSingleton<IRedisConnectionPool, RedisConnectionPool>();
            services.TryAddEnumerable(
                ServiceDescriptor.Singleton<
                    IPostConfigureOptions<RedisMessagingOptions>,
                    RedisMessagingOptionsPostConfigure
                >()
            );
            _configureOptions(services);
        }
    }

    private sealed class RedisMessagingOptionsPostConfigure : IPostConfigureOptions<RedisMessagingOptions>
    {
        public void PostConfigure(string? name, RedisMessagingOptions options)
        {
            options.Configuration ??= new ConfigurationOptions();

            if (!options.Configuration.EndPoints.Any())
            {
                options.Configuration.EndPoints.Add(IPAddress.Loopback, 0);
                options.Configuration.SetDefaultPorts();
            }
        }
    }
}
