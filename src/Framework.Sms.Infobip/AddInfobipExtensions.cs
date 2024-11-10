// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Framework.Sms.Infobip;

[PublicAPI]
public static class AddInfobipExtensions
{
    public static IServiceCollection AddInfobipSmsSender(this IServiceCollection services, IConfiguration config)
    {
        services.ConfigureSingleton<InfobipSettings, InfobipSettingsValidator>(config);

        return _AddCore(services);
    }

    public static IServiceCollection AddInfobipSmsSender(
        this IServiceCollection services,
        Action<InfobipSettings> setupAction
    )
    {
        services.ConfigureSingleton<InfobipSettings, InfobipSettingsValidator>(setupAction);

        return _AddCore(services);
    }

    public static IServiceCollection AddInfobipSmsSender(
        this IServiceCollection services,
        Action<InfobipSettings, IServiceProvider> setupAction
    )
    {
        services.ConfigureSingleton<InfobipSettings, InfobipSettingsValidator>(setupAction);

        return _AddCore(services);
    }

    private static IServiceCollection _AddCore(IServiceCollection services)
    {
        services
            .AddSingleton<ISmsSender, InfobipSmsSender>()
            .AddHttpClient<ISmsSender, InfobipSmsSender>(name: "infobip-client")
            .AddStandardResilienceHandler();

        return services;
    }
}
