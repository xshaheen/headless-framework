// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;

namespace Headless.Sms.Connekio;

[PublicAPI]
public static class ConnekioSetup
{
    internal const string HttpClientName = "Headless:ConnekioSms";

    public static IServiceCollection AddConnekioSmsSender(
        this IServiceCollection services,
        IConfiguration config,
        Action<HttpClient>? configureClient = null,
        Action<HttpStandardResilienceOptions>? configureResilience = null
    )
    {
        services.Configure<ConnekioSmsOptions, ConnekioSmsOptionsValidator>(config);

        return _AddCore(services, configureClient, configureResilience);
    }

    public static IServiceCollection AddConnekioSmsSender(
        this IServiceCollection services,
        Action<ConnekioSmsOptions, IServiceProvider> setupAction,
        Action<HttpClient>? configureClient = null,
        Action<HttpStandardResilienceOptions>? configureResilience = null
    )
    {
        services.Configure<ConnekioSmsOptions, ConnekioSmsOptionsValidator>(setupAction);

        return _AddCore(services, configureClient, configureResilience);
    }

    public static IServiceCollection AddConnekioSmsSender(
        this IServiceCollection services,
        Action<ConnekioSmsOptions> setupAction,
        Action<HttpClient>? configureClient = null,
        Action<HttpStandardResilienceOptions>? configureResilience = null
    )
    {
        services.Configure<ConnekioSmsOptions, ConnekioSmsOptionsValidator>(setupAction);

        return _AddCore(services, configureClient, configureResilience);
    }

    private static IServiceCollection _AddCore(
        IServiceCollection services,
        Action<HttpClient>? configureClient,
        Action<HttpStandardResilienceOptions>? configureResilience
    )
    {
        services.AddSingleton<ISmsSender, ConnekioSmsSender>();

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
