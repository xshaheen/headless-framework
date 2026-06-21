// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.ReCaptcha.Contracts;
using Headless.ReCaptcha.Services;
using Headless.ReCaptcha.V2;
using Headless.ReCaptcha.V3;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;

namespace Headless.ReCaptcha;

/// <summary>Extension members that register Google reCAPTCHA v2 or v3 verification services.</summary>
[PublicAPI]
public static class SetupReCaptcha
{
    internal const string V3Name = "Headless:ReCaptchaV3";
    internal const string V2Name = "Headless:ReCaptchaV2";

    extension(IServiceCollection services)
    {
        /// <summary>Adds reCAPTCHA v3 verification, binding options from the given configuration section.</summary>
        /// <param name="configuration">The configuration section that supplies <see cref="ReCaptchaOptions"/>.</param>
        /// <param name="configureClient">Optional hook to further configure the named <see cref="HttpClient"/>.</param>
        /// <param name="configureResilience">Optional hook to override the standard resilience handler.</param>
        /// <returns>The same service collection.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is <see langword="null"/>.</exception>
        public IServiceCollection AddReCaptchaV3(
            IConfiguration configuration,
            Action<HttpClient>? configureClient = null,
            Action<HttpStandardResilienceOptions>? configureResilience = null
        )
        {
            Argument.IsNotNull(configuration);
            services.Configure<ReCaptchaOptions, ReCaptchaOptionsValidator>(configuration, V3Name);

            return _AddReCaptchaCore<IReCaptchaSiteVerifyV3, ReCaptchaSiteVerifyV3>(
                services,
                V3Name,
                configureClient,
                configureResilience
            );
        }

        /// <summary>Adds reCAPTCHA v3 verification, configuring options via a delegate.</summary>
        /// <param name="setupAction">Configures the required <see cref="ReCaptchaOptions"/>.</param>
        /// <param name="configureClient">Optional hook to further configure the named <see cref="HttpClient"/>.</param>
        /// <param name="configureResilience">Optional hook to override the standard resilience handler.</param>
        /// <returns>The same service collection.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="setupAction"/> is <see langword="null"/>.</exception>
        public IServiceCollection AddReCaptchaV3(
            Action<ReCaptchaOptions> setupAction,
            Action<HttpClient>? configureClient = null,
            Action<HttpStandardResilienceOptions>? configureResilience = null
        )
        {
            Argument.IsNotNull(setupAction);
            services.Configure<ReCaptchaOptions, ReCaptchaOptionsValidator>(setupAction, V3Name);

            return _AddReCaptchaCore<IReCaptchaSiteVerifyV3, ReCaptchaSiteVerifyV3>(
                services,
                V3Name,
                configureClient,
                configureResilience
            );
        }

        /// <summary>Adds reCAPTCHA v3 verification, configuring options via a delegate with service-provider access.</summary>
        /// <param name="setupAction">Configures the required <see cref="ReCaptchaOptions"/>.</param>
        /// <param name="configureClient">Optional hook to further configure the named <see cref="HttpClient"/>.</param>
        /// <param name="configureResilience">Optional hook to override the standard resilience handler.</param>
        /// <returns>The same service collection.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="setupAction"/> is <see langword="null"/>.</exception>
        public IServiceCollection AddReCaptchaV3(
            Action<ReCaptchaOptions, IServiceProvider> setupAction,
            Action<HttpClient>? configureClient = null,
            Action<HttpStandardResilienceOptions>? configureResilience = null
        )
        {
            Argument.IsNotNull(setupAction);
            services.Configure<ReCaptchaOptions, ReCaptchaOptionsValidator>(setupAction, V3Name);

            return _AddReCaptchaCore<IReCaptchaSiteVerifyV3, ReCaptchaSiteVerifyV3>(
                services,
                V3Name,
                configureClient,
                configureResilience
            );
        }

