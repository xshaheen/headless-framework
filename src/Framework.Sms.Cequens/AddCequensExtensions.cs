// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;

namespace Framework.Sms.Cequens;

[PublicAPI]
public static class AddCequensExtensions
{
    public static IServiceCollection AddCequensSmsSender(
        this IServiceCollection services,
        IConfiguration config,
        Action<HttpClient>? configureClient = null,
        Action<HttpStandardResilienceOptions>? configureResilience = null
    )
    {
        services.ConfigureSingleton<CequensSettings, CequensSettingsValidator>(config);

        return _AddCore(services, configureClient, configureResilience);
    }

    public static IServiceCollection AddCequensSmsSender(
        this IServiceCollection services,
        Action<CequensSettings> setupAction,
        Action<HttpClient>? configureClient = null,
        Action<HttpStandardResilienceOptions>? configureResilience = null
    )
    {
        services.ConfigureSingleton<CequensSettings, CequensSettingsValidator>(setupAction);

        return _AddCore(services, configureClient, configureResilience);
    }

    public static IServiceCollection AddCequensSmsSender(
        this IServiceCollection services,
        Action<CequensSettings, IServiceProvider> setupAction,
        Action<HttpClient>? configureClient = null,
        Action<HttpStandardResilienceOptions>? configureResilience = null
    )
    {
        services.ConfigureSingleton<CequensSettings, CequensSettingsValidator>(setupAction);

        return _AddCore(services, configureClient, configureResilience);
    }

    private static IServiceCollection _AddCore(
        IServiceCollection services,
        Action<HttpClient>? configureClient,
        Action<HttpStandardResilienceOptions>? configureResilience
    )
    {
        services.AddSingleton<ISmsSender, CequensSmsSender>();

        var httpClientBuilder = configureClient is null
            ? services.AddHttpClient<ISmsSender, CequensSmsSender>(name: "cequens-client")
            : services.AddHttpClient<ISmsSender, CequensSmsSender>(name: "cequens-client", configureClient);

        if (configureResilience is not null)
        {
            httpClientBuilder.AddStandardResilienceHandler(configureResilience);
        }
        else
        {
            httpClientBuilder.AddStandardResilienceHandler();
        }

        return services;
    }
}
