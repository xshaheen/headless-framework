// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace Headless.Sms.Cequens;

[PublicAPI]
public static class SetupCequens
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

        return _AddCequensSmsSenderCore(services, configureClient, configureResilience);
    }

    public static IServiceCollection AddCequensSmsSender(
        this IServiceCollection services,
        Action<CequensSmsOptions> setupAction,
        Action<HttpClient>? configureClient = null,
        Action<HttpStandardResilienceOptions>? configureResilience = null
    )
    {
        services.Configure<CequensSmsOptions, CequensSmsOptionsValidator>(setupAction);

        return _AddCequensSmsSenderCore(services, configureClient, configureResilience);
    }

    public static IServiceCollection AddCequensSmsSender(
        this IServiceCollection services,
        Action<CequensSmsOptions, IServiceProvider> setupAction,
        Action<HttpClient>? configureClient = null,
        Action<HttpStandardResilienceOptions>? configureResilience = null
    )
    {
        services.Configure<CequensSmsOptions, CequensSmsOptionsValidator>(setupAction);

        return _AddCequensSmsSenderCore(services, configureClient, configureResilience);
    }

    private static IServiceCollection _AddCequensSmsSenderCore(
        IServiceCollection services,
        Action<HttpClient>? configureClient,
        Action<HttpStandardResilienceOptions>? configureResilience
    )
    {
        services.AddSingleton<ISmsSender, CequensSmsSender>();

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
