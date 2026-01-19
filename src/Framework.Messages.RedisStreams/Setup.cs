// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Framework.Checks;
using Framework.Messages;
using Framework.Messages.Configuration;
using Framework.Messages.RedisStreams;
using Framework.Messages.Transport;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

public static class MessagesRedisSetup
{
    extension(MessagingOptions options)
    {
        public MessagingOptions UseRedis()
        {
            return options.UseRedis(_ => { });
        }

        /// <summary>Use redis streams as the message transport.</summary>
        /// <param name="connection">The StackExchange.Redis <see cref="ConfigurationOptions" /> comma-delimited configuration string.</param>
        public MessagingOptions UseRedis(string connection)
        {
            return options.UseRedis(opt => opt.Configuration = ConfigurationOptions.Parse(connection));
        }

        /// <summary>Use redis streams as the message transport.</summary>
        /// <param name="configure">The CAP redis client options.</param>
        /// <exception cref="ArgumentNullException"><paramref name="configure" /> is <see langword="null"/>.</exception>
        public MessagingOptions UseRedis(Action<CapRedisOptions> configure)
        {
            Argument.IsNotNull(configure);

            options.RegisterExtension(new RedisOptionsExtension(configure));

            return options;
        }
    }

    private sealed class RedisOptionsExtension(Action<CapRedisOptions> configure) : IMessagesOptionsExtension
    {
        private readonly Action<CapRedisOptions> _configure =
            configure ?? throw new ArgumentNullException(nameof(configure));

        public void AddServices(IServiceCollection services)
        {
            services.AddSingleton(new MessageQueueMarkerService("RedisStreams"));
            services.AddSingleton<IRedisStreamManager, RedisStreamManager>();
            services.AddSingleton<IConsumerClientFactory, RedisConsumerClientFactory>();
            services.AddSingleton<ITransport, RedisTransport>();
            services.AddSingleton<IRedisConnectionPool, RedisConnectionPool>();
            services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IPostConfigureOptions<CapRedisOptions>, CapRedisOptionsPostConfigure>()
            );
            services.AddOptions<CapRedisOptions>().Configure(_configure);
        }
    }

    private sealed class CapRedisOptionsPostConfigure : IPostConfigureOptions<CapRedisOptions>
    {
        public void PostConfigure(string? name, CapRedisOptions options)
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
}
