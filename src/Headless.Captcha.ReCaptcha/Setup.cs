// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Captcha;

/// <summary>
/// Provides extension methods for configuring Google reCAPTCHA v2 and v3 on <see cref="HeadlessCaptchaSetupBuilder"/>.
/// </summary>
[PublicAPI]
public static class SetupReCaptcha
{
    extension(HeadlessCaptchaSetupBuilder setup)
    {
        #region UseReCaptchaV3

        /// <summary>Registers reCAPTCHA v3 as the default verifier by binding options from configuration.</summary>
        /// <param name="configuration">The configuration section containing reCAPTCHA options.</param>
        /// <returns>The setup builder instance.</returns>
        public HeadlessCaptchaSetupBuilder UseReCaptchaV3(IConfiguration configuration)
        {
            Argument.IsNotNull(configuration);

            setup.RegisterDefault(
                CaptchaConstants.ReCaptchaV3Provider,
                services =>
                {
                    services.Configure<ReCaptchaOptions, ReCaptchaOptionsValidator>(
                        configuration,
                        CaptchaConstants.ReCaptchaV3Provider
                    );
                    AddReCaptchaV3Core(services, CaptchaConstants.ReCaptchaV3Provider, isDefault: true);
                }
            );

            return setup;
        }

        /// <summary>Registers reCAPTCHA v3 as the default verifier using an options configuration action.</summary>
        /// <param name="setupAction">The delegate used to configure reCAPTCHA options.</param>
        /// <returns>The setup builder instance.</returns>
        public HeadlessCaptchaSetupBuilder UseReCaptchaV3(Action<ReCaptchaOptions> setupAction)
        {
            Argument.IsNotNull(setupAction);

            setup.RegisterDefault(
                CaptchaConstants.ReCaptchaV3Provider,
                services =>
                {
                    services.Configure<ReCaptchaOptions, ReCaptchaOptionsValidator>(
                        setupAction,
                        CaptchaConstants.ReCaptchaV3Provider
                    );
                    AddReCaptchaV3Core(services, CaptchaConstants.ReCaptchaV3Provider, isDefault: true);
                }
            );

            return setup;
        }

        /// <summary>Registers reCAPTCHA v3 as the default verifier using a service provider-aware options configuration action.</summary>
        /// <param name="setupAction">The delegate used to configure reCAPTCHA options with the service provider.</param>
        /// <returns>The setup builder instance.</returns>
        public HeadlessCaptchaSetupBuilder UseReCaptchaV3(Action<ReCaptchaOptions, IServiceProvider> setupAction)
        {
            Argument.IsNotNull(setupAction);

            setup.RegisterDefault(
                CaptchaConstants.ReCaptchaV3Provider,
                services =>
                {
                    services.Configure<ReCaptchaOptions, ReCaptchaOptionsValidator>(
                        setupAction,
                        CaptchaConstants.ReCaptchaV3Provider
                    );
                    AddReCaptchaV3Core(services, CaptchaConstants.ReCaptchaV3Provider, isDefault: true);
                }
            );

            return setup;
        }

        #endregion

        #region UseReCaptchaV2

        /// <summary>Registers reCAPTCHA v2 as the default verifier by binding options from configuration.</summary>
        /// <param name="configuration">The configuration section containing reCAPTCHA options.</param>
        /// <returns>The setup builder instance.</returns>
        public HeadlessCaptchaSetupBuilder UseReCaptchaV2(IConfiguration configuration)
        {
            Argument.IsNotNull(configuration);

            setup.RegisterDefault(
                CaptchaConstants.ReCaptchaV2Provider,
                services =>
                {
                    services.Configure<ReCaptchaOptions, ReCaptchaOptionsValidator>(
                        configuration,
                        CaptchaConstants.ReCaptchaV2Provider
                    );
                    AddReCaptchaV2Core(services, CaptchaConstants.ReCaptchaV2Provider, isDefault: true);
                }
            );

            return setup;
        }

        /// <summary>Registers reCAPTCHA v2 as the default verifier using an options configuration action.</summary>
        /// <param name="setupAction">The delegate used to configure reCAPTCHA options.</param>
        /// <returns>The setup builder instance.</returns>
        public HeadlessCaptchaSetupBuilder UseReCaptchaV2(Action<ReCaptchaOptions> setupAction)
        {
            Argument.IsNotNull(setupAction);

            setup.RegisterDefault(
                CaptchaConstants.ReCaptchaV2Provider,
                services =>
                {
                    services.Configure<ReCaptchaOptions, ReCaptchaOptionsValidator>(
                        setupAction,
                        CaptchaConstants.ReCaptchaV2Provider
                    );
                    AddReCaptchaV2Core(services, CaptchaConstants.ReCaptchaV2Provider, isDefault: true);
                }
            );

            return setup;
        }

