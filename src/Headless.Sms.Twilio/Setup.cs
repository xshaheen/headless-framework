// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Sms.Twilio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Twilio.Clients;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Sms;

/// <summary>
/// Extension members for selecting Twilio as the default (unkeyed) SMS provider on
/// <see cref="HeadlessSmsSetupBuilder"/>. Named instances are configured through
/// <see cref="SetupTwilioNamed"/>.
/// </summary>
[PublicAPI]
public static class SetupTwilio
{
    internal const string HttpClientName = "Headless:TwilioSms";

    extension(HeadlessSmsSetupBuilder setup)
    {
        /// <summary>Selects Twilio, binding and validating <see cref="TwilioSmsOptions"/> from configuration.</summary>
        /// <remarks>
        /// HTTP retry is disabled by default because SMS sends are not idempotent. Pass
        /// <paramref name="configureResilience"/> to opt back in.
        /// </remarks>
        /// <param name="config">Configuration section containing <see cref="TwilioSmsOptions"/> values.</param>
        /// <param name="configureClient">Optional delegate to further configure the underlying <see cref="HttpClient"/>.</param>
        /// <param name="configureResilience">Optional delegate to override the default resilience pipeline.</param>
        /// <returns>The same builder, for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="config"/> is <see langword="null"/>.</exception>
        public HeadlessSmsSetupBuilder UseTwilio(
            IConfiguration config,
            Action<HttpClient>? configureClient = null,
            Action<HttpStandardResilienceOptions>? configureResilience = null
        )
        {
            Argument.IsNotNull(config);

            setup.RegisterDefaultProvider(services =>
                AddTwilioSmsCore(
                    services,
                    name: null,
                    (s, n) => s.Configure<TwilioSmsOptions, TwilioSmsOptionsValidator>(config, n),
                    configureClient,
                    configureResilience
                )
            );

            return setup;
        }

        /// <summary>Selects Twilio, configuring <see cref="TwilioSmsOptions"/> via a delegate.</summary>
        /// <param name="setupAction">Delegate that populates the options.</param>
        /// <param name="configureClient">Optional delegate to further configure the underlying <see cref="HttpClient"/>.</param>
        /// <param name="configureResilience">Optional delegate to override the default resilience pipeline.</param>
        /// <returns>The same builder, for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="setupAction"/> is <see langword="null"/>.</exception>
        public HeadlessSmsSetupBuilder UseTwilio(
            Action<TwilioSmsOptions> setupAction,
            Action<HttpClient>? configureClient = null,
            Action<HttpStandardResilienceOptions>? configureResilience = null
        )
        {
            Argument.IsNotNull(setupAction);

            setup.RegisterDefaultProvider(services =>
                AddTwilioSmsCore(
                    services,
                    name: null,
                    (s, n) => s.Configure<TwilioSmsOptions, TwilioSmsOptionsValidator>(setupAction, n),
                    configureClient,
                    configureResilience
                )
            );

            return setup;
        }

        /// <summary>Selects Twilio, configuring <see cref="TwilioSmsOptions"/> with access to the service provider.</summary>
        /// <param name="setupAction">Delegate that populates the options, with access to the resolved service provider.</param>
        /// <param name="configureClient">Optional delegate to further configure the underlying <see cref="HttpClient"/>.</param>
        /// <param name="configureResilience">Optional delegate to override the default resilience pipeline.</param>
        /// <returns>The same builder, for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="setupAction"/> is <see langword="null"/>.</exception>
        public HeadlessSmsSetupBuilder UseTwilio(
            Action<TwilioSmsOptions, IServiceProvider> setupAction,
            Action<HttpClient>? configureClient = null,
            Action<HttpStandardResilienceOptions>? configureResilience = null
        )
        {
            Argument.IsNotNull(setupAction);

            setup.RegisterDefaultProvider(services =>
                AddTwilioSmsCore(
                    services,
                    name: null,
                    (s, n) => s.Configure<TwilioSmsOptions, TwilioSmsOptionsValidator>(setupAction, n),
                    configureClient,
                    configureResilience
                )
            );

            return setup;
        }
    }

    /// <summary>
    /// The Twilio HttpClient name doubles as the resilience-pipeline key, so each named instance gets its
    /// own client registration (and pipeline) suffixed with the instance name.
    /// </summary>
    internal static string GetHttpClientName(string? name)
    {
        return name is null ? HttpClientName : $"{HttpClientName}:{name}";
    }

    /// <summary>
    /// Registers the Twilio SMS sender. <paramref name="name"/> <see langword="null"/> registers the default
    /// (unkeyed) sender and an overridable <see cref="ITwilioRestClient"/> (<c>TryAddSingleton</c>, so a
    /// host-supplied client wins); a non-null name registers a keyed sender, a keyed
    /// <see cref="ITwilioRestClient"/> built from that name's options and per-name HttpClient, named options,
    /// and a per-name HttpClient with its own resilience pipeline. Every factory reads the options snapshot
    /// for its own name (<c>IOptionsMonitor.Get(name)</c>) so keyed settings never bleed across instances —
    /// keyed DI does not cascade the key to ctor dependencies, and a keyed sender must not read
    /// <c>CurrentValue</c> (which binds the default). Twilio sends to one recipient per API call, so no
    /// <see cref="IBulkSmsSender"/> forward is registered.
    /// </summary>
    internal static void AddTwilioSmsCore(
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
            services.TryAddSingleton<ITwilioRestClient>(static sp =>
                _CreateTwilioClient(sp, HttpClientName, optionsName: null)
            );

            services.AddSingleton<ISmsSender>(static sp => new TwilioSmsSender(
                sp.GetRequiredService<ITwilioRestClient>(),
                sp.GetRequiredService<IOptionsMonitor<TwilioSmsOptions>>(),
                optionsName: null,
                sp.GetRequiredService<ILogger<TwilioSmsSender>>()
            ));

            return;
        }

