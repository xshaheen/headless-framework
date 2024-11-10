// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Framework.Sms.Cequens;

[PublicAPI]
public static class AddCequensExtensions
{
    public static void AddCequensSmsSender(this IServiceCollection services, IConfiguration config)
    {
        services.ConfigureSingleton<CequensSettings, CequensSettingsValidator>(config);
        _AddCore(services);
    }

    public static void AddCequensSmsSender(this IServiceCollection services, Action<CequensSettings> setupAction)
    {
        services.ConfigureSingleton<CequensSettings, CequensSettingsValidator>(setupAction);
        _AddCore(services);
    }

    public static void AddCequensSmsSender(
        this IServiceCollection services,
        Action<CequensSettings, IServiceProvider> setupAction
    )
    {
        services.ConfigureSingleton<CequensSettings, CequensSettingsValidator>(setupAction);
        _AddCore(services);
    }

    private static void _AddCore(IServiceCollection services)
    {
        services
            .AddSingleton<ISmsSender, CequensSmsSender>()
            .AddHttpClient<ISmsSender, CequensSmsSender>(name: "cequens-client")
            .AddStandardResilienceHandler();
    }
}
