// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace Headless.Sms.Infobip;

[PublicAPI]
public static class SetupInfobip
{
    internal const string HttpClientName = "Headless:InfobipSms";

    public static IServiceCollection AddInfobipSmsSender(
        this IServiceCollection services,
        IConfiguration config,
        Action<HttpClient>? configureClient = null,
        Action<HttpStandardResilienceOptions>? configureResilience = null
    )
    {
        services.Configure<InfobipSmsOptions, InfobipSmsOptionsValidator>(config);

        return _AddInfobipSmsSenderCore(services, configureClient, configureResilience);
    }

    public static IServiceCollection AddInfobipSmsSender(
        this IServiceCollection services,
        Action<InfobipSmsOptions> setupAction,
        Action<HttpClient>? configureClient = null,
        Action<HttpStandardResilienceOptions>? configureResilience = null
    )
    {
        services.Configure<InfobipSmsOptions, InfobipSmsOptionsValidator>(setupAction);

        return _AddInfobipSmsSenderCore(services, configureClient, configureResilience);
    }

    public static IServiceCollection AddInfobipSmsSender(
        this IServiceCollection services,
        Action<InfobipSmsOptions, IServiceProvider> setupAction,
        Action<HttpClient>? configureClient = null,
        Action<HttpStandardResilienceOptions>? configureResilience = null
    )
    {
        services.Configure<InfobipSmsOptions, InfobipSmsOptionsValidator>(setupAction);

        return _AddInfobipSmsSenderCore(services, configureClient, configureResilience);
    }

    private static IServiceCollection _AddInfobipSmsSenderCore(
        IServiceCollection services,
        Action<HttpClient>? configureClient,
        Action<HttpStandardResilienceOptions>? configureResilience
    )
    {
        services.AddSingleton<ISmsSender, InfobipSmsSender>();

        var httpClientBuilder = configureClient is null
            ? services.AddHttpClient(HttpClientName)
            : services.AddHttpClient(HttpClientName, configureClient);

        // SMS sends are not idempotent: don't auto-retry by default to avoid duplicate messages.
        // Consumers can opt back in via configureResilience (ideally with a provider idempotency key).
        httpClientBuilder.AddStandardResilienceHandler(options =>
        {
            options.Retry.ShouldHandle = static _ => PredicateResult.False();
            configureResilience?.Invoke(options);
        });

        return services;
    }
}
