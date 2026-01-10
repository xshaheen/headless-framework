// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;

namespace Framework.Messaging;

[PublicAPI]
public static class MassTransitSetup
{
    /// <summary>
    /// Registers Framework messaging adapter for MassTransit.
    /// Call AFTER AddMassTransit().
    /// </summary>
    public static IServiceCollection AddHeadlessMassTransitAdapter(this IServiceCollection services)
    {
        services.AddScoped<IMessagePublisher>(sp => sp.GetRequiredService<IMessageBus>());
        services.AddScoped<IMessageSubscriber>(sp => sp.GetRequiredService<IMessageBus>());
        services.AddScoped<IMessageBus, MassTransitMessageBusAdapter>();
        return services;
    }
}
