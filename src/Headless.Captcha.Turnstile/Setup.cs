// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

#pragma warning disable CA1708 // multiple extension blocks emit marker members differing only by case
namespace Headless.Captcha;

[PublicAPI]
public static class SetupTurnstile
{
    extension(HeadlessCaptchaSetupBuilder setup)
    {
        /// <summary>
        /// Uses Cloudflare Turnstile as the default (unkeyed) <see cref="ICaptchaVerifier"/> / <see cref="ITurnstileVerifier"/>,
        /// also aliased under <see cref="CaptchaConstants.TurnstileProvider"/>. Binds the options from the supplied action.
        /// </summary>
        /// <param name="setupAction">Configuration action for <see cref="TurnstileOptions"/>.</param>
        /// <returns>The setup builder for chaining.</returns>
        public HeadlessCaptchaSetupBuilder UseTurnstile(Action<TurnstileOptions> setupAction)
        {
            Argument.IsNotNull(setupAction);

            setup.RegisterDefault(
                CaptchaConstants.TurnstileProvider,
                services =>
                {
                    services.Configure<TurnstileOptions, TurnstileOptionsValidator>(
                        setupAction,
                        CaptchaConstants.TurnstileProvider
                    );
                    services._AddTurnstileCore(CaptchaConstants.TurnstileProvider, isDefault: true);
                }
            );

            return setup;
        }

        /// <summary>
        /// Uses Cloudflare Turnstile as the default verifier with service provider-aware configuration.
        /// See <see cref="UseTurnstile(HeadlessCaptchaSetupBuilder, Action{TurnstileOptions})"/>.
        /// </summary>
        /// <param name="setupAction">Configuration action with access to the service provider.</param>
        /// <returns>The setup builder for chaining.</returns>
        public HeadlessCaptchaSetupBuilder UseTurnstile(Action<TurnstileOptions, IServiceProvider> setupAction)
        {
            Argument.IsNotNull(setupAction);

            setup.RegisterDefault(
                CaptchaConstants.TurnstileProvider,
                services =>
                {
                    services.Configure<TurnstileOptions, TurnstileOptionsValidator>(
                        setupAction,
                        CaptchaConstants.TurnstileProvider
                    );
                    services._AddTurnstileCore(CaptchaConstants.TurnstileProvider, isDefault: true);
                }
            );

            return setup;
        }

        /// <summary>
        /// Uses Cloudflare Turnstile as the default verifier, binding <see cref="TurnstileOptions"/> from configuration.
        /// </summary>
        /// <param name="configuration">The configuration section to bind (for example <c>Headless:Captcha:Turnstile</c>).</param>
        /// <returns>The setup builder for chaining.</returns>
        public HeadlessCaptchaSetupBuilder UseTurnstile(IConfiguration configuration)
        {
            Argument.IsNotNull(configuration);

            setup.RegisterDefault(
                CaptchaConstants.TurnstileProvider,
                services =>
                {
                    services.Configure<TurnstileOptions, TurnstileOptionsValidator>(
                        configuration,
                        CaptchaConstants.TurnstileProvider
                    );
                    services._AddTurnstileCore(CaptchaConstants.TurnstileProvider, isDefault: true);
                }
            );

            return setup;
        }

        /// <summary>
        /// Adds a named Cloudflare Turnstile verifier, resolvable through <see cref="ICaptchaProvider"/> by
        /// <paramref name="name"/> or as a keyed <see cref="ICaptchaVerifier"/> / <see cref="ITurnstileVerifier"/>.
        /// </summary>
        /// <param name="name">The provider instance name.</param>
        /// <param name="setupAction">Configuration action for <see cref="TurnstileOptions"/>.</param>
        /// <returns>The setup builder for chaining.</returns>
        public HeadlessCaptchaSetupBuilder UseTurnstile(string name, Action<TurnstileOptions> setupAction)
        {
            Argument.IsNotNullOrWhiteSpace(name);
            Argument.IsNotNull(setupAction);

            setup.RegisterNamed(
                name,
                services =>
                {
                    services.Configure<TurnstileOptions, TurnstileOptionsValidator>(setupAction, name);
                    services._AddTurnstileCore(name, isDefault: false);
                }
            );

            return setup;
        }

        /// <summary>
        /// Adds a named Cloudflare Turnstile verifier with service provider-aware configuration.
        /// See <see cref="UseTurnstile(HeadlessCaptchaSetupBuilder, string, Action{TurnstileOptions})"/>.
        /// </summary>
        /// <param name="name">The provider instance name.</param>
        /// <param name="setupAction">Configuration action with access to the service provider.</param>
        /// <returns>The setup builder for chaining.</returns>
        public HeadlessCaptchaSetupBuilder UseTurnstile(
            string name,
            Action<TurnstileOptions, IServiceProvider> setupAction
        )
        {
            Argument.IsNotNullOrWhiteSpace(name);
            Argument.IsNotNull(setupAction);

            setup.RegisterNamed(
                name,
                services =>
                {
                    services.Configure<TurnstileOptions, TurnstileOptionsValidator>(setupAction, name);
                    services._AddTurnstileCore(name, isDefault: false);
                }
            );

            return setup;
        }

        /// <summary>
        /// Adds a named Cloudflare Turnstile verifier, binding <see cref="TurnstileOptions"/> from configuration.
        /// </summary>
        /// <param name="name">The provider instance name.</param>
        /// <param name="configuration">The configuration section to bind.</param>
        /// <returns>The setup builder for chaining.</returns>
        public HeadlessCaptchaSetupBuilder UseTurnstile(string name, IConfiguration configuration)
        {
            Argument.IsNotNullOrWhiteSpace(name);
            Argument.IsNotNull(configuration);

            setup.RegisterNamed(
                name,
                services =>
                {
                    services.Configure<TurnstileOptions, TurnstileOptionsValidator>(configuration, name);
                    services._AddTurnstileCore(name, isDefault: false);
                }
            );

            return setup;
        }
    }

    extension(IServiceCollection services)
    {
        private IServiceCollection _AddTurnstileCore(string name, bool isDefault)
        {
            services.TryAddTransient<ITurnstileLanguageCodeProvider, CultureInfoTurnstileLanguageCodeProvider>();

            services
                .AddHttpClient(
                    name,
                    (sp, client) =>
                    {
                        var options = sp.GetRequiredService<IOptionsMonitor<TurnstileOptions>>().Get(name);
                        client.BaseAddress = new Uri(options.VerifyBaseUrl);
                    }
                )
                // Turnstile tokens are single-use — disable retry on POST to avoid replaying them.
                .AddStandardResilienceHandler(options => options.Retry.DisableForUnsafeHttpMethods());

            services.AddKeyedSingleton<ITurnstileVerifier>(
                name,
                (sp, key) =>
                    new TurnstileSiteVerify(
                        (string)key,
                        sp.GetRequiredService<IOptionsMonitor<TurnstileOptions>>(),
                        sp.GetRequiredService<IHttpClientFactory>(),
                        sp.GetService<ILogger<TurnstileSiteVerify>>()
                    )
            );
            services.AddKeyedSingleton<ICaptchaVerifier>(
                name,
                (sp, key) => sp.GetRequiredKeyedService<ITurnstileVerifier>(key)
            );

            if (isDefault)
            {
                services.TryAddSingleton<ITurnstileVerifier>(sp =>
                    sp.GetRequiredKeyedService<ITurnstileVerifier>(name)
                );
                services.TryAddSingleton<ICaptchaVerifier>(sp => sp.GetRequiredKeyedService<ITurnstileVerifier>(name));
            }

            return services;
        }
    }
}