        /// <summary>Adds reCAPTCHA v2 verification, binding options from the given configuration section.</summary>
        /// <param name="configuration">The configuration section that supplies <see cref="ReCaptchaOptions"/>.</param>
        /// <param name="configureClient">Optional hook to further configure the named <see cref="HttpClient"/>.</param>
        /// <param name="configureResilience">Optional hook to override the standard resilience handler.</param>
        /// <returns>The same service collection.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is <see langword="null"/>.</exception>
        public IServiceCollection AddReCaptchaV2(
            IConfiguration configuration,
            Action<HttpClient>? configureClient = null,
            Action<HttpStandardResilienceOptions>? configureResilience = null
        )
        {
            Argument.IsNotNull(configuration);
            services.Configure<ReCaptchaOptions, ReCaptchaOptionsValidator>(configuration, V2Name);

            return _AddReCaptchaCore<IReCaptchaSiteVerifyV2, ReCaptchaSiteVerifyV2>(
                services,
                V2Name,
                configureClient,
                configureResilience
            );
        }

        /// <summary>Adds reCAPTCHA v2 verification, configuring options via a delegate.</summary>
        /// <param name="setupAction">Configures the required <see cref="ReCaptchaOptions"/>.</param>
        /// <param name="configureClient">Optional hook to further configure the named <see cref="HttpClient"/>.</param>
        /// <param name="configureResilience">Optional hook to override the standard resilience handler.</param>
        /// <returns>The same service collection.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="setupAction"/> is <see langword="null"/>.</exception>
        public IServiceCollection AddReCaptchaV2(
            Action<ReCaptchaOptions> setupAction,
            Action<HttpClient>? configureClient = null,
            Action<HttpStandardResilienceOptions>? configureResilience = null
        )
        {
            Argument.IsNotNull(setupAction);
            services.Configure<ReCaptchaOptions, ReCaptchaOptionsValidator>(setupAction, V2Name);

            return _AddReCaptchaCore<IReCaptchaSiteVerifyV2, ReCaptchaSiteVerifyV2>(
                services,
                V2Name,
                configureClient,
                configureResilience
            );
        }

        /// <summary>Adds reCAPTCHA v2 verification, configuring options via a delegate with service-provider access.</summary>
        /// <param name="setupAction">Configures the required <see cref="ReCaptchaOptions"/>.</param>
        /// <param name="configureClient">Optional hook to further configure the named <see cref="HttpClient"/>.</param>
        /// <param name="configureResilience">Optional hook to override the standard resilience handler.</param>
        /// <returns>The same service collection.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="setupAction"/> is <see langword="null"/>.</exception>
        public IServiceCollection AddReCaptchaV2(
            Action<ReCaptchaOptions, IServiceProvider> setupAction,
            Action<HttpClient>? configureClient = null,
            Action<HttpStandardResilienceOptions>? configureResilience = null
        )
        {
            Argument.IsNotNull(setupAction);
            services.Configure<ReCaptchaOptions, ReCaptchaOptionsValidator>(setupAction, V2Name);

            return _AddReCaptchaCore<IReCaptchaSiteVerifyV2, ReCaptchaSiteVerifyV2>(
                services,
                V2Name,
                configureClient,
                configureResilience
            );
        }
    }

    private static IServiceCollection _AddReCaptchaCore<TService, TImplementation>(
        IServiceCollection services,
        string name,
        Action<HttpClient>? configureClient,
        Action<HttpStandardResilienceOptions>? configureResilience
    )
        where TService : class
        where TImplementation : class, TService
    {
        var httpClientBuilder = services.AddHttpClient(
            name,
            (sp, client) =>
            {
                // IOptionsMonitor is singleton-safe; the HttpClient factory invokes this from the root provider.
                var options = sp.GetRequiredService<IOptionsMonitor<ReCaptchaOptions>>().Get(name);
                client.BaseAddress = new Uri(options.VerifyBaseUrl);
                configureClient?.Invoke(client);
            }
        );

        httpClientBuilder.AddStandardResilienceHandler(options =>
        {
            // reCAPTCHA tokens are single-use; retrying the verify POST yields a "timeout-or-duplicate" error.
            options.Retry.DisableForUnsafeHttpMethods();
            configureResilience?.Invoke(options);
        });

        services.TryAddTransient<IReCaptchaLanguageCodeProvider, CultureInfoReCaptchaLanguageCodeProvider>();
        services.AddTransient<TService, TImplementation>();

        return services;
    }
}
