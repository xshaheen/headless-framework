// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Checks;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Transport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Headless.Messaging.Redis;

/// <summary>
/// Extension members that register Redis as the message transport.
/// </summary>
/// <remarks>
/// Two independent Redis transport modes are available:
/// <list type="bullet">
/// <item>
///   <term><c>UseRedis()</c> / <c>UseRedis(string)</c> / <c>UseRedis(Action)</c></term>
///   <description>
///     Redis Streams — durable, at-least-once, consumer-group-based queue transport. Supports
///     point-to-point messaging with acknowledgement and pending-message redelivery.
///   </description>
/// </item>
/// <item>
///   <term><c>UseRedisPubSub()</c> / <c>UseRedisPubSub(string)</c> / <c>UseRedisPubSub(Action)</c></term>
///   <description>
///     Redis Pub/Sub — at-most-once fan-out bus transport. Messages sent while no subscriber is
///     connected are silently dropped. Use only where occasional loss is acceptable.
///   </description>
/// </item>
/// </list>
/// Both modes can be registered simultaneously: streams handle queue (point-to-point) delivery
/// and Pub/Sub handles bus (fan-out) delivery.
/// </remarks>
public static class SetupRedisMessaging
{
    extension(MessagingSetupBuilder setup)
    {
        /// <summary>
        /// Registers Redis Streams as the queue transport, connecting to localhost on the default
        /// Redis port with all other options at their defaults.
        /// </summary>
        /// <returns>The same <paramref name="setup"/> builder for chaining.</returns>
        public MessagingSetupBuilder UseRedis()
        {
            return setup.UseRedis(_ => { });
        }

        /// <summary>
        /// Registers Redis Streams as the queue transport using a StackExchange.Redis
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
        /// Registers Redis Streams as the queue transport, binding and validating
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
        /// Registers Redis Streams as the queue transport with full programmatic configuration.
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
        /// Registers Redis Streams as the queue transport, configuring <see cref="RedisMessagingOptions"/>
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

        /// <summary>
        /// Use Redis Pub/Sub as the bus (fan-out) transport with default options.
        /// </summary>
        /// <remarks>
        /// <b>At-most-once delivery:</b> Redis Pub/Sub drops messages when no subscriber is connected at publish
        /// time and provides no built-in retry or dead-letter support. Configure <see cref="RedisPubSubMessagingOptions.OnDispatchFailed"/>
        /// to handle or record failed dispatches.
        /// </remarks>
        public MessagingSetupBuilder UseRedisPubSub()
        {
            return setup.UseRedisPubSub(_ => { });
        }

        /// <summary>
        /// Use Redis Pub/Sub as the bus (fan-out) transport with the given connection string.
        /// </summary>
        /// <param name="connection">A StackExchange.Redis comma-delimited configuration string.</param>
        /// <remarks>
        /// <b>At-most-once delivery:</b> Redis Pub/Sub drops messages when no subscriber is connected at publish
        /// time and provides no built-in retry or dead-letter support. Configure <see cref="RedisPubSubMessagingOptions.OnDispatchFailed"/>
        /// to handle or record failed dispatches.
        /// </remarks>
        public MessagingSetupBuilder UseRedisPubSub(string connection)
        {
            return setup.UseRedisPubSub(opt => opt.Configuration = ConfigurationOptions.Parse(connection));
        }

        /// <summary>
        /// Use Redis Pub/Sub as the bus (fan-out) transport, binding and validating
        /// <see cref="RedisPubSubMessagingOptions"/> from configuration.
        /// </summary>
        /// <param name="config">Configuration section containing <see cref="RedisPubSubMessagingOptions"/> values.</param>
        /// <exception cref="ArgumentNullException"><paramref name="config"/> is <see langword="null"/>.</exception>
        /// <remarks>
        /// <b>At-most-once delivery:</b> Redis Pub/Sub drops messages when no subscriber is connected at publish
        /// time and provides no built-in retry or dead-letter support. Configure <see cref="RedisPubSubMessagingOptions.OnDispatchFailed"/>
        /// to handle or record failed dispatches.
        /// </remarks>
        public MessagingSetupBuilder UseRedisPubSub(IConfiguration config)
        {
            Argument.IsNotNull(config);

            return _RegisterRedisPubSub(
                setup,
                services =>
                    services.Configure<RedisPubSubMessagingOptions, RedisPubSubMessagingOptionsValidator>(config)
            );
        }

