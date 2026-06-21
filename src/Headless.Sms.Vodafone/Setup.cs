// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace Headless.Sms.Vodafone;

[PublicAPI]
public static class SetupVodafone
{
    internal const string HttpClientName = "Headless:VodafoneSms";

    public static IServiceCollection AddVodafoneSmsSender(
        this IServiceCollection services,
        IConfiguration config,
        Action<HttpClient>? configureClient = null,
        Action<HttpStandardResilienceOptions>? configureResilience = null
    )
    {
        services.Configure<VodafoneSmsOptions, VodafoneSmsOptionsValidator>(config);

        return _AddVodafoneSmsSenderCore(services, configureClient, configureResilience);
    }

    public static IServiceCollection AddVodafoneSmsSender(
        this IServiceCollection services,
        Action<VodafoneSmsOptions, IServiceProvider> setupAction,
        Action<HttpClient>? configureClient = null,
        Action<HttpStandardResilienceOptions>? configureResilience = null
    )
    {
        services.Configure<VodafoneSmsOptions, VodafoneSmsOptionsValidator>(setupAction);

        return _AddVodafoneSmsSenderCore(services, configureClient, configureResilience);
    }

    public static IServiceCollection AddVodafoneSmsSender(
        this IServiceCollection services,
        Action<VodafoneSmsOptions> setupAction,
        Action<HttpClient>? configureClient = null,
        Action<HttpStandardResilienceOptions>? configureResilience = null
    )
    {
        services.Configure<VodafoneSmsOptions, VodafoneSmsOptionsValidator>(setupAction);

        return _AddVodafoneSmsSenderCore(services, configureClient, configureResilience);
    }

    private static IServiceCollection _AddVodafoneSmsSenderCore(
        IServiceCollection services,
        Action<HttpClient>? configureClient,
        Action<HttpStandardResilienceOptions>? configureResilience
    )
    {
        services.AddSingleton<ISmsSender, VodafoneSmsSender>();

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
