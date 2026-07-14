// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Sms.Connekio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Sms;

/// <summary>
/// Extension members for selecting Connekio as the default (unkeyed) SMS provider on
/// <see cref="HeadlessSmsSetupBuilder"/>. Named instances are configured through
/// <see cref="SetupConnekioNamed"/>.
/// </summary>
[PublicAPI]
public static class SetupConnekio
{
    internal const string HttpClientName = "Headless:ConnekioSms";

    extension(HeadlessSmsSetupBuilder setup)
    {
        /// <summary>Selects Connekio, binding and validating <see cref="ConnekioSmsOptions"/> from configuration.</summary>
        /// <remarks>
        /// HTTP retry is disabled by default because SMS sends are not idempotent. Pass
        /// <paramref name="configureResilience"/> to opt back in.
        /// </remarks>
        /// <param name="config">Configuration section containing <see cref="ConnekioSmsOptions"/> values.</param>
        /// <param name="configureClient">Optional delegate to further configure the underlying <see cref="HttpClient"/>.</param>
        /// <param name="configureResilience">Optional delegate to override the default resilience pipeline.</param>
        /// <returns>The same builder, for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="config"/> is <see langword="null"/>.</exception>
        public HeadlessSmsSetupBuilder UseConnekio(
            IConfiguration config,
            Action<HttpClient>? configureClient = null,
            Action<HttpStandardResilienceOptions>? configureResilience = null
        )
        {
            Argument.IsNotNull(config);

            setup.RegisterDefaultProvider(services =>
                AddConnekioSmsCore(
                    services,
                    name: null,
                    (s, n) => s.Configure<ConnekioSmsOptions, ConnekioSmsOptionsValidator>(config, n),
                    configureClient,
                    configureResilience
                )
            );

            return setup;
        }

        /// <summary>Selects Connekio, configuring <see cref="ConnekioSmsOptions"/> via a delegate.</summary>
        /// <param name="setupAction">Delegate that populates the options.</param>
        /// <param name="configureClient">Optional delegate to further configure the underlying <see cref="HttpClient"/>.</param>
        /// <param name="configureResilience">Optional delegate to override the default resilience pipeline.</param>
        /// <returns>The same builder, for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="setupAction"/> is <see langword="null"/>.</exception>
        public HeadlessSmsSetupBuilder UseConnekio(
            Action<ConnekioSmsOptions> setupAction,
            Action<HttpClient>? configureClient = null,
            Action<HttpStandardResilienceOptions>? configureResilience = null
        )
        {
            Argument.IsNotNull(setupAction);

            setup.RegisterDefaultProvider(services =>
                AddConnekioSmsCore(
                    services,
                    name: null,
                    (s, n) => s.Configure<ConnekioSmsOptions, ConnekioSmsOptionsValidator>(setupAction, n),
                    configureClient,
                    configureResilience
                )
            );

            return setup;
        }

        /// <summary>Selects Connekio, configuring <see cref="ConnekioSmsOptions"/> with access to the service provider.</summary>
        /// <param name="setupAction">Delegate that populates the options, with access to the resolved service provider.</param>
        /// <param name="configureClient">Optional delegate to further configure the underlying <see cref="HttpClient"/>.</param>
        /// <param name="configureResilience">Optional delegate to override the default resilience pipeline.</param>
        /// <returns>The same builder, for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="setupAction"/> is <see langword="null"/>.</exception>
        public HeadlessSmsSetupBuilder UseConnekio(
            Action<ConnekioSmsOptions, IServiceProvider> setupAction,
            Action<HttpClient>? configureClient = null,
            Action<HttpStandardResilienceOptions>? configureResilience = null
        )
        {
            Argument.IsNotNull(setupAction);

            setup.RegisterDefaultProvider(services =>
                AddConnekioSmsCore(
                    services,
                    name: null,
                    (s, n) => s.Configure<ConnekioSmsOptions, ConnekioSmsOptionsValidator>(setupAction, n),
                    configureClient,
                    configureResilience
                )
            );

            return setup;
        }
    }

    /// <summary>
    /// The Connekio HttpClient name doubles as the resilience-pipeline key, so each named instance gets its
    /// own client registration (and pipeline) suffixed with the instance name.
    /// </summary>
    internal static string GetHttpClientName(string? name)
    {
        return name is null ? HttpClientName : $"{HttpClientName}:{name}";
    }

    /// <summary>
    /// Registers the Connekio SMS sender. <paramref name="name"/> <see langword="null"/> registers the default
    /// (unkeyed) sender; a non-null name registers a keyed sender (plus keyed bulk forward), named options,
    /// and a per-name HttpClient with its own resilience pipeline. Every factory reads the options snapshot
    /// for its own name (<c>IOptionsMonitor.Get(name)</c>) so keyed settings never bleed across instances —
    /// keyed DI does not cascade the key to ctor dependencies, and a keyed sender must not read
    /// <c>CurrentValue</c> (which binds the default).
    /// </summary>
    internal static void AddConnekioSmsCore(
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
            services.AddSingleton<ISmsSender>(static sp => new ConnekioSmsSender(
                sp.GetRequiredService<IHttpClientFactory>(),
                HttpClientName,
                sp.GetRequiredService<IOptionsMonitor<ConnekioSmsOptions>>(),
                optionsName: null,
                sp.GetRequiredService<ILogger<ConnekioSmsSender>>()
            ));
            services.AddSingleton<IBulkSmsSender>(static sp => (IBulkSmsSender)sp.GetRequiredService<ISmsSender>());

            return;
        }

