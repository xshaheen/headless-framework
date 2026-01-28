// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;

namespace Headless.Sms.Cequens;

[PublicAPI]
public static class CequensSetup
{
    internal const string HttpClientName = "Headless:CequensSms";

    public static IServiceCollection AddCequensSmsSender(
        this IServiceCollection services,
        IConfiguration config,
        Action<HttpClient>? configureClient = null,
        Action<HttpStandardResilienceOptions>? configureResilience = null
    )
    {
        services.Configure<CequensSmsOptions, CequensSmsOptionsValidator>(config);

        return _AddCore(services, configureClient, configureResilience);
    }

    public static IServiceCollection AddCequensSmsSender(
        this IServiceCollection services,
        Action<CequensSmsOptions> setupAction,
        Action<HttpClient>? configureClient = null,
        Action<HttpStandardResilienceOptions>? configureResilience = null
    )
    {
        services.Configure<CequensSmsOptions, CequensSmsOptionsValidator>(setupAction);

        return _AddCore(services, configureClient, configureResilience);
    }

    public static IServiceCollection AddCequensSmsSender(
        this IServiceCollection services,
        Action<CequensSmsOptions, IServiceProvider> setupAction,
        Action<HttpClient>? configureClient = null,
        Action<HttpStandardResilienceOptions>? configureResilience = null
    )
    {
        services.Configure<CequensSmsOptions, CequensSmsOptionsValidator>(setupAction);

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
            ? services.AddHttpClient(HttpClientName)
            : services.AddHttpClient(HttpClientName, configureClient);

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
