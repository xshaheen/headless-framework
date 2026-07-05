// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Captcha;

[PublicAPI]
public static class SetupReCaptcha
{
    extension(HeadlessCaptchaSetupBuilder setup)
    {
        #region UseReCaptchaV3

        /// <summary>
        /// Uses Google reCAPTCHA v3 as the default (unkeyed) <see cref="ICaptchaVerifier"/> / <see cref="IReCaptchaV3Verifier"/>,
        /// also aliased under <see cref="CaptchaConstants.ReCaptchaV3Provider"/>. Binds the options from the supplied action.
        /// </summary>
        /// <param name="setupAction">Configuration action for <see cref="ReCaptchaOptions"/>.</param>
        /// <returns>The setup builder for chaining.</returns>
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
                    _AddReCaptchaV3Core(services, CaptchaConstants.ReCaptchaV3Provider, isDefault: true);
                }
            );

            return setup;
        }

        /// <summary>Uses reCAPTCHA v3 as the default verifier with service provider-aware configuration.</summary>
        /// <param name="setupAction">Configuration action with access to the service provider.</param>
        /// <returns>The setup builder for chaining.</returns>
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
                    _AddReCaptchaV3Core(services, CaptchaConstants.ReCaptchaV3Provider, isDefault: true);
                }
            );

            return setup;
        }

        /// <summary>Uses reCAPTCHA v3 as the default verifier, binding <see cref="ReCaptchaOptions"/> from configuration.</summary>
        /// <param name="configuration">The configuration section to bind (for example <c>Headless:Captcha:ReCaptchaV3</c>).</param>
        /// <returns>The setup builder for chaining.</returns>
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
                    _AddReCaptchaV3Core(services, CaptchaConstants.ReCaptchaV3Provider, isDefault: true);
                }
            );

            return setup;
        }

        /// <summary>Adds a named reCAPTCHA v3 verifier, resolvable through <see cref="ICaptchaProvider"/> by <paramref name="name"/>.</summary>
        /// <param name="name">The provider instance name.</param>
        /// <param name="setupAction">Configuration action for <see cref="ReCaptchaOptions"/>.</param>
        /// <returns>The setup builder for chaining.</returns>
        public HeadlessCaptchaSetupBuilder UseReCaptchaV3(string name, Action<ReCaptchaOptions> setupAction)
        {
            Argument.IsNotNullOrWhiteSpace(name);
            Argument.IsNotNull(setupAction);

            setup.RegisterNamed(
                name,
                services =>
                {
                    services.Configure<ReCaptchaOptions, ReCaptchaOptionsValidator>(setupAction, name);
                    _AddReCaptchaV3Core(services, name, isDefault: false);
                }
            );

            return setup;
        }

        /// <summary>Adds a named reCAPTCHA v3 verifier with service provider-aware configuration.</summary>
        /// <param name="name">The provider instance name.</param>
        /// <param name="setupAction">Configuration action with access to the service provider.</param>
        /// <returns>The setup builder for chaining.</returns>
        public HeadlessCaptchaSetupBuilder UseReCaptchaV3(
            string name,
            Action<ReCaptchaOptions, IServiceProvider> setupAction
        )
        {
            Argument.IsNotNullOrWhiteSpace(name);
            Argument.IsNotNull(setupAction);

            setup.RegisterNamed(
                name,
                services =>
                {
                    services.Configure<ReCaptchaOptions, ReCaptchaOptionsValidator>(setupAction, name);
                    _AddReCaptchaV3Core(services, name, isDefault: false);
                }
            );

            return setup;
        }

        /// <summary>Adds a named reCAPTCHA v3 verifier, binding <see cref="ReCaptchaOptions"/> from configuration.</summary>
        /// <param name="name">The provider instance name.</param>
        /// <param name="configuration">The configuration section to bind.</param>
        /// <returns>The setup builder for chaining.</returns>
        public HeadlessCaptchaSetupBuilder UseReCaptchaV3(string name, IConfiguration configuration)
        {
            Argument.IsNotNullOrWhiteSpace(name);
            Argument.IsNotNull(configuration);

            setup.RegisterNamed(
                name,
                services =>
                {
                    services.Configure<ReCaptchaOptions, ReCaptchaOptionsValidator>(configuration, name);
                    _AddReCaptchaV3Core(services, name, isDefault: false);
                }
            );

            return setup;
        }

        #endregion

        #region UseReCaptchaV2

        /// <summary>
        /// Uses Google reCAPTCHA v2 as the default (unkeyed) <see cref="ICaptchaVerifier"/>, also aliased under
        /// <see cref="CaptchaConstants.ReCaptchaV2Provider"/>. Binds the options from the supplied action.
        /// </summary>
        /// <param name="setupAction">Configuration action for <see cref="ReCaptchaOptions"/>.</param>
        /// <returns>The setup builder for chaining.</returns>
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
                    _AddReCaptchaV2Core(services, CaptchaConstants.ReCaptchaV2Provider, isDefault: true);
                }
            );

            return setup;
        }

        /// <summary>Uses reCAPTCHA v2 as the default verifier with service provider-aware configuration.</summary>
        /// <param name="setupAction">Configuration action with access to the service provider.</param>
        /// <returns>The setup builder for chaining.</returns>
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
                    _AddReCaptchaV2Core(services, CaptchaConstants.ReCaptchaV2Provider, isDefault: true);
                }
            );

            return setup;
        }

        /// <summary>Uses reCAPTCHA v2 as the default verifier, binding <see cref="ReCaptchaOptions"/> from configuration.</summary>
        /// <param name="configuration">The configuration section to bind (for example <c>Headless:Captcha:ReCaptchaV2</c>).</param>
        /// <returns>The setup builder for chaining.</returns>
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
                    _AddReCaptchaV2Core(services, CaptchaConstants.ReCaptchaV2Provider, isDefault: true);
                }
            );

            return setup;
        }

        /// <summary>Adds a named reCAPTCHA v2 verifier, resolvable through <see cref="ICaptchaProvider"/> by <paramref name="name"/>.</summary>
        /// <param name="name">The provider instance name.</param>
        /// <param name="setupAction">Configuration action for <see cref="ReCaptchaOptions"/>.</param>
        /// <returns>The setup builder for chaining.</returns>
        public HeadlessCaptchaSetupBuilder UseReCaptchaV2(string name, Action<ReCaptchaOptions> setupAction)
        {
            Argument.IsNotNullOrWhiteSpace(name);
            Argument.IsNotNull(setupAction);

            setup.RegisterNamed(
                name,
                services =>
                {
                    services.Configure<ReCaptchaOptions, ReCaptchaOptionsValidator>(setupAction, name);
                    _AddReCaptchaV2Core(services, name, isDefault: false);
                }
            );

            return setup;
        }

        /// <summary>Adds a named reCAPTCHA v2 verifier with service provider-aware configuration.</summary>
        /// <param name="name">The provider instance name.</param>
        /// <param name="setupAction">Configuration action with access to the service provider.</param>
        /// <returns>The setup builder for chaining.</returns>
        public HeadlessCaptchaSetupBuilder UseReCaptchaV2(
            string name,
            Action<ReCaptchaOptions, IServiceProvider> setupAction
        )
        {
            Argument.IsNotNullOrWhiteSpace(name);
            Argument.IsNotNull(setupAction);

            setup.RegisterNamed(
                name,
                services =>
                {
                    services.Configure<ReCaptchaOptions, ReCaptchaOptionsValidator>(setupAction, name);
                    _AddReCaptchaV2Core(services, name, isDefault: false);
                }
            );

            return setup;
        }

        /// <summary>Adds a named reCAPTCHA v2 verifier, binding <see cref="ReCaptchaOptions"/> from configuration.</summary>
        /// <param name="name">The provider instance name.</param>
        /// <param name="configuration">The configuration section to bind.</param>
        /// <returns>The setup builder for chaining.</returns>
        public HeadlessCaptchaSetupBuilder UseReCaptchaV2(string name, IConfiguration configuration)
        {
            Argument.IsNotNullOrWhiteSpace(name);
            Argument.IsNotNull(configuration);

            setup.RegisterNamed(
                name,
                services =>
                {
                    services.Configure<ReCaptchaOptions, ReCaptchaOptionsValidator>(configuration, name);
                    _AddReCaptchaV2Core(services, name, isDefault: false);
                }
            );

            return setup;
        }

        #endregion
    }

    private static IServiceCollection _AddReCaptchaV3Core(IServiceCollection services, string name, bool isDefault)
    {
        services.TryAddTransient<IReCaptchaLanguageCodeProvider, CultureInfoReCaptchaLanguageCodeProvider>();

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

    private static IServiceCollection _AddReCaptchaV2Core(IServiceCollection services, string name, bool isDefault)
    {
        services.TryAddTransient<IReCaptchaLanguageCodeProvider, CultureInfoReCaptchaLanguageCodeProvider>();

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
