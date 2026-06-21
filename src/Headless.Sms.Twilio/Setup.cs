// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;
using Twilio.Clients;

namespace Headless.Sms.Twilio;

[PublicAPI]
public static class SetupTwilio
{
    internal const string HttpClientName = "Headless:TwilioSms";

    public static IServiceCollection AddTwilioSmsSender(
        this IServiceCollection services,
        IConfiguration config,
        Action<HttpClient>? configureClient = null,
        Action<HttpStandardResilienceOptions>? configureResilience = null
    )
    {
        services.Configure<TwilioSmsOptions, TwilioSmsOptionsValidator>(config);

        return _AddTwilioSmsSenderCore(services, configureClient, configureResilience);
    }

    public static IServiceCollection AddTwilioSmsSender(
        this IServiceCollection services,
        Action<TwilioSmsOptions> setupAction,
        Action<HttpClient>? configureClient = null,
        Action<HttpStandardResilienceOptions>? configureResilience = null
    )
    {
        services.Configure<TwilioSmsOptions, TwilioSmsOptionsValidator>(setupAction);

        return _AddTwilioSmsSenderCore(services, configureClient, configureResilience);
    }

    public static IServiceCollection AddTwilioSmsSender(
        this IServiceCollection services,
        Action<TwilioSmsOptions, IServiceProvider> setupAction,
        Action<HttpClient>? configureClient = null,
        Action<HttpStandardResilienceOptions>? configureResilience = null
    )
    {
        services.Configure<TwilioSmsOptions, TwilioSmsOptionsValidator>(setupAction);

        return _AddTwilioSmsSenderCore(services, configureClient, configureResilience);
    }

    private static IServiceCollection _AddTwilioSmsSenderCore(
        IServiceCollection services,
        Action<HttpClient>? configureClient,
        Action<HttpStandardResilienceOptions>? configureResilience
    )
    {
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

        services.TryAddSingleton<ITwilioRestClient>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<TwilioSmsOptions>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);

            return new TwilioRestClient(
                username: options.Sid,
                password: options.AuthToken,
                accountSid: options.Sid,
                region: options.Region,
                edge: options.Edge,
                httpClient: new global::Twilio.Http.SystemNetHttpClient(httpClient)
            );
        });

        services.AddSingleton<ISmsSender, TwilioSmsSender>();

        return services;
    }
}
