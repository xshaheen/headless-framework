// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Recaptcha.Contracts;
using Framework.Recaptcha.Services;
using Framework.Recaptcha.V2;
using Framework.Recaptcha.V3;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;

namespace Framework.Recaptcha;

[PublicAPI]
public static class ReCaptchaSetup
{
    internal const string V3Name = "Headless:ReCaptchaV3";
    internal const string V2Name = "Headless:ReCaptchaV2";

    extension(IServiceCollection services)
    {
        public IServiceCollection AddReCaptchaV3(
            Action<ReCaptchaOptions>? setupAction,
            Action<HttpClient>? configureClient = null,
            Action<HttpStandardResilienceOptions>? configureResilience = null
        )
        {
            if (setupAction is not null)
            {
                services.Configure<ReCaptchaOptions, ReCaptchaOptionsValidator>(setupAction, V3Name);
            }

            _AddCoreV3(services, configureClient, configureResilience);

            return services;
        }

        public IServiceCollection AddReCaptchaV3(
            Action<ReCaptchaOptions, IServiceProvider>? setupAction,
            Action<HttpClient>? configureClient = null,
            Action<HttpStandardResilienceOptions>? configureResilience = null
        )
        {
            if (setupAction is not null)
            {
                services.Configure<ReCaptchaOptions, ReCaptchaOptionsValidator>(setupAction,V3Name);
            }

            _AddCoreV3(services, configureClient, configureResilience);

            return services;
        }

        public IServiceCollection AddReCaptchaV2(
            Action<ReCaptchaOptions>? setupAction,
            Action<HttpClient>? configureClient = null,
            Action<HttpStandardResilienceOptions>? configureResilience = null
        )
        {
            if (setupAction is not null)
            {
                services.Configure<ReCaptchaOptions, ReCaptchaOptionsValidator>(setupAction, V2Name);
            }

            _AddCoreV2(services, configureClient, configureResilience);

            return services;
        }

        public IServiceCollection AddReCaptchaV2(
            Action<ReCaptchaOptions, IServiceProvider>? setupAction,
            Action<HttpClient>? configureClient = null,
            Action<HttpStandardResilienceOptions>? configureResilience = null
        )
        {
            if (setupAction is not null)
            {
                services.Configure<ReCaptchaOptions, ReCaptchaOptionsValidator>(setupAction, V2Name);
            }

            _AddCoreV2(services, configureClient, configureResilience);

            return services;
        }
    }

    private static void _AddCoreV3(
        IServiceCollection services,
        Action<HttpClient>? configureClient,
        Action<HttpStandardResilienceOptions>? configureResilience
    )
    {
        var httpClientBuilder = services.AddHttpClient(V3Name, (sp, client) =>
        {
            var options = sp.GetRequiredService<IOptionsSnapshot<ReCaptchaOptions>>().Get(V3Name);
            client.BaseAddress = new Uri(options.VerifyBaseUrl);
            configureClient?.Invoke(client);
        });

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
        var httpClientBuilder = services.AddHttpClient(V2Name, (sp, client) =>
        {
            var options = sp.GetRequiredService<IOptionsSnapshot<ReCaptchaOptions>>().Get(V2Name);
            client.BaseAddress = new Uri(options.VerifyBaseUrl);
            configureClient?.Invoke(client);
        });

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