        /// <summary>Registers reCAPTCHA v2 as the default verifier using a service provider-aware options configuration action.</summary>
        /// <param name="setupAction">The delegate used to configure reCAPTCHA options with the service provider.</param>
        /// <returns>The setup builder instance.</returns>
        public HeadlessCaptchaSetupBuilder UseReCaptchaV2(Action<ReCaptchaOptions, IServiceProvider> setupAction)
        {
            Argument.IsNotNull(setupAction);

            setup.RegisterDefault(
                CaptchaConstants.ReCaptchaV2Provider,
                services =>
                {
                    services.Configure<ReCaptchaOptions, ReCaptchaOptionsValidator>(
                        setupAction,
                        CaptchaConstants.ReCaptchaV2Provider
                    );
                    AddReCaptchaV2Core(services, CaptchaConstants.ReCaptchaV2Provider, isDefault: true);
                }
            );

            return setup;
        }

        #endregion
    }

    internal static IServiceCollection AddReCaptchaV3Core(IServiceCollection services, string name, bool isDefault)
    {
        services.TryAddTransient<ICaptchaLanguageCodeProvider, CultureInfoCaptchaLanguageCodeProvider>();

        services
            .AddHttpClient(
                name,
                (sp, client) =>
                {
                    var options = sp.GetRequiredService<IOptionsMonitor<ReCaptchaOptions>>().Get(name);
                    client.BaseAddress = new Uri(options.VerifyBaseUrl);
                }
            )
            // reCAPTCHA tokens are single-use — disable retry on POST to avoid replaying them.
            .AddStandardResilienceHandler(options => options.Retry.DisableForUnsafeHttpMethods());

        services.AddKeyedSingleton<IReCaptchaV3Verifier>(
            name,
            (sp, key) =>
                new ReCaptchaSiteVerifyV3(
                    (string)key,
                    sp.GetRequiredService<IOptionsMonitor<ReCaptchaOptions>>(),
                    sp.GetRequiredService<IHttpClientFactory>(),
                    sp.GetService<ILogger<ReCaptchaSiteVerifyV3>>()
                )
        );
        services.AddKeyedSingleton<ICaptchaVerifier>(
            name,
            (sp, key) => sp.GetRequiredKeyedService<IReCaptchaV3Verifier>(key)
        );

        if (isDefault)
        {
            services.TryAddSingleton<IReCaptchaV3Verifier>(sp =>
                sp.GetRequiredKeyedService<IReCaptchaV3Verifier>(name)
            );
            services.TryAddSingleton<ICaptchaVerifier>(sp => sp.GetRequiredKeyedService<IReCaptchaV3Verifier>(name));
        }

        return services;
    }

    internal static IServiceCollection AddReCaptchaV2Core(IServiceCollection services, string name, bool isDefault)
    {
        services.TryAddTransient<ICaptchaLanguageCodeProvider, CultureInfoCaptchaLanguageCodeProvider>();

        services
            .AddHttpClient(
                name,
                (sp, client) =>
                {
                    var options = sp.GetRequiredService<IOptionsMonitor<ReCaptchaOptions>>().Get(name);
                    client.BaseAddress = new Uri(options.VerifyBaseUrl);
                }
            )
            // reCAPTCHA tokens are single-use — disable retry on POST to avoid replaying them.
            .AddStandardResilienceHandler(options => options.Retry.DisableForUnsafeHttpMethods());

        services.AddKeyedSingleton<ICaptchaVerifier>(
            name,
            (sp, key) =>
                new ReCaptchaSiteVerifyV2(
                    (string)key,
                    sp.GetRequiredService<IOptionsMonitor<ReCaptchaOptions>>(),
                    sp.GetRequiredService<IHttpClientFactory>(),
                    sp.GetService<ILogger<ReCaptchaSiteVerifyV2>>()
                )
        );

        if (isDefault)
        {
            services.TryAddSingleton<ICaptchaVerifier>(sp => sp.GetRequiredKeyedService<ICaptchaVerifier>(name));
        }

        return services;
    }
}

/// <summary>
/// Provides extension methods for configuring Google reCAPTCHA v2 and v3 for a named captcha instance on
/// <see cref="HeadlessCaptchaInstanceBuilder"/>.
/// </summary>
[PublicAPI]
public static class SetupReCaptchaNamed
{
    extension(HeadlessCaptchaInstanceBuilder instance)
    {
        #region UseReCaptchaV3

