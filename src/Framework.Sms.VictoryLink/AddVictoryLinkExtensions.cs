// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Framework.Sms.VictoryLink;

[PublicAPI]
public static class AddVictoryLinkExtensions
{
    public static IServiceCollection AddVictoryLinkSmsSender(this IServiceCollection services, IConfiguration config)
    {
        services.ConfigureSingleton<VictoryLinkSettings, VictoryLinkSettingsValidator>(config);

        return _AddCore(services);
    }

    public static IServiceCollection AddVictoryLinkSmsSender(
        this IServiceCollection services,
        Action<VictoryLinkSettings, IServiceProvider> setupAction
    )
    {
        services.ConfigureSingleton<VictoryLinkSettings, VictoryLinkSettingsValidator>(setupAction);

        return _AddCore(services);
    }

    public static IServiceCollection AddVictoryLinkSmsSender(
        this IServiceCollection services,
        Action<VictoryLinkSettings> setupAction
    )
    {
        services.ConfigureSingleton<VictoryLinkSettings, VictoryLinkSettingsValidator>(setupAction);

        return _AddCore(services);
    }

    private static IServiceCollection _AddCore(IServiceCollection services)
    {
        services
            .AddSingleton<ISmsSender, VictoryLinkSmsSender>()
            .AddHttpClient<ISmsSender, VictoryLinkSmsSender>(name: "victory-link-client")
            .AddStandardResilienceHandler();

        return services;
    }
}
