// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;

namespace Headless.Sms.Cequens;

/// <summary>
/// Extension members for selecting Cequens as the default (unkeyed) SMS provider on
/// <see cref="HeadlessSmsSetupBuilder"/>. Named instances are configured through
/// <see cref="SetupCequensNamed"/>.
/// </summary>
[PublicAPI]
public static class SetupCequens
{
    internal const string HttpClientName = "Headless:CequensSms";

    extension(HeadlessSmsSetupBuilder setup)
    {
        /// <summary>Selects Cequens, binding and validating <see cref="CequensSmsOptions"/> from configuration.</summary>
        /// <remarks>
        /// HTTP retry is disabled by default because SMS sends are not idempotent. Pass
        /// <paramref name="configureResilience"/> to opt back in (ideally after verifying the provider
        /// supports an idempotency key).
        /// </remarks>
        /// <param name="config">Configuration section containing <see cref="CequensSmsOptions"/> values.</param>
        /// <param name="configureClient">Optional delegate to further configure the underlying <see cref="HttpClient"/>.</param>
        /// <param name="configureResilience">Optional delegate to override the default resilience pipeline.</param>
        /// <returns>The same builder, for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="config"/> is <see langword="null"/>.</exception>
        public HeadlessSmsSetupBuilder UseCequens(
            IConfiguration config,
            Action<HttpClient>? configureClient = null,
            Action<HttpStandardResilienceOptions>? configureResilience = null
        )
        {
            Argument.IsNotNull(config);

            setup.RegisterDefaultProvider(services =>
                AddCequensSmsCore(
                    services,
                    name: null,
                    (s, n) => s.Configure<CequensSmsOptions, CequensSmsOptionsValidator>(config, n),
                    configureClient,
                    configureResilience
                )
            );

            return setup;
        }

        /// <summary>Selects Cequens, configuring <see cref="CequensSmsOptions"/> via a delegate.</summary>
        /// <param name="setupAction">Delegate that populates the options.</param>
        /// <param name="configureClient">Optional delegate to further configure the underlying <see cref="HttpClient"/>.</param>
        /// <param name="configureResilience">Optional delegate to override the default resilience pipeline.</param>
        /// <returns>The same builder, for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="setupAction"/> is <see langword="null"/>.</exception>
        public HeadlessSmsSetupBuilder UseCequens(
            Action<CequensSmsOptions> setupAction,
            Action<HttpClient>? configureClient = null,
            Action<HttpStandardResilienceOptions>? configureResilience = null
        )
        {
            Argument.IsNotNull(setupAction);

            setup.RegisterDefaultProvider(services =>
                AddCequensSmsCore(
                    services,
                    name: null,
                    (s, n) => s.Configure<CequensSmsOptions, CequensSmsOptionsValidator>(setupAction, n),
                    configureClient,
                    configureResilience
                )
            );

            return setup;
        }

        /// <summary>Selects Cequens, configuring <see cref="CequensSmsOptions"/> with access to the service provider.</summary>
        /// <param name="setupAction">Delegate that populates the options, with access to the resolved service provider.</param>
        /// <param name="configureClient">Optional delegate to further configure the underlying <see cref="HttpClient"/>.</param>
        /// <param name="configureResilience">Optional delegate to override the default resilience pipeline.</param>
        /// <returns>The same builder, for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="setupAction"/> is <see langword="null"/>.</exception>
        public HeadlessSmsSetupBuilder UseCequens(
            Action<CequensSmsOptions, IServiceProvider> setupAction,
            Action<HttpClient>? configureClient = null,
            Action<HttpStandardResilienceOptions>? configureResilience = null
        )
        {
            Argument.IsNotNull(setupAction);

            setup.RegisterDefaultProvider(services =>
                AddCequensSmsCore(
                    services,
                    name: null,
                    (s, n) => s.Configure<CequensSmsOptions, CequensSmsOptionsValidator>(setupAction, n),
                    configureClient,
                    configureResilience
                )
            );

            return setup;
        }
    }

    /// <summary>
    /// The Cequens HttpClient name doubles as the resilience-pipeline key, so each named instance gets its
    /// own client registration (and pipeline) suffixed with the instance name.
    /// </summary>
    internal static string GetHttpClientName(string? name)
    {
        return name is null ? HttpClientName : $"{HttpClientName}:{name}";
    }

    /// <summary>
    /// Registers the Cequens SMS sender. <paramref name="name"/> <see langword="null"/> registers the default
    /// (unkeyed) sender; a non-null name registers a keyed sender (plus keyed bulk forward), named options,
    /// and a per-name HttpClient with its own resilience pipeline. Every factory reads the options snapshot
    /// for its own name (<c>IOptionsMonitor.Get(name)</c>) so keyed settings never bleed across instances —
    /// keyed DI does not cascade the key to ctor dependencies, and a keyed sender must not read
    /// <c>CurrentValue</c> (which binds the default). The sender's token cache and semaphore are instance
    /// fields, so per-name state isolation falls out of one-keyed-singleton-per-name; the container owns
    /// disposal of each keyed sender.
    /// </summary>
    internal static void AddCequensSmsCore(
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
            services.AddSingleton<ISmsSender>(static sp => new CequensSmsSender(
                sp.GetRequiredService<IHttpClientFactory>(),
                HttpClientName,
                sp.GetRequiredService<TimeProvider>(),
                sp.GetRequiredService<IOptionsMonitor<CequensSmsOptions>>(),
                optionsName: null,
                sp.GetRequiredService<ILogger<CequensSmsSender>>()
            ));
            services.AddSingleton<IBulkSmsSender>(static sp => (IBulkSmsSender)sp.GetRequiredService<ISmsSender>());

            return;
        }

