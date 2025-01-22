// Copyright (c) Mahmoud Shaheen. All rights reserved.

global using IFoundatioMessageBus = Foundatio.Messaging.IMessageBus;
using Foundatio;
using Foundatio.Messaging;
using Foundatio.Serializer;
using Framework.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Framework.Messaging;

[PublicAPI]
public static class AddFoundatioExtensions
{
    public static IServiceCollection AddMessageBusFoundatioAdapter(this IServiceCollection services)
    {
        DefaultSerializer.Instance = FoundationHelper.JsonSerializer;
        services.AddSingleton<IMessagePublisher>(provider => provider.GetRequiredService<IMessageBus>());
        services.AddSingleton<IMessageSubscriber>(provider => provider.GetRequiredService<IMessageBus>());
        services.AddSingleton<IMessageBus, MessageBusFoundatioAdapter>();

        return services;
    }

    public static IServiceCollection AddFoundatioInMemoryMessageBus(
        this IServiceCollection services,
        Builder<InMemoryMessageBusOptionsBuilder, InMemoryMessageBusOptions>? setupAction = null
    )
    {
        services.AddSingleton<IFoundatioMessageBus>(provider => new InMemoryMessageBus(builder =>
        {
            var result = builder
                .TimeProvider(provider.GetRequiredService<TimeProvider>())
                .LoggerFactory(provider.GetRequiredService<ILoggerFactory>())
                .Serializer(FoundationHelper.JsonSerializer);

            return setupAction is null ? result : setupAction(result);
        }));

        return services.AddMessageBusFoundatioAdapter();
    }
}
