// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Checks;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Redis;
using Headless.Messaging.Transport;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

public static class SetupRedisMessaging
{
    extension(MessagingSetupBuilder setup)
    {
        public MessagingSetupBuilder UseRedis()
        {
            return setup.UseRedis(_ => { });
        }

        /// <summary>Use redis streams as the message transport.</summary>
        /// <param name="connection">The StackExchange.Redis <see cref="ConfigurationOptions" /> comma-delimited configuration string.</param>
        public MessagingSetupBuilder UseRedis(string connection)
        {
            return setup.UseRedis(opt => opt.Configuration = ConfigurationOptions.Parse(connection));
        }

        /// <summary>Use redis streams as the message transport.</summary>
        /// <param name="configure">The redis client options.</param>
        /// <exception cref="ArgumentNullException"><paramref name="configure" /> is <see langword="null"/>.</exception>
        public MessagingSetupBuilder UseRedis(Action<MessagingRedisOptions> configure)
        {
            Argument.IsNotNull(configure);

            setup.RegisterExtension(new RedisOptionsExtension(configure));

            return setup;
        }

        public MessagingSetupBuilder UseRedisPubSub()
        {
            return setup.UseRedisPubSub(_ => { });
        }

        public MessagingSetupBuilder UseRedisPubSub(string connection)
        {
            return setup.UseRedisPubSub(opt => opt.Configuration = ConfigurationOptions.Parse(connection));
        }

        public MessagingSetupBuilder UseRedisPubSub(Action<RedisPubSubOptions> configure)
        {
            Argument.IsNotNull(configure);

            setup.RegisterExtension(new RedisPubSubOptionsExtension(configure));

            return setup;
        }
    }

    private sealed class RedisOptionsExtension(Action<MessagingRedisOptions> configure) : IMessagesOptionsExtension
    {
        private readonly Action<MessagingRedisOptions> _configure = Argument.IsNotNull(configure);

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
                    IPostConfigureOptions<MessagingRedisOptions>,
                    MessagingRedisOptionsPostConfigure
                >()
            );
            services.Configure<MessagingRedisOptions, MessagingRedisOptionsValidator>(_configure);
        }
    }

    private sealed class MessagingRedisOptionsPostConfigure : IPostConfigureOptions<MessagingRedisOptions>
    {
        public void PostConfigure(string? name, MessagingRedisOptions options)
        {
            options.Configuration ??= new ConfigurationOptions();

            if (options.StreamEntriesCount == 0)
            {
                options.StreamEntriesCount = 10;
            }

            if (options.ConnectionPoolSize == 0)
            {
                options.ConnectionPoolSize = 10;
            }

            if (!options.Configuration.EndPoints.Any())
            {
                options.Configuration.EndPoints.Add(IPAddress.Loopback, 0);
                options.Configuration.SetDefaultPorts();
            }
        }
    }

    private sealed class RedisPubSubOptionsExtension(Action<RedisPubSubOptions> configure)
        : IMessagesOptionsExtension
    {
        private readonly Action<RedisPubSubOptions> _configure = Argument.IsNotNull(configure);

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
                    IPostConfigureOptions<RedisPubSubOptions>,
                    RedisPubSubOptionsPostConfigure
                >()
            );
            services.Configure<RedisPubSubOptions, RedisPubSubOptionsValidator>(_configure);
        }
    }

    private sealed class RedisPubSubOptionsPostConfigure : IPostConfigureOptions<RedisPubSubOptions>
    {
        public void PostConfigure(string? name, RedisPubSubOptions options)
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