        /// <summary>
        /// Use Redis Pub/Sub as the bus (fan-out) transport.
        /// </summary>
        /// <param name="configure">Configures <see cref="RedisPubSubMessagingOptions"/>.</param>
        /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
        /// <remarks>
        /// <b>At-most-once delivery:</b> Redis Pub/Sub drops messages when no subscriber is connected at publish
        /// time and provides no built-in retry or dead-letter support. Configure <see cref="RedisPubSubMessagingOptions.OnDispatchFailed"/>
        /// to handle or record failed dispatches.
        /// </remarks>
        public MessagingSetupBuilder UseRedisPubSub(Action<RedisPubSubMessagingOptions> configure)
        {
            Argument.IsNotNull(configure);

            return _RegisterRedisPubSub(
                setup,
                services =>
                    services.Configure<RedisPubSubMessagingOptions, RedisPubSubMessagingOptionsValidator>(configure)
            );
        }

        /// <summary>
        /// Use Redis Pub/Sub as the bus (fan-out) transport, configuring
        /// <see cref="RedisPubSubMessagingOptions"/> with access to the resolved service provider.
        /// </summary>
        /// <param name="configure">
        /// A delegate that configures <see cref="RedisPubSubMessagingOptions"/> using the service provider
        /// (for example to resolve secrets or connection settings from DI).
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
        /// <remarks>
        /// <b>At-most-once delivery:</b> Redis Pub/Sub drops messages when no subscriber is connected at publish
        /// time and provides no built-in retry or dead-letter support. Configure <see cref="RedisPubSubMessagingOptions.OnDispatchFailed"/>
        /// to handle or record failed dispatches.
        /// </remarks>
        public MessagingSetupBuilder UseRedisPubSub(Action<RedisPubSubMessagingOptions, IServiceProvider> configure)
        {
            Argument.IsNotNull(configure);

            return _RegisterRedisPubSub(
                setup,
                services =>
                    services.Configure<RedisPubSubMessagingOptions, RedisPubSubMessagingOptionsValidator>(configure)
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

    private static MessagingSetupBuilder _RegisterRedisPubSub(
        MessagingSetupBuilder setup,
        Action<IServiceCollection> configureOptions
    )
    {
        setup.RegisterExtension(new RedisPubSubMessagingOptionsExtension(configureOptions));

        return setup;
    }

    private sealed class RedisMessagingOptionsExtension(Action<IServiceCollection> configureOptions)
        : IMessagesOptionsExtension
    {
        private readonly Action<IServiceCollection> _configureOptions = Argument.IsNotNull(configureOptions);

        public void AddServices(IServiceCollection services)
        {
            services.TryAddSingleton(new MessageQueueMarkerService("Redis"));
            services.AddSingleton<IRedisStreamManager, RedisStreamManager>();
            services.AddSingleton<RedisConsumerClientFactory>();
            services.TryAddSingleton<RedisConsumerClientFactorySelector>();
            services.TryAddSingleton<IConsumerClientFactory>(sp =>
                sp.GetRequiredService<RedisConsumerClientFactorySelector>()
            );
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

    private sealed class RedisPubSubMessagingOptionsExtension(Action<IServiceCollection> configureOptions)
        : IMessagesOptionsExtension
    {
        private readonly Action<IServiceCollection> _configureOptions = Argument.IsNotNull(configureOptions);

        public void AddServices(IServiceCollection services)
        {
            services.TryAddSingleton(new MessageQueueMarkerService("Redis"));
            services.AddSingleton<IRedisPubSubConnectionProvider, RedisPubSubConnectionProvider>();
            services.AddSingleton<RedisPubSubBusTransport>();
            services.AddSingleton<IBusTransport>(sp => sp.GetRequiredService<RedisPubSubBusTransport>());
            services.AddSingleton<RedisPubSubConsumerClientFactory>();
            services.TryAddSingleton<RedisConsumerClientFactorySelector>();
            services.TryAddSingleton<IConsumerClientFactory>(sp =>
                sp.GetRequiredService<RedisConsumerClientFactorySelector>()
            );
            services.TryAddEnumerable(
                ServiceDescriptor.Singleton<
                    IPostConfigureOptions<RedisPubSubMessagingOptions>,
                    RedisPubSubMessagingOptionsPostConfigure
                >()
            );
            _configureOptions(services);
        }
    }

    private sealed class RedisPubSubMessagingOptionsPostConfigure : IPostConfigureOptions<RedisPubSubMessagingOptions>
    {
        public void PostConfigure(string? name, RedisPubSubMessagingOptions options)
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
