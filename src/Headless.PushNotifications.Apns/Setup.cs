// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Checks;
using Headless.PushNotifications.Apns;
using Headless.PushNotifications.Apns.Internals;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Timeout;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.PushNotifications;

/// <summary>
/// Extension members for selecting Apple Push Notification service (APNs) as the default (unkeyed)
/// push-notification provider on <see cref="HeadlessPushNotificationsSetupBuilder"/>. Named instances are
/// configured through <see cref="SetupApnsPushNotificationsNamed"/>.
/// </summary>
/// <remarks>
/// <para>
/// Each instance owns its options and its own HTTP/2 client. Instances that share a team id and key id share one
/// cached provider token, because Apple rejects a key whose tokens change more than once every 20 minutes.
/// </para>
/// <para>
/// The options choose the authentication mode when the service is first resolved. A certificate-mode instance
/// presents its provider certificate during the TLS handshake, picks up a renewed certificate on new connections
/// when its options reload, and is checked for expiry at host start and daily after that.
/// </para>
/// <para>
/// The default resilience pipeline retries, at most twice, only connection failures that happen before a request
/// is sent. It never retries an HTTP 5xx, because Apple asks senders to wait about 15 minutes before retrying one,
/// and never retries HTTP 429, which throttles a single device token. HTTP 500 and 503 still count toward the
/// circuit breaker. Pass <c>configureResilience</c> to change it.
/// </para>
/// </remarks>
[PublicAPI]
public static class SetupApnsPushNotifications
{
    internal const string HttpClientName = "Headless:Apns";

    // Apple serves the same hosts on 443 and on 2197 for networks that block 443 to non-web endpoints.
    internal static readonly Uri ProductionAddress = new("https://api.push.apple.com");
    internal static readonly Uri SandboxAddress = new("https://api.sandbox.push.apple.com");
    internal const int AlternativePort = 2197;
    internal const int DefaultPort = 443;

    /// <summary>The endpoint the environment and port settings select.</summary>
    internal static Uri GetBaseAddress(ApnsOptions options)
    {
        // A UriBuilder port must be reset to -1 to mean "scheme default", so the flag decides it once here.
        var address = options.Environment == ApnsEnvironment.Sandbox ? SandboxAddress : ProductionAddress;

        if (!options.UseAlternativePort)
        {
            return address;
        }

        var builder = new UriBuilder(address) { Port = AlternativePort };

        return builder.Uri;
    }

    extension(HeadlessPushNotificationsSetupBuilder setup)
    {
        /// <summary>Selects APNs, binding and validating <see cref="ApnsOptions"/> from configuration.</summary>
        /// <param name="configuration">Configuration section containing <see cref="ApnsOptions"/> values.</param>
        /// <param name="configureClient">
        /// Optional delegate to further configure the underlying <see cref="HttpClient"/>. It runs after the
        /// environment's <see cref="HttpClient.BaseAddress"/> is set, so it may replace it.
        /// </param>
        /// <param name="configureResilience">Optional delegate to override the default resilience pipeline.</param>
        /// <returns>The same builder, for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is <see langword="null"/>.</exception>
        public HeadlessPushNotificationsSetupBuilder UseApns(
            IConfiguration configuration,
            Action<HttpClient>? configureClient = null,
            Action<HttpStandardResilienceOptions>? configureResilience = null
        )
        {
            Argument.IsNotNull(configuration);

            setup.RegisterDefaultProvider(services =>
                AddApnsCore(
                    services,
                    name: null,
                    (s, n) => s.Configure<ApnsOptions, ApnsOptionsValidator>(configuration, n),
                    configureClient,
                    configureResilience
                )
            );

            return setup;
        }

        /// <summary>Selects APNs, configuring <see cref="ApnsOptions"/> via a delegate.</summary>
        /// <param name="configure">Delegate that populates the options.</param>
        /// <param name="configureClient">
        /// Optional delegate to further configure the underlying <see cref="HttpClient"/>. It runs after the
        /// environment's <see cref="HttpClient.BaseAddress"/> is set, so it may replace it.
        /// </param>
        /// <param name="configureResilience">Optional delegate to override the default resilience pipeline.</param>
        /// <returns>The same builder, for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
        public HeadlessPushNotificationsSetupBuilder UseApns(
            Action<ApnsOptions> configure,
            Action<HttpClient>? configureClient = null,
            Action<HttpStandardResilienceOptions>? configureResilience = null
        )
        {
            Argument.IsNotNull(configure);

            setup.RegisterDefaultProvider(services =>
                AddApnsCore(
                    services,
                    name: null,
                    (s, n) => s.Configure<ApnsOptions, ApnsOptionsValidator>(configure, n),
                    configureClient,
                    configureResilience
                )
            );

            return setup;
        }