        services.AddKeyedSingleton<ISmsSender>(
            name,
            (sp, _) =>
                new ConnekioSmsSender(
                    sp.GetRequiredService<IHttpClientFactory>(),
                    httpClientName,
                    sp.GetRequiredService<IOptionsMonitor<ConnekioSmsOptions>>(),
                    name,
                    sp.GetRequiredService<ILogger<ConnekioSmsSender>>()
                )
        );
        services.AddKeyedSingleton<IBulkSmsSender>(
            name,
            (sp, _) => (IBulkSmsSender)sp.GetRequiredKeyedService<ISmsSender>(name)
        );
    }
}

/// <summary>
/// Extension members for selecting Connekio for a named SMS instance on
/// <see cref="HeadlessSmsInstanceBuilder"/>. The instance owns its own named options, HttpClient (and
/// resilience pipeline), and keyed sender; it never shares them with the default sender or other named
/// instances.
/// </summary>
[PublicAPI]
public static class SetupConnekioNamed
{
    extension(HeadlessSmsInstanceBuilder instance)
    {
        /// <summary>Uses Connekio for this named instance, binding and validating <see cref="ConnekioSmsOptions"/> from configuration.</summary>
        /// <remarks>
        /// HTTP retry is disabled by default because SMS sends are not idempotent. Pass
        /// <paramref name="configureResilience"/> to opt back in (ideally after verifying the provider
        /// supports an idempotency key).
        /// </remarks>
        /// <param name="config">Configuration section containing <see cref="ConnekioSmsOptions"/> values.</param>
        /// <param name="configureClient">Optional delegate to further configure the underlying <see cref="HttpClient"/>.</param>
        /// <param name="configureResilience">Optional delegate to override the default resilience pipeline.</param>
        /// <returns>The instance builder, for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="config"/> is <see langword="null"/>.</exception>
        public HeadlessSmsInstanceBuilder UseConnekio(
            IConfiguration config,
            Action<HttpClient>? configureClient = null,
            Action<HttpStandardResilienceOptions>? configureResilience = null
        )
        {
            Argument.IsNotNull(config);

            var name = instance.Name;

            instance.RegisterProvider(services =>
                SetupConnekio.AddConnekioSmsCore(
                    services,
                    name,
                    (s, n) => s.Configure<ConnekioSmsOptions, ConnekioSmsOptionsValidator>(config, n),
                    configureClient,
                    configureResilience
                )
            );

            return instance;
        }

        /// <summary>Uses Connekio for this named instance, configuring <see cref="ConnekioSmsOptions"/> via a delegate.</summary>
        /// <param name="setupAction">Delegate that populates the options.</param>
        /// <param name="configureClient">Optional delegate to further configure the underlying <see cref="HttpClient"/>.</param>
        /// <param name="configureResilience">Optional delegate to override the default resilience pipeline.</param>
        /// <returns>The instance builder, for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="setupAction"/> is <see langword="null"/>.</exception>
        public HeadlessSmsInstanceBuilder UseConnekio(
            Action<ConnekioSmsOptions> setupAction,
            Action<HttpClient>? configureClient = null,
            Action<HttpStandardResilienceOptions>? configureResilience = null
        )
        {
            Argument.IsNotNull(setupAction);

            var name = instance.Name;

            instance.RegisterProvider(services =>
                SetupConnekio.AddConnekioSmsCore(
                    services,
                    name,
                    (s, n) => s.Configure<ConnekioSmsOptions, ConnekioSmsOptionsValidator>(setupAction, n),
                    configureClient,
                    configureResilience
                )
            );

            return instance;
        }

        /// <summary>Uses Connekio for this named instance, configuring <see cref="ConnekioSmsOptions"/> with access to the service provider.</summary>
        /// <param name="setupAction">Delegate that populates the options, with access to the resolved service provider.</param>
        /// <param name="configureClient">Optional delegate to further configure the underlying <see cref="HttpClient"/>.</param>
        /// <param name="configureResilience">Optional delegate to override the default resilience pipeline.</param>
        /// <returns>The instance builder, for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="setupAction"/> is <see langword="null"/>.</exception>
        public HeadlessSmsInstanceBuilder UseConnekio(
            Action<ConnekioSmsOptions, IServiceProvider> setupAction,
            Action<HttpClient>? configureClient = null,
            Action<HttpStandardResilienceOptions>? configureResilience = null
        )
        {
            Argument.IsNotNull(setupAction);

            var name = instance.Name;

            instance.RegisterProvider(services =>
                SetupConnekio.AddConnekioSmsCore(
                    services,
                    name,
                    (s, n) => s.Configure<ConnekioSmsOptions, ConnekioSmsOptionsValidator>(setupAction, n),
                    configureClient,
                    configureResilience
                )
            );

            return instance;
        }
    }
}
