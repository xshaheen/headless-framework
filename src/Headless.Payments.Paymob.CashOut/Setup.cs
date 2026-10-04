// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Http.Resilience;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;

namespace Headless.Payments.Paymob.CashOut;

[PublicAPI]
public static class SetupPaymobCashOut
{
    internal const string HttpClientName = "Headless:PaymobCashOut";

    /// <summary>
    /// Registers Paymob CashOut services using an inline configuration delegate.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add the services to.</param>
    /// <param name="setupAction">Delegate that configures <see cref="PaymobCashOutOptions"/>.</param>
    /// <param name="configureClient">Optional delegate to customise the internal <c>HttpClient</c>.</param>
    /// <param name="configureResilience">Optional delegate applied after the derived resilience defaults (no automatic retry on POST).</param>
    /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
    /// <remarks>
    /// Registers <c>IPaymobCashOutAuthenticator</c> as a singleton (token cache is process-scoped) and
    /// <c>IPaymobCashOutBroker</c> as scoped. Options are validated on startup.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> or <paramref name="setupAction"/> is <see langword="null"/>.</exception>
    public static IServiceCollection AddPaymobCashOut(
        this IServiceCollection services,
        Action<PaymobCashOutOptions> setupAction,
        Action<HttpClient>? configureClient = null,
        Action<HttpStandardResilienceOptions>? configureResilience = null
    )
    {
        Argument.IsNotNull(services);
        Argument.IsNotNull(setupAction);

        services.Configure<PaymobCashOutOptions, PaymobCashOutOptionsValidator>(setupAction);

        return _AddCore(services, configureClient, configureResilience);
    }

    /// <summary>
    /// Registers Paymob CashOut services using an inline configuration delegate that receives the
    /// <see cref="IServiceProvider"/>, allowing options to reference other registered services.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add the services to.</param>
    /// <param name="setupAction">Delegate that configures <see cref="PaymobCashOutOptions"/> using the service provider.</param>
    /// <param name="configureClient">Optional delegate to customise the internal <c>HttpClient</c>.</param>
    /// <param name="configureResilience">Optional delegate applied after the derived resilience defaults (no automatic retry on POST).</param>
    /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
    /// <remarks>
    /// Registers <c>IPaymobCashOutAuthenticator</c> as a singleton and <c>IPaymobCashOutBroker</c>
    /// as scoped. Options are validated on startup.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> or <paramref name="setupAction"/> is <see langword="null"/>.</exception>
    public static IServiceCollection AddPaymobCashOut(
        this IServiceCollection services,
        Action<PaymobCashOutOptions, IServiceProvider> setupAction,
        Action<HttpClient>? configureClient = null,
        Action<HttpStandardResilienceOptions>? configureResilience = null
    )
    {
        Argument.IsNotNull(services);
        Argument.IsNotNull(setupAction);

        services.Configure<PaymobCashOutOptions, PaymobCashOutOptionsValidator>(setupAction);

        return _AddCore(services, configureClient, configureResilience);
    }

    /// <summary>
    /// Registers Paymob CashOut services using an <see cref="IConfiguration"/> section.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add the services to.</param>
    /// <param name="config">The configuration section that contains <see cref="PaymobCashOutOptions"/> settings.</param>
    /// <param name="configureClient">Optional delegate to customise the internal <c>HttpClient</c>.</param>
    /// <param name="configureResilience">Optional delegate applied after the derived resilience defaults (no automatic retry on POST).</param>
    /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
    /// <remarks>
    /// Registers <c>IPaymobCashOutAuthenticator</c> as a singleton and <c>IPaymobCashOutBroker</c>
    /// as scoped. Options are validated on startup.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> or <paramref name="config"/> is <see langword="null"/>.</exception>
    public static IServiceCollection AddPaymobCashOut(
        this IServiceCollection services,
        IConfiguration config,
        Action<HttpClient>? configureClient = null,
        Action<HttpStandardResilienceOptions>? configureResilience = null
    )
    {
        Argument.IsNotNull(services);
        Argument.IsNotNull(config);

        services.Configure<PaymobCashOutOptions, PaymobCashOutOptionsValidator>(config);

        return _AddCore(services, configureClient, configureResilience);
    }

    private static IServiceCollection _AddCore(
        IServiceCollection services,
        Action<HttpClient>? configureClient = null,
        Action<HttpStandardResilienceOptions>? configureResilience = null
    )
    {
        services.TryAddSingleton(TimeProvider.System);

        var httpClientBuilder = configureClient is not null
            ? services.AddHttpClient(HttpClientName, configureClient)
            : services.AddHttpClient(HttpClientName);

        // /disburse carries no idempotency key or client reference, so a retried payout POST can pay out twice.
        // The derived pipeline never retries POSTs, keeps retry for budget and transaction inquiry (GET), and
        // removes host-wide handlers (such as service defaults) that would otherwise retry anyway.
        // configureResilience runs after these defaults; setting Retry.ShouldHandle there opts back in.
        httpClientBuilder.AddEffectResilienceHandler(OutboundEffect.Unsafe, configureResilience: configureResilience);

        services.AddSingleton<IPaymobCashOutAuthenticator, PaymobCashOutAuthenticator>();

        services
            .AddScoped<IPaymobCashOutBroker, PaymobCashOutBroker>()
            .AddHttpClient<IPaymobCashOutBroker, PaymobCashOutBroker>(HttpClientName);

        return services;
    }
}
