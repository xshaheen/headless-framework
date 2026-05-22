// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Checks;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.RedisPubSub;
using Headless.Messaging.Transport;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

public static class SetupRedisPubSubMessaging
{
    extension(MessagingSetupBuilder setup)
    {
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

    private sealed class RedisPubSubOptionsExtension(Action<RedisPubSubOptions> configure)
        : IMessagesOptionsExtension
    {
        private readonly Action<RedisPubSubOptions> _configure = Argument.IsNotNull(configure);

        public void AddServices(IServiceCollection services)
        {
            services.AddSingleton(new MessageQueueMarkerService("RedisPubSub"));
            services.AddSingleton<IRedisPubSubConnectionProvider, RedisPubSubConnectionProvider>();
            services.AddSingleton<RedisPubSubBusTransport>();
            services.AddSingleton<IBusTransport>(sp => sp.GetRequiredService<RedisPubSubBusTransport>());
            services.AddSingleton<IConsumerClientFactory, RedisPubSubConsumerClientFactory>();
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
