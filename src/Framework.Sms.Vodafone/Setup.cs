// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;

namespace Framework.Sms.Vodafone;

[PublicAPI]
public static class Setup
{
    public static IServiceCollection AddVodafoneSmsSender(
        this IServiceCollection services,
        IConfiguration config,
        Action<HttpClient>? configureClient = null,
        Action<HttpStandardResilienceOptions>? configureResilience = null
    )
    {
        services.Configure<VodafoneSmsOptions, VodafoneSmsOptionsValidator>(config);

        return _AddCore(services, configureClient, configureResilience);
    }

    public static IServiceCollection AddVodafoneSmsSender(
        this IServiceCollection services,
        Action<VodafoneSmsOptions, IServiceProvider> setupAction,
        Action<HttpClient>? configureClient = null,
        Action<HttpStandardResilienceOptions>? configureResilience = null
    )
    {
        services.Configure<VodafoneSmsOptions, VodafoneSmsOptionsValidator>(setupAction);

        return _AddCore(services, configureClient, configureResilience);
    }

    public static IServiceCollection AddVodafoneSmsSender(
        this IServiceCollection services,
        Action<VodafoneSmsOptions> setupAction,
        Action<HttpClient>? configureClient = null,
        Action<HttpStandardResilienceOptions>? configureResilience = null
    )
    {
        services.Configure<VodafoneSmsOptions, VodafoneSmsOptionsValidator>(setupAction);

        return _AddCore(services, configureClient, configureResilience);
    }

    private static IServiceCollection _AddCore(
        IServiceCollection services,
        Action<HttpClient>? configureClient,
        Action<HttpStandardResilienceOptions>? configureResilience
    )
    {
        services.AddSingleton<ISmsSender, VodafoneSmsSender>();

        var httpClientBuilder = configureClient is null
            ? services.AddHttpClient("VodafoneSms")
            : services.AddHttpClient("VodafoneSms", configureClient);

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
