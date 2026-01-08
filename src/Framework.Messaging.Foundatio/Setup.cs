// Copyright (c) Mahmoud Shaheen. All rights reserved.

global using IFoundatioMessageBus = Foundatio.Messaging.IMessageBus;
global using IFoundatioSerializer = Foundatio.Serializer.ITextSerializer;
using Foundatio;
using Foundatio.Messaging;
using Foundatio.Serializer;
using Framework.Serializer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Framework.Messaging;

[PublicAPI]
public static class FoundatioSetup
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddMessageBusFoundatioAdapter()
        {
            DefaultSerializer.Instance = new FoundatioSerializerAdapter(new SystemJsonSerializer());
            services.AddSingleton<IMessagePublisher>(provider => provider.GetRequiredService<IMessageBus>());
            services.AddSingleton<IMessageSubscriber>(provider => provider.GetRequiredService<IMessageBus>());
            services.AddSingleton<IMessageBus, FoundatioMessageBusAdapter>();

            return services;
        }

        public IServiceCollection AddFoundatioInMemoryMessageBus(
            Builder<InMemoryMessageBusOptionsBuilder, InMemoryMessageBusOptions>? setupAction = null
        )
        {
            services.TryAddSingleton<IJsonOptionsProvider>(new DefaultJsonOptionsProvider());

            services.TryAddSingleton<IJsonSerializer>(sp => new SystemJsonSerializer(
                sp.GetRequiredService<IJsonOptionsProvider>()
            ));

            services.AddSingleton<IFoundatioMessageBus>(provider => new InMemoryMessageBus(builder =>
            {
                var result = builder
                    .TimeProvider(provider.GetRequiredService<TimeProvider>())
                    .LoggerFactory(provider.GetRequiredService<ILoggerFactory>())
                    .Serializer(new FoundatioSerializerAdapter(provider.GetRequiredService<IJsonSerializer>()));

                return setupAction is null ? result : setupAction(result);
            }));

            return services.AddMessageBusFoundatioAdapter();
        }
    }
}