        /// <summary>Selects APNs, configuring <see cref="ApnsOptions"/> with access to the service provider.</summary>
        /// <param name="configure">Delegate that populates the options, with access to the resolved service provider.</param>
        /// <param name="configureClient">
        /// Optional delegate to further configure the underlying <see cref="HttpClient"/>. It runs after the
        /// environment's <see cref="HttpClient.BaseAddress"/> is set, so it may replace it.
        /// </param>
        /// <param name="configureResilience">Optional delegate to override the default resilience pipeline.</param>
        /// <returns>The same builder, for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
        public HeadlessPushNotificationsSetupBuilder UseApns(
            Action<ApnsOptions, IServiceProvider> configure,
            Action<HttpClient>? configureClient = null,
            Action<HttpStandardResilienceOptions>? configureResilience = null
        )
        {
            Argument.IsNotNull(configure);

            setup.RegisterDefaultProvider(services =>
                AddApnsCore(
                    services,
                    name: null,
                    (s, n) => s.Configure<ApnsOptions, ApnsOptionsValidator>(configure, n),
                    configureClient,
                    configureResilience
                )
            );

            return setup;
        }

        /// <summary>Selects APNs from a pre-built options instance (validated at startup).</summary>
        /// <param name="options">The options to copy.</param>
        /// <param name="configureClient">
        /// Optional delegate to further configure the underlying <see cref="HttpClient"/>. It runs after the
        /// environment's <see cref="HttpClient.BaseAddress"/> is set, so it may replace it.
        /// </param>
        /// <param name="configureResilience">Optional delegate to override the default resilience pipeline.</param>
        /// <returns>The same builder, for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
        public HeadlessPushNotificationsSetupBuilder UseApns(
            ApnsOptions options,
            Action<HttpClient>? configureClient = null,
            Action<HttpStandardResilienceOptions>? configureResilience = null
        )
        {
            Argument.IsNotNull(options);

            setup.RegisterDefaultProvider(services =>
                AddApnsCore(services, name: null, CopyOptions(options), configureClient, configureResilience)
            );

            return setup;
        }
    }

    /// <summary>
    /// The client name doubles as the resilience-pipeline key, so each named instance gets its own client and
    /// pipeline, suffixed with the instance name.
    /// </summary>
    internal static string GetHttpClientName(string? name)
    {
        return name is null ? HttpClientName : $"{HttpClientName}:{name}";
    }