        services.AddKeyedSingleton<ISmsSender>(
            name,
            (sp, _) =>
                new CequensSmsSender(
                    sp.GetRequiredService<IHttpClientFactory>(),
                    httpClientName,
                    sp.GetRequiredService<TimeProvider>(),
                    sp.GetRequiredService<IOptionsMonitor<CequensSmsOptions>>(),
                    name,
                    sp.GetRequiredService<ILogger<CequensSmsSender>>()
                )
        );
        services.AddKeyedSingleton<IBulkSmsSender>(
            name,
            (sp, _) => (IBulkSmsSender)sp.GetRequiredKeyedService<ISmsSender>(name)
        );
    }
}

/// <summary>
/// Extension members for selecting Cequens for a named SMS instance on
/// <see cref="HeadlessSmsInstanceBuilder"/>. The instance owns its own named options, HttpClient (and
/// resilience pipeline), token cache, and keyed sender; it never shares them with the default sender or
/// other named instances.
/// </summary>
[PublicAPI]
public static class SetupCequensNamed
{
    extension(HeadlessSmsInstanceBuilder instance)
    {
        /// <summary>Uses Cequens for this named instance, binding and validating <see cref="CequensSmsOptions"/> from configuration.</summary>
        /// <remarks>
        /// HTTP retry is disabled by default because SMS sends are not idempotent. Pass
        /// <paramref name="configureResilience"/> to opt back in (ideally after verifying the provider
        /// supports an idempotency key).
        /// </remarks>
        /// <param name="config">Configuration section containing <see cref="CequensSmsOptions"/> values.</param>
        /// <param name="configureClient">Optional delegate to further configure the underlying <see cref="HttpClient"/>.</param>
        /// <param name="configureResilience">Optional delegate to override the default resilience pipeline.</param>
        /// <returns>The instance builder, for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="config"/> is <see langword="null"/>.</exception>
        public HeadlessSmsInstanceBuilder UseCequens(
            IConfiguration config,
            Action<HttpClient>? configureClient = null,
            Action<HttpStandardResilienceOptions>? configureResilience = null
        )
        {
            Argument.IsNotNull(config);

            var name = instance.Name;

            instance.RegisterProvider(services =>
                SetupCequens.AddCequensSmsCore(
                    services,
                    name,
                    (s, n) => s.Configure<CequensSmsOptions, CequensSmsOptionsValidator>(config, n),
                    configureClient,
                    configureResilience
                )
            );

            return instance;
        }

        /// <summary>Uses Cequens for this named instance, configuring <see cref="CequensSmsOptions"/> via a delegate.</summary>
        /// <param name="setupAction">Delegate that populates the options.</param>
        /// <param name="configureClient">Optional delegate to further configure the underlying <see cref="HttpClient"/>.</param>
        /// <param name="configureResilience">Optional delegate to override the default resilience pipeline.</param>
        /// <returns>The instance builder, for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="setupAction"/> is <see langword="null"/>.</exception>
        public HeadlessSmsInstanceBuilder UseCequens(
            Action<CequensSmsOptions> setupAction,
            Action<HttpClient>? configureClient = null,
            Action<HttpStandardResilienceOptions>? configureResilience = null
        )
        {
            Argument.IsNotNull(setupAction);

            var name = instance.Name;

            instance.RegisterProvider(services =>
                SetupCequens.AddCequensSmsCore(
                    services,
                    name,
                    (s, n) => s.Configure<CequensSmsOptions, CequensSmsOptionsValidator>(setupAction, n),
                    configureClient,
                    configureResilience
                )
            );

            return instance;
        }

        /// <summary>Uses Cequens for this named instance, configuring <see cref="CequensSmsOptions"/> with access to the service provider.</summary>
        /// <param name="setupAction">Delegate that populates the options, with access to the resolved service provider.</param>
        /// <param name="configureClient">Optional delegate to further configure the underlying <see cref="HttpClient"/>.</param>
        /// <param name="configureResilience">Optional delegate to override the default resilience pipeline.</param>
        /// <returns>The instance builder, for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="setupAction"/> is <see langword="null"/>.</exception>
        public HeadlessSmsInstanceBuilder UseCequens(
            Action<CequensSmsOptions, IServiceProvider> setupAction,
            Action<HttpClient>? configureClient = null,
            Action<HttpStandardResilienceOptions>? configureResilience = null
        )
        {
            Argument.IsNotNull(setupAction);

            var name = instance.Name;

            instance.RegisterProvider(services =>
                SetupCequens.AddCequensSmsCore(
                    services,
                    name,
                    (s, n) => s.Configure<CequensSmsOptions, CequensSmsOptionsValidator>(setupAction, n),
                    configureClient,
                    configureResilience
                )
            );

            return instance;
        }
    }
}
