// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace Headless.Sms.Connekio;

[PublicAPI]
public static class SetupConnekio
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

        return _AddConnekioSmsSenderCore(services, configureClient, configureResilience);
    }

    public static IServiceCollection AddConnekioSmsSender(
        this IServiceCollection services,
        Action<ConnekioSmsOptions, IServiceProvider> setupAction,
        Action<HttpClient>? configureClient = null,
        Action<HttpStandardResilienceOptions>? configureResilience = null
    )
    {
        services.Configure<ConnekioSmsOptions, ConnekioSmsOptionsValidator>(setupAction);

        return _AddConnekioSmsSenderCore(services, configureClient, configureResilience);
    }

    public static IServiceCollection AddConnekioSmsSender(
        this IServiceCollection services,
        Action<ConnekioSmsOptions> setupAction,
        Action<HttpClient>? configureClient = null,
        Action<HttpStandardResilienceOptions>? configureResilience = null
    )
    {
        services.Configure<ConnekioSmsOptions, ConnekioSmsOptionsValidator>(setupAction);

        return _AddConnekioSmsSenderCore(services, configureClient, configureResilience);
    }

    private static IServiceCollection _AddConnekioSmsSenderCore(
        IServiceCollection services,
        Action<HttpClient>? configureClient,
        Action<HttpStandardResilienceOptions>? configureResilience
    )
    {
        services.AddSingleton<ISmsSender, ConnekioSmsSender>();

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
