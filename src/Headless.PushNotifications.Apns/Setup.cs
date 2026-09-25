// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Checks;
using Headless.PushNotifications.Apns;
using Headless.PushNotifications.Apns.Internals;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
/// The default resilience pipeline retries transport faults, HTTP 500, and HTTP 503 at most twice, and never
/// retries HTTP 429, which throttles a single device token. Pass <c>configureResilience</c> to change it.
/// </para>
/// </remarks>
[PublicAPI]
public static class SetupApnsPushNotifications
{
    internal const string HttpClientName = "Headless:Apns";

    internal static readonly Uri ProductionAddress = new("https://api.push.apple.com");
    internal static readonly Uri SandboxAddress = new("https://api.sandbox.push.apple.com");

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
    internal static void AddApnsCore(
        IServiceCollection services,
        string? name,
        Action<IServiceCollection, string?> configureOptions,
        Action<HttpClient>? configureClient,
        Action<HttpStandardResilienceOptions>? configureResilience
    )
    {
        configureOptions(services, name);
        services.TryAddSingleton(TimeProvider.System);

        // One source for the container: instances that share a key must share its token (see the class remarks).
        services.TryAddSingleton<ApnsTokenSource>();

        var httpClientName = GetHttpClientName(name);

        services
            .AddHttpClient(
                httpClientName,
                (serviceProvider, client) =>
                {
                    var options = serviceProvider.GetRequiredService<IOptionsMonitor<ApnsOptions>>().Get(name);
                    client.BaseAddress =
                        options.Environment == ApnsEnvironment.Sandbox ? SandboxAddress : ProductionAddress;
                    client.DefaultRequestVersion = HttpVersion.Version20;
                    client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact;
                    configureClient?.Invoke(client);
                }
            )
            .ConfigurePrimaryHttpMessageHandler(static () => _CreatePrimaryHandler())
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
                options.Retry.ShouldHandle = static args => ValueTask.FromResult(_IsRetryable(args.Outcome));
                // The default also counts 429, so throttled device tokens would open the breaker for the whole
                // instance.
                options.CircuitBreaker.ShouldHandle = static args =>
                    ValueTask.FromResult(
                        _IsRetryable(args.Outcome) || args.Outcome.Exception is TimeoutRejectedException
                    );
                // The standard limiter has no queue, so concurrent multicasts on one instance would be rejected
                // once they pass its permit count instead of waiting for a free slot.
                options.RateLimiter.DefaultRateLimiterOptions.QueueLimit = 10_000;

                configureResilience?.Invoke(options);
            });

        if (name is null)
        {
            services.AddSingleton<IPushNotificationService>(static serviceProvider =>
                _CreateService(serviceProvider, HttpClientName, optionsName: null)
            );

            return;
        }

        services.AddKeyedSingleton<IPushNotificationService>(
            name,
            (serviceProvider, _) => _CreateService(serviceProvider, httpClientName, name)
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
                    target.BundleId = options.BundleId;
                    target.Environment = options.Environment;
                    target.PushType = options.PushType;
                    target.Priority = options.Priority;
                    target.TreatBadDeviceTokenAsUnregistered = options.TreatBadDeviceTokenAsUnregistered;
                    target.MaxConcurrency = options.MaxConcurrency;
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
        return new ApnsPushNotificationService(
            serviceProvider.GetRequiredService<IHttpClientFactory>(),
            httpClientName,
            serviceProvider.GetRequiredService<ApnsTokenSource>(),
            serviceProvider.GetRequiredService<IOptionsMonitor<ApnsOptions>>(),
            optionsName,
            serviceProvider.GetRequiredService<ILogger<ApnsPushNotificationService>>()
        );
    }

    private static SocketsHttpHandler _CreatePrimaryHandler()
    {
        // Apple asks providers to keep connections open rather than reconnecting per notification, so the pool
        // holds connections for hours and pings hourly to keep idle ones alive through middleboxes.
        return new SocketsHttpHandler
        {
            EnableMultipleHttp2Connections = true,
            PooledConnectionLifetime = TimeSpan.FromHours(6),
            PooledConnectionIdleTimeout = TimeSpan.FromHours(1),
            KeepAlivePingDelay = TimeSpan.FromHours(1),
            KeepAlivePingTimeout = TimeSpan.FromSeconds(20),
            KeepAlivePingPolicy = HttpKeepAlivePingPolicy.Always,
        };
    }

    private static bool _IsRetryable(Outcome<HttpResponseMessage> outcome)
    {
        // Only failures that prove the request never reached APNs are retried. A connection lost after the request
        // was sent may follow an accepted notification, and APNs does not deduplicate, so resending it would show
        // the notification twice.
        if (outcome.Exception is HttpRequestException requestException)
        {
            return requestException.HttpRequestError
                is HttpRequestError.ConnectionError
                    or HttpRequestError.NameResolutionError
                    or HttpRequestError.SecureConnectionError;
        }

        if (outcome.Exception is not null)
        {
            return false;
        }

        return outcome.Result?.StatusCode is HttpStatusCode.InternalServerError or HttpStatusCode.ServiceUnavailable;
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
