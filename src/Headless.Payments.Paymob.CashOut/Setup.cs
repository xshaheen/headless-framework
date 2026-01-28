// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Payments.Paymob.CashOut.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;

namespace Headless.Payments.Paymob.CashOut;

[PublicAPI]
public static class PaymobCashOutSetup
{
    internal const string HttpClientName = "Headless:PaymobCashOut";

    /// <summary>Adds services required for using paymob cash out.</summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add the services to.</param>
    /// <param name="setupAction">The action used to configure <see cref="PaymobCashOutOptions"/>.</param>
    /// <param name="configureClient"></param>
    /// <param name="configureResilience"></param>
    /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
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

    /// <summary>Adds services required for using paymob cash out.</summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add the services to.</param>
    /// <param name="setupAction">The action used to configure <see cref="PaymobCashOutOptions"/>.</param>
    /// <param name="configureClient"></param>
    /// <param name="configureResilience"></param>
    /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
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

    /// <summary>Adds services required for using paymob cash out.</summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add the services to.</param>
    /// <param name="config">The configuration section that contains <see cref="PaymobCashOutOptions"/> settings.</param>
    /// <param name="configureClient"></param>
    /// <param name="configureResilience"></param>
    /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
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

        if (configureResilience is not null)
        {
            httpClientBuilder.AddStandardResilienceHandler(configureResilience);
        }
        else
        {
            httpClientBuilder.AddStandardResilienceHandler();
        }

        services.AddSingleton<IPaymobCashOutAuthenticator, PaymobCashOutAuthenticator>();

        services
            .AddScoped<IPaymobCashOutBroker, PaymobCashOutBroker>()
            .AddHttpClient<IPaymobCashOutBroker, PaymobCashOutBroker>(HttpClientName);

        return services;
    }
}
