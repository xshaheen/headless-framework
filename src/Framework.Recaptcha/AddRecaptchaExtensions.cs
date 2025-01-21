// Copyright (c) Mahmoud Shaheen. All rights reserved.

global using JetBrainsPure = JetBrains.Annotations.PureAttribute;
global using SystemPure = System.Diagnostics.Contracts.PureAttribute;
using Framework.Recaptcha.Contracts;
using Framework.Recaptcha.Services;
using Framework.Recaptcha.V2;
using Framework.Recaptcha.V3;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;

namespace Framework.Recaptcha;

[PublicAPI]
public static class AddRecaptchaExtensions
{
    public static IServiceCollection AddReCaptchaV3(
        this IServiceCollection services,
        Action<ReCaptchaOptions>? setupAction,
        Action<HttpClient>? configureClient = null,
        Action<HttpStandardResilienceOptions>? configureResilience = null
    )
    {
        if (setupAction is not null)
        {
            services.Configure<ReCaptchaOptions, RecaptchaOptionsValidator>(setupAction, ReCaptchaConstants.V3);
        }

        _AddCoreV3(services, configureClient, configureResilience);

        return services;
    }

    public static IServiceCollection AddReCaptchaV3(
        this IServiceCollection services,
        Action<ReCaptchaOptions, IServiceProvider>? setupAction,
        Action<HttpClient>? configureClient = null,
        Action<HttpStandardResilienceOptions>? configureResilience = null
    )
    {
        if (setupAction is not null)
        {
            services.Configure<ReCaptchaOptions, RecaptchaOptionsValidator>(setupAction, ReCaptchaConstants.V3);
        }

        _AddCoreV3(services, configureClient, configureResilience);

        return services;
    }

    public static IServiceCollection AddReCaptchaV2(
        this IServiceCollection services,
        Action<ReCaptchaOptions>? setupAction,
        Action<HttpClient>? configureClient = null,
        Action<HttpStandardResilienceOptions>? configureResilience = null
    )
    {
        if (setupAction is not null)
        {
            services.Configure<ReCaptchaOptions, RecaptchaOptionsValidator>(setupAction, ReCaptchaConstants.V2);
        }

        _AddCoreV2(services, configureClient, configureResilience);

        return services;
    }

    public static IServiceCollection AddReCaptchaV2(
        this IServiceCollection services,
        Action<ReCaptchaOptions, IServiceProvider>? setupAction,
        Action<HttpClient>? configureClient = null,
        Action<HttpStandardResilienceOptions>? configureResilience = null
    )
    {
        if (setupAction is not null)
        {
            services.Configure<ReCaptchaOptions, RecaptchaOptionsValidator>(setupAction, ReCaptchaConstants.V2);
        }

        _AddCoreV2(services, configureClient, configureResilience);

        return services;
    }

    private static void _AddCoreV3(
        IServiceCollection services,
        Action<HttpClient>? configureClient,
        Action<HttpStandardResilienceOptions>? configureResilience
    )
    {
        var httpClientBuilder = configureClient is null
            ? services.AddHttpClient(ReCaptchaConstants.V3)
            : services.AddHttpClient(ReCaptchaConstants.V3, configureClient);

        if (configureResilience is not null)
        {
            httpClientBuilder.AddStandardResilienceHandler(configureResilience);
        }
        else
        {
            httpClientBuilder.AddStandardResilienceHandler();
        }

        services.TryAddTransient<IReCaptchaLanguageCodeProvider, CultureInfoReCaptchaLanguageCodeProvider>();
        services.AddTransient<IReCaptchaSiteVerifyV3, ReCaptchaSiteVerifyV3>();
    }

    private static void _AddCoreV2(
        IServiceCollection services,
        Action<HttpClient>? configureClient,
        Action<HttpStandardResilienceOptions>? configureResilience
    )
    {
        var httpClientBuilder = configureClient is null
            ? services.AddHttpClient(ReCaptchaConstants.V2)
            : services.AddHttpClient(ReCaptchaConstants.V2, configureClient);

        if (configureResilience is not null)
        {
            httpClientBuilder.AddStandardResilienceHandler(configureResilience);
        }
        else
        {
            httpClientBuilder.AddStandardResilienceHandler();
        }

        services.TryAddTransient<IReCaptchaLanguageCodeProvider, CultureInfoReCaptchaLanguageCodeProvider>();
        services.AddTransient<IReCaptchaSiteVerifyV2, ReCaptchaSiteVerifyV2>();
    }
}
