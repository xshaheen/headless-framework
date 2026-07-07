// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Sms.Vodafone;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
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
    /// The Vodafone HttpClient name doubles as the resilience-pipeline key, so each named instance gets its
    /// own client registration (and pipeline) suffixed with the instance name.
    /// </summary>
    internal static string GetHttpClientName(string? name)
    {
        return name is null ? HttpClientName : $"{HttpClientName}:{name}";
    }

    /// <summary>
    /// Registers the Vodafone Egypt SMS sender. <paramref name="name"/> <see langword="null"/> registers the default
    /// (unkeyed) sender; a non-null name registers a keyed sender (plus keyed bulk forward), named options,
    /// and a per-name HttpClient with its own resilience pipeline. Every factory reads the options snapshot
    /// for its own name (<c>IOptionsMonitor.Get(name)</c>) so keyed settings never bleed across instances —
    /// keyed DI does not cascade the key to ctor dependencies, and a keyed sender must not read
    /// <c>CurrentValue</c> (which binds the default).
    /// </summary>
    internal static void AddVodafoneSmsCore(
        IServiceCollection services,
        string? name,
        Action<IServiceCollection, string?> configureOptions,
        Action<HttpClient>? configureClient,
        Action<HttpStandardResilienceOptions>? configureResilience
    )
    {
        configureOptions(services, name);

        var httpClientName = GetHttpClientName(name);

        var httpClientBuilder = configureClient is null
            ? services.AddHttpClient(httpClientName)
            : services.AddHttpClient(httpClientName, configureClient);

        // SMS sends are not idempotent: don't auto-retry by default to avoid duplicate messages.
        // Consumers can opt back in via configureResilience (ideally with a provider idempotency key).
        httpClientBuilder.AddStandardResilienceHandler(options =>
        {
            options.Retry.ShouldHandle = static _ => PredicateResult.False();
            configureResilience?.Invoke(options);
        });

        if (name is null)
        {
            services.AddSingleton<ISmsSender>(static sp => new VodafoneSmsSender(
                sp.GetRequiredService<IHttpClientFactory>(),
                HttpClientName,
                sp.GetRequiredService<IOptionsMonitor<VodafoneSmsOptions>>(),
                optionsName: null,
                sp.GetRequiredService<ILogger<VodafoneSmsSender>>()
            ));
            services.AddSingleton<IBulkSmsSender>(static sp => (IBulkSmsSender)sp.GetRequiredService<ISmsSender>());

            return;
        }

        services.AddKeyedSingleton<ISmsSender>(
            name,
            (sp, _) =>
                new VodafoneSmsSender(
                    sp.GetRequiredService<IHttpClientFactory>(),
                    httpClientName,
                    sp.GetRequiredService<IOptionsMonitor<VodafoneSmsOptions>>(),
                    name,
                    sp.GetRequiredService<ILogger<VodafoneSmsSender>>()
                )
        );
        services.AddKeyedSingleton<IBulkSmsSender>(
            name,
            (sp, _) => (IBulkSmsSender)sp.GetRequiredKeyedService<ISmsSender>(name)
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