        services.AddKeyedSingleton<ITwilioRestClient>(name, (sp, _) => _CreateTwilioClient(sp, httpClientName, name));

        services.AddKeyedSingleton<ISmsSender>(
            name,
            (sp, _) =>
                new TwilioSmsSender(
                    sp.GetRequiredKeyedService<ITwilioRestClient>(name),
                    sp.GetRequiredService<IOptionsMonitor<TwilioSmsOptions>>(),
                    name,
                    sp.GetRequiredService<ILogger<TwilioSmsSender>>()
                )
        );
    }

    private static TwilioRestClient _CreateTwilioClient(
        IServiceProvider serviceProvider,
        string httpClientName,
        string? optionsName
    )
    {
        var options = serviceProvider.GetRequiredService<IOptionsMonitor<TwilioSmsOptions>>().Get(optionsName);
        var httpClient = serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient(httpClientName);

        return new TwilioRestClient(
            username: options.Sid,
            password: options.AuthToken,
            accountSid: options.Sid,
            region: options.Region,
            httpClient: new global::Twilio.Http.SystemNetHttpClient(httpClient),
            edge: options.Edge
        );
    }
}

/// <summary>
/// Extension members for selecting Twilio for a named SMS instance on
/// <see cref="HeadlessSmsInstanceBuilder"/>. The instance owns its own named options, HttpClient (and
/// resilience pipeline), keyed <see cref="ITwilioRestClient"/>, and keyed sender; it never shares them with
/// the default sender or other named instances.
/// </summary>
[PublicAPI]
public static class SetupTwilioNamed
{
    extension(HeadlessSmsInstanceBuilder instance)
    {
        /// <summary>Uses Twilio for this named instance, binding and validating <see cref="TwilioSmsOptions"/> from configuration.</summary>
        /// <remarks>
        /// HTTP retry is disabled by default because SMS sends are not idempotent. Pass
        /// <paramref name="configureResilience"/> to opt back in.
        /// </remarks>
        /// <param name="config">Configuration section containing <see cref="TwilioSmsOptions"/> values.</param>
        /// <param name="configureClient">Optional delegate to further configure the underlying <see cref="HttpClient"/>.</param>
        /// <param name="configureResilience">Optional delegate to override the default resilience pipeline.</param>
        /// <returns>The instance builder, for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="config"/> is <see langword="null"/>.</exception>
        public HeadlessSmsInstanceBuilder UseTwilio(
            IConfiguration config,
            Action<HttpClient>? configureClient = null,
            Action<HttpStandardResilienceOptions>? configureResilience = null
        )
        {
            Argument.IsNotNull(config);

            var name = instance.Name;

            instance.RegisterProvider(services =>
                SetupTwilio.AddTwilioSmsCore(
                    services,
                    name,
                    (s, n) => s.Configure<TwilioSmsOptions, TwilioSmsOptionsValidator>(config, n),
                    configureClient,
                    configureResilience
                )
            );

            return instance;
        }

        /// <summary>Uses Twilio for this named instance, configuring <see cref="TwilioSmsOptions"/> via a delegate.</summary>
        /// <param name="setupAction">Delegate that populates the options.</param>
        /// <param name="configureClient">Optional delegate to further configure the underlying <see cref="HttpClient"/>.</param>
        /// <param name="configureResilience">Optional delegate to override the default resilience pipeline.</param>
        /// <returns>The instance builder, for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="setupAction"/> is <see langword="null"/>.</exception>
        public HeadlessSmsInstanceBuilder UseTwilio(
            Action<TwilioSmsOptions> setupAction,
            Action<HttpClient>? configureClient = null,
            Action<HttpStandardResilienceOptions>? configureResilience = null
        )
        {
            Argument.IsNotNull(setupAction);

            var name = instance.Name;

            instance.RegisterProvider(services =>
                SetupTwilio.AddTwilioSmsCore(
                    services,
                    name,
                    (s, n) => s.Configure<TwilioSmsOptions, TwilioSmsOptionsValidator>(setupAction, n),
                    configureClient,
                    configureResilience
                )
            );

            return instance;
        }

        /// <summary>Uses Twilio for this named instance, configuring <see cref="TwilioSmsOptions"/> with access to the service provider.</summary>
        /// <param name="setupAction">Delegate that populates the options, with access to the resolved service provider.</param>
        /// <param name="configureClient">Optional delegate to further configure the underlying <see cref="HttpClient"/>.</param>
        /// <param name="configureResilience">Optional delegate to override the default resilience pipeline.</param>
        /// <returns>The instance builder, for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="setupAction"/> is <see langword="null"/>.</exception>
        public HeadlessSmsInstanceBuilder UseTwilio(
            Action<TwilioSmsOptions, IServiceProvider> setupAction,
            Action<HttpClient>? configureClient = null,
            Action<HttpStandardResilienceOptions>? configureResilience = null
        )
        {
            Argument.IsNotNull(setupAction);

            var name = instance.Name;

            instance.RegisterProvider(services =>
                SetupTwilio.AddTwilioSmsCore(
                    services,
                    name,
                    (s, n) => s.Configure<TwilioSmsOptions, TwilioSmsOptionsValidator>(setupAction, n),
                    configureClient,
                    configureResilience
                )
            );

            return instance;
        }
    }
}
