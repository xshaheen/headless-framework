// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace Headless.Sms.VictoryLink;

[PublicAPI]
public static class SetupVictoryLink
{
    internal const string HttpClientName = "Headless:VictoryLinkSms";

    public static IServiceCollection AddVictoryLinkSmsSender(
        this IServiceCollection services,
        IConfiguration config,
        Action<HttpClient>? configureClient = null,
        Action<HttpStandardResilienceOptions>? configureResilience = null
    )
    {
        services.Configure<VictoryLinkSmsOptions, VictoryLinkSmsOptionsValidator>(config);

        return _AddVictoryLinkSmsSenderCore(services, configureClient, configureResilience);
    }

    public static IServiceCollection AddVictoryLinkSmsSender(
        this IServiceCollection services,
        Action<VictoryLinkSmsOptions, IServiceProvider> setupAction,
        Action<HttpClient>? configureClient = null,
        Action<HttpStandardResilienceOptions>? configureResilience = null
    )
    {
        services.Configure<VictoryLinkSmsOptions, VictoryLinkSmsOptionsValidator>(setupAction);

        return _AddVictoryLinkSmsSenderCore(services, configureClient, configureResilience);
    }

    public static IServiceCollection AddVictoryLinkSmsSender(
        this IServiceCollection services,
        Action<VictoryLinkSmsOptions> setupAction,
        Action<HttpClient>? configureClient = null,
        Action<HttpStandardResilienceOptions>? configureResilience = null
    )
    {
        services.Configure<VictoryLinkSmsOptions, VictoryLinkSmsOptionsValidator>(setupAction);

        return _AddVictoryLinkSmsSenderCore(services, configureClient, configureResilience);
    }

    private static IServiceCollection _AddVictoryLinkSmsSenderCore(
        IServiceCollection services,
        Action<HttpClient>? configureClient,
        Action<HttpStandardResilienceOptions>? configureResilience
    )
    {
        services.AddSingleton<ISmsSender, VictoryLinkSmsSender>();

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
