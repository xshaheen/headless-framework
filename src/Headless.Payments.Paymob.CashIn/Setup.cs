// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Http.Resilience;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;

namespace Headless.Payments.Paymob.CashIn;

[PublicAPI]
public static class SetupPaymobCashIn
{
    internal const string HttpClientName = "Headless:PaymobCashIn";

    /// <summary>
    /// Registers Paymob CashIn services using an inline configuration delegate.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add the services to.</param>
    /// <param name="setupAction">Delegate that configures <see cref="PaymobCashInOptions"/>.</param>
    /// <param name="configureClient">Optional delegate to customise the internal <c>HttpClient</c> (base address, headers, etc.).</param>
    /// <param name="configureResilience">Optional delegate applied after the derived resilience defaults (no automatic retry on POST).</param>
    /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
    /// <remarks>
    /// Registers <c>IPaymobCashInAuthenticator</c> as a singleton (token cache is process-scoped) and
    /// <c>IPaymobCashInBroker</c> as scoped. Options are validated on startup.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> or <paramref name="setupAction"/> is <see langword="null"/>.</exception>
    public static IServiceCollection AddPaymobCashIn(
        this IServiceCollection services,
        Action<PaymobCashInOptions> setupAction,
        Action<HttpClient>? configureClient = null,
        Action<HttpStandardResilienceOptions>? configureResilience = null
    )
    {
        Argument.IsNotNull(services);
        Argument.IsNotNull(setupAction);

        services.Configure<PaymobCashInOptions, PaymobCashInOptionsValidator>(setupAction);

        return _AddPaymobCashInCore(services, configureClient, configureResilience);
    }

    /// <summary>
    /// Registers Paymob CashIn services using an inline configuration delegate that receives the
    /// <see cref="IServiceProvider"/>, allowing options to reference other registered services.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add the services to.</param>
    /// <param name="setupAction">Delegate that configures <see cref="PaymobCashInOptions"/> using the service provider.</param>
    /// <param name="configureClient">Optional delegate to customise the internal <c>HttpClient</c>.</param>
    /// <param name="configureResilience">Optional delegate applied after the derived resilience defaults (no automatic retry on POST).</param>
    /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
    /// <remarks>
    /// Registers <c>IPaymobCashInAuthenticator</c> as a singleton (token cache is process-scoped) and
    /// <c>IPaymobCashInBroker</c> as scoped. Options are validated on startup.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> or <paramref name="setupAction"/> is <see langword="null"/>.</exception>
    public static IServiceCollection AddPaymobCashIn(
        this IServiceCollection services,
        Action<PaymobCashInOptions, IServiceProvider> setupAction,
        Action<HttpClient>? configureClient = null,
        Action<HttpStandardResilienceOptions>? configureResilience = null
    )
    {
        Argument.IsNotNull(services);
        Argument.IsNotNull(setupAction);

        services.Configure<PaymobCashInOptions, PaymobCashInOptionsValidator>(setupAction);

        return _AddPaymobCashInCore(services, configureClient, configureResilience);
    }

    /// <summary>
    /// Registers Paymob CashIn services using an <see cref="IConfiguration"/> section.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add the services to.</param>
    /// <param name="config">The configuration section that contains <see cref="PaymobCashInOptions"/> settings.</param>
    /// <param name="configureClient">Optional delegate to customise the internal <c>HttpClient</c>.</param>
    /// <param name="configureResilience">Optional delegate applied after the derived resilience defaults (no automatic retry on POST).</param>
    /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
    /// <remarks>
    /// Registers <c>IPaymobCashInAuthenticator</c> as a singleton (token cache is process-scoped) and
    /// <c>IPaymobCashInBroker</c> as scoped. Options are validated on startup.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> or <paramref name="config"/> is <see langword="null"/>.</exception>
    public static IServiceCollection AddPaymobCashIn(
        this IServiceCollection services,
        IConfiguration config,
        Action<HttpClient>? configureClient = null,
        Action<HttpStandardResilienceOptions>? configureResilience = null
    )
    {
        Argument.IsNotNull(services);
        Argument.IsNotNull(config);

        services.Configure<PaymobCashInOptions, PaymobCashInOptionsValidator>(config);

        return _AddPaymobCashInCore(services, configureClient, configureResilience);
    }

    private static IServiceCollection _AddPaymobCashInCore(
        IServiceCollection services,
        Action<HttpClient>? configureClient = null,
        Action<HttpStandardResilienceOptions>? configureResilience = null
    )
    {
        services.TryAddSingleton(TimeProvider.System);

        var httpClientBuilder = configureClient is not null
            ? services.AddHttpClient(HttpClientName, configureClient)
            : services.AddHttpClient(HttpClientName);

        // Order, payment-key, intention, refund, and void POSTs carry no idempotency header, and merchant_order_id /
        // special_reference are caller-chosen body fields, not a retry-deduplication contract: a retried POST can
        // create a duplicate order or refund twice. The derived pipeline never retries POSTs, keeps retry for
        // order and transaction inquiry (GET), and removes host-wide handlers that would otherwise retry anyway.
        // configureResilience runs after these defaults; setting Retry.ShouldHandle there opts back in.
        httpClientBuilder.AddEffectResilienceHandler(OutboundEffect.Unsafe, configureResilience: configureResilience);

        services.AddSingleton<IPaymobCashInAuthenticator, PaymobCashInAuthenticator>();

        services
            .AddScoped<IPaymobCashInBroker, PaymobCashInBroker>()
            .AddHttpClient<IPaymobCashInBroker, PaymobCashInBroker>(HttpClientName);

        return services;
    }
}