    /// <summary>
    /// Registers the APNs push-notification service. <paramref name="name"/> <see langword="null"/> registers the
    /// default (unkeyed) service; a non-null name registers a keyed service. Every instance reads the options for
    /// its own name (<c>IOptionsMonitor.Get(name)</c>), because keyed DI does not pass the key to constructor
    /// dependencies and <c>CurrentValue</c> would bind the default instance's options.
    /// </summary>
    /// <param name="configurePrimaryHandler">
    /// Runs last on the primary handler. Tests use it to trust a self-signed server certificate; no public overload
    /// exposes it, so production certificate validation always stays on.
    /// </param>
    internal static void AddApnsCore(
        IServiceCollection services,
        string? name,
        Action<IServiceCollection, string?> configureOptions,
        Action<HttpClient>? configureClient,
        Action<HttpStandardResilienceOptions>? configureResilience,
        Action<SocketsHttpHandler>? configurePrimaryHandler = null
    )
    {
        configureOptions(services, name);
        services.TryAddSingleton(TimeProvider.System);

        // One source for the container: instances that share a key must share its token (see the class remarks).
        // Options bind at first resolution, so the mode is unknown here; only a token-mode service resolves it.
        services.TryAddSingleton<ApnsTokenSource>();

        // Resolved only in certificate mode, by the primary handler and the expiry check.
        ApnsCertificateHolder.Register(services, name);
        services.AddSingleton<IHostedService>(serviceProvider => new ApnsCertificateExpiryCheck(
            serviceProvider.GetRequiredService<IOptionsMonitor<ApnsOptions>>(),
            name,
            () => ApnsCertificateHolder.Get(serviceProvider, name),
            serviceProvider.GetRequiredService<TimeProvider>(),
            serviceProvider.GetRequiredService<ILogger<ApnsCertificateExpiryCheck>>()
        ));

        var httpClientName = GetHttpClientName(name);

        services
            .AddHttpClient(
                httpClientName,
                (serviceProvider, client) =>
                {
                    var options = serviceProvider.GetRequiredService<IOptionsMonitor<ApnsOptions>>().Get(name);
                    client.BaseAddress = GetBaseAddress(options);
                    client.DefaultRequestVersion = HttpVersion.Version20;
                    client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact;
                    configureClient?.Invoke(client);
                }
            )
            .ConfigurePrimaryHttpMessageHandler(serviceProvider =>
                _CreatePrimaryHandler(serviceProvider, name, configurePrimaryHandler)
            )
            // The factory's default 2-minute rotation would keep opening fresh connections, and APNs starts each
            // token-authenticated connection with a single stream until it has seen a valid token.
            .SetHandlerLifetime(Timeout.InfiniteTimeSpan)
            // The factory's default loggers write the request URI, which carries the raw device token.
            .RemoveAllLoggers()
            .AddStandardResilienceHandler(options =>
            {
                options.Retry.MaxRetryAttempts = 2;
                // The default predicate also retries every 429, but APNs' TooManyRequests throttles one device
                // token, so a retry would spend its backoff on a token that is still throttled.
                // It also retries 5xx within seconds, but Apple asks senders to wait about 15 minutes before
                // retrying one, so a 5xx is returned as a failure for the caller to retry later.
                options.Retry.ShouldHandle = static args => ValueTask.FromResult(_IsPreSendFault(args.Outcome));
                // The default also counts 429, so throttled device tokens would open the breaker for the whole
                // instance. A failing server still counts even though it is no longer retried.
                options.CircuitBreaker.ShouldHandle = static args =>
                    ValueTask.FromResult(
                        _IsPreSendFault(args.Outcome)
                            || _IsServerFailure(args.Outcome)
                            || args.Outcome.Exception is TimeoutRejectedException
                    );
                // The standard limiter has no queue, so concurrent multicasts on one instance would be rejected
                // once they pass its permit count instead of waiting for a free slot.
                options.RateLimiter.DefaultRateLimiterOptions.QueueLimit = 10_000;

                configureResilience?.Invoke(options);
            });

        // One instance serves both service types, so the typed and shared paths share its HTTP client, provider
        // token, and options.
        if (name is null)
        {
            services.AddSingleton(static serviceProvider =>
                _CreateService(serviceProvider, HttpClientName, optionsName: null)
            );
            services.AddSingleton<IPushNotificationService>(static serviceProvider =>
                serviceProvider.GetRequiredService<ApnsPushNotificationService>()
            );
            services.AddSingleton<IApnsPushNotificationService>(static serviceProvider =>
                serviceProvider.GetRequiredService<ApnsPushNotificationService>()
            );

            return;
        }

        services.AddKeyedSingleton(name, (serviceProvider, _) => _CreateService(serviceProvider, httpClientName, name));
        services.AddKeyedSingleton<IPushNotificationService>(
            name,
            static (serviceProvider, key) => serviceProvider.GetRequiredKeyedService<ApnsPushNotificationService>(key)
        );
        services.AddKeyedSingleton<IApnsPushNotificationService>(
            name,
            static (serviceProvider, key) => serviceProvider.GetRequiredKeyedService<ApnsPushNotificationService>(key)
        );
    }

    internal static Action<IServiceCollection, string?> CopyOptions(ApnsOptions options)
    {
        return (services, name) =>
            services.Configure<ApnsOptions, ApnsOptionsValidator>(
                target =>
                {
                    target.KeyId = options.KeyId;
                    target.TeamId = options.TeamId;
                    target.PrivateKey = options.PrivateKey;
                    target.Certificate = options.Certificate;
                    target.CertificatePassword = options.CertificatePassword;
                    target.BundleId = options.BundleId;
                    target.Environment = options.Environment;
                    target.PushType = options.PushType;
                    target.Priority = options.Priority;
                    target.TreatBadDeviceTokenAsUnregistered = options.TreatBadDeviceTokenAsUnregistered;
                    target.MaxConcurrency = options.MaxConcurrency;
                    target.UseAlternativePort = options.UseAlternativePort;
                    target.Proxy = options.Proxy;
                    target.MaxConnections = options.MaxConnections;
                },
                name
            );
    }

