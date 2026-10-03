// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Http.Resilience;
using Headless.Sms.Vodafone;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Sms;

/// <summary>
/// Extension members for selecting Vodafone Egypt as the default (unkeyed) SMS provider on
/// <see cref="HeadlessSmsSetupBuilder"/>. Named instances are configured through
/// <see cref="SetupVodafoneNamed"/>.
/// </summary>
[PublicAPI]
public static class SetupVodafone
{
    internal const string HttpClientName = "Headless:VodafoneSms";

    extension(HeadlessSmsSetupBuilder setup)
    {
        /// <summary>Selects Vodafone Egypt, binding and validating <see cref="VodafoneSmsOptions"/> from configuration.</summary>
        /// <remarks>
        /// HTTP retry is disabled by default because SMS sends are not idempotent. Pass
        /// <paramref name="configureResilience"/> to opt back in.
        /// </remarks>
        /// <param name="config">Configuration section containing <see cref="VodafoneSmsOptions"/> values.</param>
        /// <param name="configureClient">Optional delegate to further configure the underlying <see cref="HttpClient"/>.</param>
        /// <param name="configureResilience">Optional delegate to override the default resilience pipeline.</param>
        /// <returns>The same builder, for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="config"/> is <see langword="null"/>.</exception>
        public HeadlessSmsSetupBuilder UseVodafone(
            IConfiguration config,
            Action<HttpClient>? configureClient = null,
            Action<HttpStandardResilienceOptions>? configureResilience = null
        )
        {
            Argument.IsNotNull(config);

            setup.RegisterDefaultProvider(services =>
                AddVodafoneSmsCore(
                    services,
                    name: null,
                    (s, n) => s.Configure<VodafoneSmsOptions, VodafoneSmsOptionsValidator>(config, n),
                    configureClient,
                    configureResilience
                )
            );

            return setup;
        }

        /// <summary>Selects Vodafone Egypt, configuring <see cref="VodafoneSmsOptions"/> via a delegate.</summary>
        /// <param name="setupAction">Delegate that populates the options.</param>
        /// <param name="configureClient">Optional delegate to further configure the underlying <see cref="HttpClient"/>.</param>
        /// <param name="configureResilience">Optional delegate to override the default resilience pipeline.</param>
        /// <returns>The same builder, for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="setupAction"/> is <see langword="null"/>.</exception>
        public HeadlessSmsSetupBuilder UseVodafone(
            Action<VodafoneSmsOptions> setupAction,
            Action<HttpClient>? configureClient = null,
            Action<HttpStandardResilienceOptions>? configureResilience = null
        )
        {
            Argument.IsNotNull(setupAction);

            setup.RegisterDefaultProvider(services =>
                AddVodafoneSmsCore(
                    services,
                    name: null,
                    (s, n) => s.Configure<VodafoneSmsOptions, VodafoneSmsOptionsValidator>(setupAction, n),
                    configureClient,
                    configureResilience
                )
            );

            return setup;
        }

        /// <summary>Selects Vodafone Egypt, configuring <see cref="VodafoneSmsOptions"/> with access to the service provider.</summary>
        /// <param name="setupAction">Delegate that populates the options, with access to the resolved service provider.</param>
        /// <param name="configureClient">Optional delegate to further configure the underlying <see cref="HttpClient"/>.</param>
        /// <param name="configureResilience">Optional delegate to override the default resilience pipeline.</param>
        /// <returns>The same builder, for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="setupAction"/> is <see langword="null"/>.</exception>
        public HeadlessSmsSetupBuilder UseVodafone(
            Action<VodafoneSmsOptions, IServiceProvider> setupAction,
            Action<HttpClient>? configureClient = null,
            Action<HttpStandardResilienceOptions>? configureResilience = null
        )
        {
            Argument.IsNotNull(setupAction);

            setup.RegisterDefaultProvider(services =>
                AddVodafoneSmsCore(
                    services,
                    name: null,
                    (s, n) => s.Configure<VodafoneSmsOptions, VodafoneSmsOptionsValidator>(setupAction, n),
                    configureClient,
                    configureResilience
                )
            );

            return setup;
        }
    }

    /// <summary>
    /// Registers the Vodafone SMS sender through the shared HTTP provider path: options, a per-instance HttpClient whose
    /// pipeline is derived from the declared <see cref="OutboundEffect.Unsafe"/> effect, and the default or keyed
    /// sender with its bulk forward.
    /// </summary>
    internal static void AddVodafoneSmsCore(
        IServiceCollection services,
        string? name,
        Action<IServiceCollection, string?> configureOptions,
        Action<HttpClient>? configureClient,
        Action<HttpStandardResilienceOptions>? configureResilience
    )
    {
        // SMS sends are not idempotent: a retried send can deliver twice.
        HttpSmsProviderRegistration.AddHttpSmsProvider(
            services,
            name,
            HttpClientName,
            configureOptions,
            static (sp, httpClientName, optionsName) =>
                new VodafoneSmsSender(
                    sp.GetRequiredService<IHttpClientFactory>(),
                    httpClientName,
                    sp.GetRequiredService<IOptionsMonitor<VodafoneSmsOptions>>(),
                    optionsName,
                    sp.GetRequiredService<ILogger<VodafoneSmsSender>>()
                ),
            OutboundEffect.Unsafe,
            configureClient,
            configureResilience
        );
    }
}

