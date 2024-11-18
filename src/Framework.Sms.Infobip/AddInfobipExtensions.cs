// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;

namespace Framework.Sms.Infobip;

[PublicAPI]
public static class AddInfobipExtensions
{
    public static IServiceCollection AddInfobipSmsSender(
        this IServiceCollection services,
        IConfiguration config,
        Action<HttpClient>? configureClient = null,
        Action<HttpStandardResilienceOptions>? configureResilience = null
    )
    {
        services.ConfigureSingleton<InfobipOptions, InfobipOptionsValidator>(config);

        return _AddCore(services, configureClient, configureResilience);
    }

    public static IServiceCollection AddInfobipSmsSender(
        this IServiceCollection services,
        Action<InfobipOptions> setupAction,
        Action<HttpClient>? configureClient = null,
        Action<HttpStandardResilienceOptions>? configureResilience = null
    )
    {
        services.ConfigureSingleton<InfobipOptions, InfobipOptionsValidator>(setupAction);

        return _AddCore(services, configureClient, configureResilience);
    }

    public static IServiceCollection AddInfobipSmsSender(
        this IServiceCollection services,
        Action<InfobipOptions, IServiceProvider> setupAction,
        Action<HttpClient>? configureClient = null,
        Action<HttpStandardResilienceOptions>? configureResilience = null
    )
    {
        services.ConfigureSingleton<InfobipOptions, InfobipOptionsValidator>(setupAction);

        return _AddCore(services, configureClient, configureResilience);
    }

    private static IServiceCollection _AddCore(
        IServiceCollection services,
        Action<HttpClient>? configureClient,
        Action<HttpStandardResilienceOptions>? configureResilience
    )
    {
        services.AddSingleton<ISmsSender, InfobipSmsSender>();

        var httpClientBuilder = configureClient is null
            ? services.AddHttpClient<ISmsSender, InfobipSmsSender>(name: "infobip-client")
            : services.AddHttpClient<ISmsSender, InfobipSmsSender>(name: "infobip-client", configureClient);

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