    private static ApnsPushNotificationService _CreateService(
        IServiceProvider serviceProvider,
        string httpClientName,
        string? optionsName
    )
    {
        // The mode is fixed when the service is built; certificate mode never resolves the shared token source.
        IApnsAuthenticator authenticator = serviceProvider
            .GetRequiredService<IOptionsMonitor<ApnsOptions>>()
            .Get(optionsName)
            .UsesCertificate
            ? ApnsCertificateAuthenticator.Instance
            : new ApnsTokenAuthenticator(serviceProvider.GetRequiredService<ApnsTokenSource>());

        return new ApnsPushNotificationService(
            serviceProvider.GetRequiredService<IHttpClientFactory>(),
            httpClientName,
            authenticator,
            serviceProvider.GetRequiredService<IOptionsMonitor<ApnsOptions>>(),
            optionsName,
            serviceProvider.GetRequiredService<TimeProvider>(),
            serviceProvider.GetRequiredService<ILogger<ApnsPushNotificationService>>()
        );
    }

    private static SocketsHttpHandler _CreatePrimaryHandler(
        IServiceProvider serviceProvider,
        string? name,
        Action<SocketsHttpHandler>? configurePrimaryHandler
    )
    {
        // Apple asks providers to keep connections open rather than reconnecting per notification, so the pool
        // holds connections for hours and pings hourly to keep idle ones alive through middleboxes.
        var handler = new SocketsHttpHandler
        {
            EnableMultipleHttp2Connections = true,
            PooledConnectionLifetime = TimeSpan.FromHours(6),
            PooledConnectionIdleTimeout = TimeSpan.FromHours(1),
            KeepAlivePingDelay = TimeSpan.FromHours(1),
            KeepAlivePingTimeout = TimeSpan.FromSeconds(20),
            KeepAlivePingPolicy = HttpKeepAlivePingPolicy.Always,
        };

        if (serviceProvider.GetRequiredService<IOptionsMonitor<ApnsOptions>>().Get(name).UsesCertificate)
        {
            // The holder owns the certificate and the container disposes it. The handler lives as long as the
            // factory, since its lifetime is infinite, so it asks the holder at every TLS handshake instead of
            // fixing one certificate: each new connection presents the current, possibly renewed, certificate, and
            // the 6-hour PooledConnectionLifetime bounds how long an older connection keeps the replaced one.
            var holder = ApnsCertificateHolder.Get(serviceProvider, name);
            handler.SslOptions.LocalCertificateSelectionCallback = (_, _, _, _, _) => holder.Certificate;
        }

        // Read at handler build time: a live IWebProxy cannot reload from configuration anyway.
        var options = serviceProvider.GetRequiredService<IOptionsMonitor<ApnsOptions>>().Get(name);

        if (options.Proxy is not null)
        {
            handler.Proxy = options.Proxy;
        }

        // The instance's connection budget. Registered per instance so the semaphore counts exactly this
        // instance's connections, and captured by the callback so the bound survives option reloads: the budget a
        // live pool already dials under cannot change under it.
#pragma warning disable CA2000 // False positive: the limiter's ownership transfers to the handler through the callback; the factory disposes the handler for the handler's whole infinite lifetime.
        var limiter = new ApnsConnectionLimiter(options.MaxConnections);
#pragma warning restore CA2000
        handler.ConnectCallback = limiter.ConnectAsync;

        configurePrimaryHandler?.Invoke(handler);

        return handler;
    }

    private static bool _IsPreSendFault(Outcome<HttpResponseMessage> outcome)
    {
        // Only failures that prove the request never reached APNs are retried. A connection lost after the request
        // was sent may follow an accepted notification, and APNs does not deduplicate, so resending it would show
        // the notification twice.
        return outcome.Exception is HttpRequestException requestException
            && requestException.HttpRequestError
                is HttpRequestError.ConnectionError
                    or HttpRequestError.NameResolutionError
                    or HttpRequestError.SecureConnectionError;
    }

    private static bool _IsServerFailure(Outcome<HttpResponseMessage> outcome)
    {
        return outcome.Exception is null
            && outcome.Result?.StatusCode is HttpStatusCode.InternalServerError or HttpStatusCode.ServiceUnavailable;
    }
}

