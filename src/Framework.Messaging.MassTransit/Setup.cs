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
    /// <remarks>
    /// Services are registered as Scoped to align with MassTransit's IPublishEndpoint lifetime.
    /// MassTransit registers IPublishEndpoint as scoped to maintain proper ASP.NET request scopes
    /// and transaction boundaries. While IReceiveEndpointConnector is singleton, it's only used
    /// during subscription setup, not for ongoing message operations.
    ///
    /// If consuming from singleton services (e.g., IHostedService), create a scope:
    /// <code>
    /// using var scope = serviceProvider.CreateScope();
    /// var messageBus = scope.ServiceProvider.GetRequiredService&lt;IMessageBus&gt;();
    /// </code>
    /// </remarks>
    public static IServiceCollection AddHeadlessMassTransitAdapter(this IServiceCollection services)
    {
        services.AddScoped<IMessagePublisher>(sp => sp.GetRequiredService<IMessageBus>());
        services.AddScoped<IMessageSubscriber>(sp => sp.GetRequiredService<IMessageBus>());
        services.AddScoped<IMessageBus, MassTransitMessageBusAdapter>();
        return services;
    }
}
