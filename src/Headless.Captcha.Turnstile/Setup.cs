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
/// Extension members for selecting Cloudflare Turnstile as the default (unkeyed) captcha verifier on
/// <see cref="HeadlessCaptchaSetupBuilder"/>. Named instances are configured through <see cref="SetupTurnstileNamed"/>
/// (<c>setup.AddNamed("name", i =&gt; i.UseTurnstile(...))</c>).
/// </summary>
[PublicAPI]
public static class SetupTurnstile
{
    extension(HeadlessCaptchaSetupBuilder setup)
    {
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
                    AddTurnstileCore(services, CaptchaConstants.TurnstileProvider, isDefault: true);
                }
            );

            return setup;
        }

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
                    AddTurnstileCore(services, CaptchaConstants.TurnstileProvider, isDefault: true);
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
                    AddTurnstileCore(services, CaptchaConstants.TurnstileProvider, isDefault: true);
                }
            );

            return setup;
        }
    }

    internal static IServiceCollection AddTurnstileCore(IServiceCollection services, string name, bool isDefault)
    {
        services.TryAddTransient<ICaptchaLanguageCodeProvider, CultureInfoCaptchaLanguageCodeProvider>();

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
            services.TryAddSingleton<ITurnstileVerifier>(sp => sp.GetRequiredKeyedService<ITurnstileVerifier>(name));
            services.TryAddSingleton<ICaptchaVerifier>(sp => sp.GetRequiredKeyedService<ITurnstileVerifier>(name));
        }

        return services;
    }
}

/// <summary>
/// Extension members for selecting Cloudflare Turnstile for a named captcha instance on
/// <see cref="HeadlessCaptchaInstanceBuilder"/>. The instance owns its own named options and HTTP client, resolves as
/// a keyed <see cref="ICaptchaVerifier"/> / <see cref="ITurnstileVerifier"/> or through <see cref="ICaptchaProvider"/>,
/// and never touches the default verifier.
/// </summary>
[PublicAPI]
public static class SetupTurnstileNamed
{
    extension(HeadlessCaptchaInstanceBuilder instance)
    {
        /// <summary>Uses Cloudflare Turnstile for this named instance, binding <see cref="TurnstileOptions"/> from configuration.</summary>
        /// <param name="configuration">The configuration section to bind.</param>
        /// <returns>The instance builder for chaining.</returns>
        public HeadlessCaptchaInstanceBuilder UseTurnstile(IConfiguration configuration)
        {
            Argument.IsNotNull(configuration);

            var name = instance.Name;

            instance.RegisterProvider(services =>
            {
                services.Configure<TurnstileOptions, TurnstileOptionsValidator>(configuration, name);
                SetupTurnstile.AddTurnstileCore(services, name, isDefault: false);
            });

            return instance;
        }

        /// <summary>Uses Cloudflare Turnstile for this named instance, configuring <see cref="TurnstileOptions"/> via a delegate.</summary>
        /// <param name="setupAction">Configuration action for <see cref="TurnstileOptions"/>.</param>
        /// <returns>The instance builder for chaining.</returns>
        public HeadlessCaptchaInstanceBuilder UseTurnstile(Action<TurnstileOptions> setupAction)
        {
            Argument.IsNotNull(setupAction);

            var name = instance.Name;

            instance.RegisterProvider(services =>
            {
                services.Configure<TurnstileOptions, TurnstileOptionsValidator>(setupAction, name);
                SetupTurnstile.AddTurnstileCore(services, name, isDefault: false);
            });

            return instance;
        }

        /// <summary>Uses Cloudflare Turnstile for this named instance with service provider-aware configuration.</summary>
        /// <param name="setupAction">Configuration action with access to the service provider.</param>
        /// <returns>The instance builder for chaining.</returns>
        public HeadlessCaptchaInstanceBuilder UseTurnstile(Action<TurnstileOptions, IServiceProvider> setupAction)
        {
            Argument.IsNotNull(setupAction);

            var name = instance.Name;

            instance.RegisterProvider(services =>
            {
                services.Configure<TurnstileOptions, TurnstileOptionsValidator>(setupAction, name);
                SetupTurnstile.AddTurnstileCore(services, name, isDefault: false);
            });

            return instance;
        }
    }
}
