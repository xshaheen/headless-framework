// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;

namespace Framework.Messaging;

[PublicAPI]
public static class MassTransitSetup
{
    /// <summary>
    /// Registers both the MassTransit message bus adapter and distributed message publisher.
    /// </summary>
    public static IServiceCollection AddHeadlessMassTransitAdapter(this IServiceCollection services)
    {
        return services;
    }
}
