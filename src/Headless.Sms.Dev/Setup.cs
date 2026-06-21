// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;

namespace Headless.Sms.Dev;

[PublicAPI]
public static class SetupDevSms
{
    public static IServiceCollection AddDevSmsSender(this IServiceCollection services, string filePath)
    {
        services.AddSingleton<ISmsSender>(_ => new DevSmsSender(filePath));

        return services;
    }

    public static IServiceCollection AddNoopSmsSender(this IServiceCollection services)
    {
        services.AddSingleton<ISmsSender, NoopSmsSender>();

        return services;
    }
}
