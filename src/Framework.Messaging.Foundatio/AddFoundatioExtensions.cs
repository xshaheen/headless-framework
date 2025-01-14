// Copyright (c) Mahmoud Shaheen. All rights reserved.

global using IFoundatioMessageBus = Foundatio.Messaging.IMessageBus;
global using IFrameworkMessageBus = Framework.Messaging.IMessageBus;
using Foundatio;
using Foundatio.Messaging;
using Foundatio.Serializer;
using Framework.Abstractions;
using Framework.Serializer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Framework.Messaging;

[PublicAPI]
public static class AddFoundatioExtensions
{
    public static IServiceCollection AddMessageBusFoundatioAdapter(this IServiceCollection services)
    {
        services.AddSingleton<IFrameworkMessageBus, MessageBusFoundatioAdapter>();
        services.AddSingleton<IMessagePublisher>(provider => provider.GetRequiredService<IFrameworkMessageBus>());
        services.AddSingleton<IMessageSubscriber>(provider => provider.GetRequiredService<IFrameworkMessageBus>());

        return services;
    }

    public static IServiceCollection AddFoundatioInMemoryMessageBus(
        this IServiceCollection services,
        Builder<InMemoryMessageBusOptionsBuilder, InMemoryMessageBusOptions>? setupAction = null
    )
    {
        services.AddSingleton<IFrameworkMessageBus>(
            provider =>
            {
                var guidGenerator = provider.GetRequiredService<IGuidGenerator>();
                var loggerFactory = provider.GetRequiredService<ILoggerFactory>();

                var inMemoryMessageBus = new InMemoryMessageBus(
                    builder =>
                    {
                        var serializer = new SystemTextJsonSerializer(
                            serializeOptions: JsonConstants.DefaultInternalJsonOptions,
                            deserializeOptions: JsonConstants.DefaultInternalJsonOptions
                        );

                        var result = builder
                            .LoggerFactory(loggerFactory)
                            .Serializer(serializer);

                        return setupAction is null ? result : setupAction(result);
                    }
                );

                return new MessageBusFoundatioAdapter(inMemoryMessageBus, guidGenerator);
            }
        );

        services.AddSingleton<IMessagePublisher>(provider => provider.GetRequiredService<IFrameworkMessageBus>());
        services.AddSingleton<IMessageSubscriber>(provider => provider.GetRequiredService<IFrameworkMessageBus>());

        return services;
    }
}