/// <summary>
/// Extension members for selecting Vodafone Egypt for a named SMS instance on
/// <see cref="HeadlessSmsInstanceBuilder"/>. The instance owns its own named options, HttpClient (and
/// resilience pipeline), and keyed sender; it never shares them with the default sender or other named
/// instances.
/// </summary>
[PublicAPI]
public static class SetupVodafoneNamed
{
    extension(HeadlessSmsInstanceBuilder instance)
    {
        /// <summary>Uses Vodafone Egypt for this named instance, binding and validating <see cref="VodafoneSmsOptions"/> from configuration.</summary>
        /// <remarks>
        /// HTTP retry is disabled by default because SMS sends are not idempotent. Pass
        /// <paramref name="configureResilience"/> to opt back in (ideally after verifying the provider
        /// supports an idempotency key).
        /// </remarks>
        /// <param name="config">Configuration section containing <see cref="VodafoneSmsOptions"/> values.</param>
        /// <param name="configureClient">Optional delegate to further configure the underlying <see cref="HttpClient"/>.</param>
        /// <param name="configureResilience">Optional delegate to override the default resilience pipeline.</param>
        /// <returns>The instance builder, for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="config"/> is <see langword="null"/>.</exception>
        public HeadlessSmsInstanceBuilder UseVodafone(
            IConfiguration config,
            Action<HttpClient>? configureClient = null,
            Action<HttpStandardResilienceOptions>? configureResilience = null
        )
        {
            Argument.IsNotNull(config);

            var name = instance.Name;

            instance.RegisterProvider(services =>
                SetupVodafone.AddVodafoneSmsCore(
                    services,
                    name,
                    (s, n) => s.Configure<VodafoneSmsOptions, VodafoneSmsOptionsValidator>(config, n),
                    configureClient,
                    configureResilience
                )
            );

            return instance;
        }

        /// <summary>Uses Vodafone Egypt for this named instance, configuring <see cref="VodafoneSmsOptions"/> via a delegate.</summary>
        /// <param name="setupAction">Delegate that populates the options.</param>
        /// <param name="configureClient">Optional delegate to further configure the underlying <see cref="HttpClient"/>.</param>
        /// <param name="configureResilience">Optional delegate to override the default resilience pipeline.</param>
        /// <returns>The instance builder, for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="setupAction"/> is <see langword="null"/>.</exception>
        public HeadlessSmsInstanceBuilder UseVodafone(
            Action<VodafoneSmsOptions> setupAction,
            Action<HttpClient>? configureClient = null,
            Action<HttpStandardResilienceOptions>? configureResilience = null
        )
        {
            Argument.IsNotNull(setupAction);

            var name = instance.Name;

            instance.RegisterProvider(services =>
                SetupVodafone.AddVodafoneSmsCore(
                    services,
                    name,
                    (s, n) => s.Configure<VodafoneSmsOptions, VodafoneSmsOptionsValidator>(setupAction, n),
                    configureClient,
                    configureResilience
                )
            );

            return instance;
        }

        /// <summary>Uses Vodafone Egypt for this named instance, configuring <see cref="VodafoneSmsOptions"/> with access to the service provider.</summary>
        /// <param name="setupAction">Delegate that populates the options, with access to the resolved service provider.</param>
        /// <param name="configureClient">Optional delegate to further configure the underlying <see cref="HttpClient"/>.</param>
        /// <param name="configureResilience">Optional delegate to override the default resilience pipeline.</param>
        /// <returns>The instance builder, for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="setupAction"/> is <see langword="null"/>.</exception>
        public HeadlessSmsInstanceBuilder UseVodafone(
            Action<VodafoneSmsOptions, IServiceProvider> setupAction,
            Action<HttpClient>? configureClient = null,
            Action<HttpStandardResilienceOptions>? configureResilience = null
        )
        {
            Argument.IsNotNull(setupAction);

            var name = instance.Name;

            instance.RegisterProvider(services =>
                SetupVodafone.AddVodafoneSmsCore(
                    services,
                    name,
                    (s, n) => s.Configure<VodafoneSmsOptions, VodafoneSmsOptionsValidator>(setupAction, n),
                    configureClient,
                    configureResilience
                )
            );

            return instance;
        }
    }
}
