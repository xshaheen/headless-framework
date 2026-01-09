// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;

namespace Framework.Sms.VictoryLink;

[PublicAPI]
public static class VictoryLinkSetup
{
    public static IServiceCollection AddVictoryLinkSmsSender(
        this IServiceCollection services,
        IConfiguration config,
        Action<HttpClient>? configureClient = null,
        Action<HttpStandardResilienceOptions>? configureResilience = null
    )
    {
        services.Configure<VictoryLinkSmsOptions, VictoryLinkSmsOptionsValidator>(config);

        return _AddCore(services, configureClient, configureResilience);
    }

    public static IServiceCollection AddVictoryLinkSmsSender(
        this IServiceCollection services,
        Action<VictoryLinkSmsOptions, IServiceProvider> setupAction,
        Action<HttpClient>? configureClient = null,
        Action<HttpStandardResilienceOptions>? configureResilience = null
    )
    {
        services.Configure<VictoryLinkSmsOptions, VictoryLinkSmsOptionsValidator>(setupAction);

        return _AddCore(services, configureClient, configureResilience);
    }

    public static IServiceCollection AddVictoryLinkSmsSender(
        this IServiceCollection services,
        Action<VictoryLinkSmsOptions> setupAction,
        Action<HttpClient>? configureClient = null,
        Action<HttpStandardResilienceOptions>? configureResilience = null
    )
    {
        services.Configure<VictoryLinkSmsOptions, VictoryLinkSmsOptionsValidator>(setupAction);

        return _AddCore(services, configureClient, configureResilience);
    }

    private static IServiceCollection _AddCore(
        IServiceCollection services,
        Action<HttpClient>? configureClient,
        Action<HttpStandardResilienceOptions>? configureResilience
    )
    {
        services.AddSingleton<ISmsSender, VictoryLinkSmsSender>();

        var httpClientBuilder = configureClient is null
            ? services.AddHttpClient<ISmsSender, VictoryLinkSmsSender>()
            : services.AddHttpClient<ISmsSender, VictoryLinkSmsSender>(configureClient);

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