/// <summary>
/// Extension members for selecting APNs for a named push-notification instance on
/// <see cref="HeadlessPushNotificationsInstanceBuilder"/>. The instance owns its own named options, HTTP client,
/// resilience pipeline, and keyed service; it shares only the provider-token cache, keyed by team id and key id.
/// </summary>
[PublicAPI]
public static class SetupApnsPushNotificationsNamed
{
    extension(HeadlessPushNotificationsInstanceBuilder instance)
    {
        /// <summary>Uses APNs for this named instance, binding and validating <see cref="ApnsOptions"/> from configuration.</summary>
        /// <param name="configuration">Configuration section containing <see cref="ApnsOptions"/> values.</param>
        /// <param name="configureClient">
        /// Optional delegate to further configure the underlying <see cref="HttpClient"/>. It runs after the
        /// environment's <see cref="HttpClient.BaseAddress"/> is set, so it may replace it.
        /// </param>
        /// <param name="configureResilience">Optional delegate to override the default resilience pipeline.</param>
        /// <returns>The instance builder, for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is <see langword="null"/>.</exception>
        public HeadlessPushNotificationsInstanceBuilder UseApns(
            IConfiguration configuration,
            Action<HttpClient>? configureClient = null,
            Action<HttpStandardResilienceOptions>? configureResilience = null
        )
        {
            Argument.IsNotNull(configuration);

            var name = instance.Name;

            instance.RegisterProvider(services =>
                SetupApnsPushNotifications.AddApnsCore(
                    services,
                    name,
                    (s, n) => s.Configure<ApnsOptions, ApnsOptionsValidator>(configuration, n),
                    configureClient,
                    configureResilience
                )
            );

            return instance;
        }

        /// <summary>Uses APNs for this named instance, configuring <see cref="ApnsOptions"/> via a delegate.</summary>
        /// <param name="configure">Delegate that populates the options.</param>
        /// <param name="configureClient">
        /// Optional delegate to further configure the underlying <see cref="HttpClient"/>. It runs after the
        /// environment's <see cref="HttpClient.BaseAddress"/> is set, so it may replace it.
        /// </param>
        /// <param name="configureResilience">Optional delegate to override the default resilience pipeline.</param>
        /// <returns>The instance builder, for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
        public HeadlessPushNotificationsInstanceBuilder UseApns(
            Action<ApnsOptions> configure,
            Action<HttpClient>? configureClient = null,
            Action<HttpStandardResilienceOptions>? configureResilience = null
        )
        {
            Argument.IsNotNull(configure);

            var name = instance.Name;

            instance.RegisterProvider(services =>
                SetupApnsPushNotifications.AddApnsCore(
                    services,
                    name,
                    (s, n) => s.Configure<ApnsOptions, ApnsOptionsValidator>(configure, n),
                    configureClient,
                    configureResilience
                )
            );

            return instance;
        }

        /// <summary>Uses APNs for this named instance, configuring <see cref="ApnsOptions"/> with access to the service provider.</summary>
        /// <param name="configure">Delegate that populates the options, with access to the resolved service provider.</param>
        /// <param name="configureClient">
        /// Optional delegate to further configure the underlying <see cref="HttpClient"/>. It runs after the
        /// environment's <see cref="HttpClient.BaseAddress"/> is set, so it may replace it.
        /// </param>
        /// <param name="configureResilience">Optional delegate to override the default resilience pipeline.</param>
        /// <returns>The instance builder, for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
        public HeadlessPushNotificationsInstanceBuilder UseApns(
            Action<ApnsOptions, IServiceProvider> configure,
            Action<HttpClient>? configureClient = null,
            Action<HttpStandardResilienceOptions>? configureResilience = null
        )
        {
            Argument.IsNotNull(configure);

            var name = instance.Name;

            instance.RegisterProvider(services =>
                SetupApnsPushNotifications.AddApnsCore(
                    services,
                    name,
                    (s, n) => s.Configure<ApnsOptions, ApnsOptionsValidator>(configure, n),
                    configureClient,
                    configureResilience
                )
            );

            return instance;
        }

        /// <summary>Uses APNs for this named instance from a pre-built options instance (validated at startup).</summary>
        /// <param name="options">The options to copy.</param>
        /// <param name="configureClient">
        /// Optional delegate to further configure the underlying <see cref="HttpClient"/>. It runs after the
        /// environment's <see cref="HttpClient.BaseAddress"/> is set, so it may replace it.
        /// </param>
        /// <param name="configureResilience">Optional delegate to override the default resilience pipeline.</param>
        /// <returns>The instance builder, for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
        public HeadlessPushNotificationsInstanceBuilder UseApns(
            ApnsOptions options,
            Action<HttpClient>? configureClient = null,
            Action<HttpStandardResilienceOptions>? configureResilience = null
        )
        {
            Argument.IsNotNull(options);

            var name = instance.Name;

            instance.RegisterProvider(services =>
                SetupApnsPushNotifications.AddApnsCore(
                    services,
                    name,
                    SetupApnsPushNotifications.CopyOptions(options),
                    configureClient,
                    configureResilience
                )
            );

            return instance;
        }
    }
}