        /// <summary>Registers reCAPTCHA v3 for this named instance by binding options from configuration.</summary>
        /// <param name="configuration">The configuration section containing reCAPTCHA options.</param>
        /// <returns>The instance builder instance.</returns>
        public HeadlessCaptchaInstanceBuilder UseReCaptchaV3(IConfiguration configuration)
        {
            Argument.IsNotNull(configuration);

            var name = instance.Name;

            instance.RegisterProvider(services =>
            {
                services.Configure<ReCaptchaOptions, ReCaptchaOptionsValidator>(configuration, name);
                SetupReCaptcha.AddReCaptchaV3Core(services, name, isDefault: false);
            });

            return instance;
        }

        /// <summary>Registers reCAPTCHA v3 for this named instance using an options configuration action.</summary>
        /// <param name="setupAction">The delegate used to configure reCAPTCHA options.</param>
        /// <returns>The instance builder instance.</returns>
        public HeadlessCaptchaInstanceBuilder UseReCaptchaV3(Action<ReCaptchaOptions> setupAction)
        {
            Argument.IsNotNull(setupAction);

            var name = instance.Name;

            instance.RegisterProvider(services =>
            {
                services.Configure<ReCaptchaOptions, ReCaptchaOptionsValidator>(setupAction, name);
                SetupReCaptcha.AddReCaptchaV3Core(services, name, isDefault: false);
            });

            return instance;
        }

        /// <summary>Registers reCAPTCHA v3 for this named instance using a service provider-aware options configuration action.</summary>
        /// <param name="setupAction">The delegate used to configure reCAPTCHA options with the service provider.</param>
        /// <returns>The instance builder instance.</returns>
        public HeadlessCaptchaInstanceBuilder UseReCaptchaV3(Action<ReCaptchaOptions, IServiceProvider> setupAction)
        {
            Argument.IsNotNull(setupAction);

            var name = instance.Name;

            instance.RegisterProvider(services =>
            {
                services.Configure<ReCaptchaOptions, ReCaptchaOptionsValidator>(setupAction, name);
                SetupReCaptcha.AddReCaptchaV3Core(services, name, isDefault: false);
            });

            return instance;
        }

        #endregion

        #region UseReCaptchaV2

        /// <summary>Registers reCAPTCHA v2 for this named instance by binding options from configuration.</summary>
        /// <param name="configuration">The configuration section containing reCAPTCHA options.</param>
        /// <returns>The instance builder instance.</returns>
        public HeadlessCaptchaInstanceBuilder UseReCaptchaV2(IConfiguration configuration)
        {
            Argument.IsNotNull(configuration);

            var name = instance.Name;

            instance.RegisterProvider(services =>
            {
                services.Configure<ReCaptchaOptions, ReCaptchaOptionsValidator>(configuration, name);
                SetupReCaptcha.AddReCaptchaV2Core(services, name, isDefault: false);
            });

            return instance;
        }

        /// <summary>Registers reCAPTCHA v2 for this named instance using an options configuration action.</summary>
        /// <param name="setupAction">The delegate used to configure reCAPTCHA options.</param>
        /// <returns>The instance builder instance.</returns>
        public HeadlessCaptchaInstanceBuilder UseReCaptchaV2(Action<ReCaptchaOptions> setupAction)
        {
            Argument.IsNotNull(setupAction);

            var name = instance.Name;

            instance.RegisterProvider(services =>
            {
                services.Configure<ReCaptchaOptions, ReCaptchaOptionsValidator>(setupAction, name);
                SetupReCaptcha.AddReCaptchaV2Core(services, name, isDefault: false);
            });

            return instance;
        }

        /// <summary>Registers reCAPTCHA v2 for this named instance using a service provider-aware options configuration action.</summary>
        /// <param name="setupAction">The delegate used to configure reCAPTCHA options with the service provider.</param>
        /// <returns>The instance builder instance.</returns>
        public HeadlessCaptchaInstanceBuilder UseReCaptchaV2(Action<ReCaptchaOptions, IServiceProvider> setupAction)
        {
            Argument.IsNotNull(setupAction);

            var name = instance.Name;

            instance.RegisterProvider(services =>
            {
                services.Configure<ReCaptchaOptions, ReCaptchaOptionsValidator>(setupAction, name);
                SetupReCaptcha.AddReCaptchaV2Core(services, name, isDefault: false);
            });

            return instance;
        }

        #endregion
    }
}
