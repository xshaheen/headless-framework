// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;

namespace Headless.Domain;

[PublicAPI]
public static class LocalPublisherSetup
{
    public static IServiceCollection AddServiceProviderLocalMessagePublisher(this IServiceCollection services)
    {
        services.AddSingleton<ILocalMessagePublisher, ServiceProviderLocalMessagePublisher>();

        return services;
    }
}
