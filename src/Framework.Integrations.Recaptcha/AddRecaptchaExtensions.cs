global using JetBrainsPure = JetBrains.Annotations.PureAttribute;
global using SystemPure = System.Diagnostics.Contracts.PureAttribute;
using Framework.Integrations.Recaptcha.Contracts;
using Framework.Integrations.Recaptcha.Services;
using Framework.Integrations.Recaptcha.V2;
using Framework.Integrations.Recaptcha.V3;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Framework.Integrations.Recaptcha;

public static class AddRecaptchaExtensions
{
    public static IServiceCollection AddReCaptchaV3(
        this IServiceCollection services,
        Action<ReCaptchaSettings>? setupAction,
        Action<HttpClient>? configureClient = null
    )
    {
        if (setupAction is not null)
        {
            services.ConfigureOptions<ReCaptchaSettings, RecaptchaSettingsValidator>(
                setupAction,
                ReCaptchaConstants.V3
            );
        }

        _AddCoreV3(services, configureClient);

        return services;
    }

    public static IServiceCollection AddReCaptchaV3(
        this IServiceCollection services,
        Action<ReCaptchaSettings, IServiceProvider>? setupAction,
        Action<HttpClient>? configureClient = null
    )
    {
        if (setupAction is not null)
        {
            services.ConfigureOptions<ReCaptchaSettings, RecaptchaSettingsValidator>(
                setupAction,
                ReCaptchaConstants.V3
            );
        }

        _AddCoreV3(services, configureClient);

        return services;
    }

    public static IServiceCollection AddReCaptchaV2(
        this IServiceCollection services,
        Action<ReCaptchaSettings>? setupAction,
        Action<HttpClient>? configureClient = null
    )
    {
        if (setupAction is not null)
        {
            services.ConfigureOptions<ReCaptchaSettings, RecaptchaSettingsValidator>(
                setupAction,
                ReCaptchaConstants.V2
            );
        }

        _AddCoreV2(services, configureClient);

        return services;
    }

    public static IServiceCollection AddReCaptchaV2(
        this IServiceCollection services,
        Action<ReCaptchaSettings, IServiceProvider>? setupAction,
        Action<HttpClient>? configureClient = null
    )
    {
        if (setupAction is not null)
        {
            services.ConfigureOptions<ReCaptchaSettings, RecaptchaSettingsValidator>(
                setupAction,
                ReCaptchaConstants.V2
            );
        }

        _AddCoreV2(services, configureClient);

        return services;
    }

    private static void _AddCoreV3(IServiceCollection services, Action<HttpClient>? configureClient)
    {
        if (configureClient is null)
        {
            services.AddHttpClient(ReCaptchaConstants.V3).AddStandardResilienceHandler();
        }
        else
        {
            services.AddHttpClient(ReCaptchaConstants.V3, configureClient).AddStandardResilienceHandler();
        }

        services.TryAddTransient<IReCaptchaLanguageCodeProvider, CultureInfoReCaptchaLanguageCodeProvider>();
        services.AddTransient<IReCaptchaSiteVerifyV3, ReCaptchaSiteVerifyV3>();
    }

    private static void _AddCoreV2(IServiceCollection services, Action<HttpClient>? configureClient)
    {
        if (configureClient is null)
        {
            services.AddHttpClient(ReCaptchaConstants.V2).AddStandardResilienceHandler();
        }
        else
        {
            services.AddHttpClient(ReCaptchaConstants.V2, configureClient).AddStandardResilienceHandler();
        }

        services.TryAddTransient<IReCaptchaLanguageCodeProvider, CultureInfoReCaptchaLanguageCodeProvider>();
        services.AddTransient<IReCaptchaSiteVerifyV2, ReCaptchaSiteVerifyV2>();
    }
}
