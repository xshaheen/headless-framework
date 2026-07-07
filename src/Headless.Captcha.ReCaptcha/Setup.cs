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
/// Extension members for selecting Google reCAPTCHA (v2 or v3) as the default (unkeyed) captcha verifier on
/// <see cref="HeadlessCaptchaSetupBuilder"/>. Named instances are configured through <see cref="SetupReCaptchaNamed"/>
/// (<c>setup.AddNamed("name", i =&gt; i.UseReCaptchaV3(...))</c>).
/// </summary>
[PublicAPI]
public static class SetupReCaptcha
{
    extension(HeadlessCaptchaSetupBuilder setup)
    {
        #region UseReCaptchaV3

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
                    AddReCaptchaV3Core(services, CaptchaConstants.ReCaptchaV3Provider, isDefault: true);
                }
            );

            return setup;
        }

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
                    AddReCaptchaV3Core(services, CaptchaConstants.ReCaptchaV3Provider, isDefault: true);
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
                    AddReCaptchaV3Core(services, CaptchaConstants.ReCaptchaV3Provider, isDefault: true);
                }
            );

            return setup;
        }

        #endregion

        #region UseReCaptchaV2

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
                    AddReCaptchaV2Core(services, CaptchaConstants.ReCaptchaV2Provider, isDefault: true);
                }
            );

            return setup;
        }

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
                    AddReCaptchaV2Core(services, CaptchaConstants.ReCaptchaV2Provider, isDefault: true);
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
/// Extension members for selecting Google reCAPTCHA (v2 or v3) for a named captcha instance on
/// <see cref="HeadlessCaptchaInstanceBuilder"/>. The instance owns its own named options and HTTP client, resolves as
/// a keyed <see cref="ICaptchaVerifier"/> (v3 also as a keyed <see cref="IReCaptchaV3Verifier"/>) or through
/// <see cref="ICaptchaProvider"/>, and never touches the default verifier.
/// </summary>
[PublicAPI]
public static class SetupReCaptchaNamed
{
    extension(HeadlessCaptchaInstanceBuilder instance)
    {
        #region UseReCaptchaV3

        /// <summary>Uses reCAPTCHA v3 for this named instance, binding <see cref="ReCaptchaOptions"/> from configuration.</summary>
        /// <param name="configuration">The configuration section to bind.</param>
        /// <returns>The instance builder for chaining.</returns>
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

        /// <summary>Uses reCAPTCHA v3 for this named instance, configuring <see cref="ReCaptchaOptions"/> via a delegate.</summary>
        /// <param name="setupAction">Configuration action for <see cref="ReCaptchaOptions"/>.</param>
        /// <returns>The instance builder for chaining.</returns>
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

        /// <summary>Uses reCAPTCHA v3 for this named instance with service provider-aware configuration.</summary>
        /// <param name="setupAction">Configuration action with access to the service provider.</param>
        /// <returns>The instance builder for chaining.</returns>
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

        /// <summary>Uses reCAPTCHA v2 for this named instance, binding <see cref="ReCaptchaOptions"/> from configuration.</summary>
        /// <param name="configuration">The configuration section to bind.</param>
        /// <returns>The instance builder for chaining.</returns>
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

        /// <summary>Uses reCAPTCHA v2 for this named instance, configuring <see cref="ReCaptchaOptions"/> via a delegate.</summary>
        /// <param name="setupAction">Configuration action for <see cref="ReCaptchaOptions"/>.</param>
        /// <returns>The instance builder for chaining.</returns>
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

        /// <summary>Uses reCAPTCHA v2 for this named instance with service provider-aware configuration.</summary>
        /// <param name="setupAction">Configuration action with access to the service provider.</param>
        /// <returns>The instance builder for chaining.</returns>
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
