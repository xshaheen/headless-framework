// Copyright (c) Mahmoud Shaheen. All rights reserved.

global using IFoundatioMessageBus = Foundatio.Messaging.IMessageBus;
global using IFrameworkMessageBus = Framework.Messaging.IMessageBus;
using Microsoft.Extensions.DependencyInjection;

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
}
