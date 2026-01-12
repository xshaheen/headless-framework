// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;
using Framework.Payments.Paymob.CashIn.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;

namespace Framework.Payments.Paymob.CashIn;

[PublicAPI]
public static class AddPaymobCashInExtensions
{
    /// <summary>Adds services required for using paymob cash in.</summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add the services to.</param>
    /// <param name="setupAction">The action used to configure <see cref="PaymobCashInOptions"/>.</param>
    /// <param name="configureClient"></param>
    /// <param name="configureResilience"></param>
    /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
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

        return _AddCore(services, configureClient, configureResilience);
    }

    /// <summary>Adds services required for using paymob cash in.</summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add the services to.</param>
    /// <param name="config">The configuration section that contains <see cref="PaymobCashInOptions"/> settings.</param>
    /// <param name="configureClient"></param>
    /// <param name="configureResilience"></param>
    /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
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

        return _AddCore(services, configureClient, configureResilience);
    }

    private static IServiceCollection _AddCore(
        IServiceCollection services,
        Action<HttpClient>? configureClient = null,
        Action<HttpStandardResilienceOptions>? configureResilience = null
    )
    {
        services.TryAddSingleton(TimeProvider.System);

        const string clientName = "paymob_cash_in";

        var httpClientBuilder = configureClient is not null
            ? services.AddHttpClient(clientName, configureClient)
            : services.AddHttpClient(clientName);

        if (configureResilience is not null)
        {
            httpClientBuilder.AddStandardResilienceHandler(configureResilience);
        }
        else
        {
            httpClientBuilder.AddStandardResilienceHandler();
        }

        services.AddSingleton<IPaymobCashInAuthenticator, PaymobCashInAuthenticator>();

        services
            .AddScoped<IPaymobCashInBroker, PaymobCashInBroker>()
            .AddHttpClient<IPaymobCashInBroker, PaymobCashInBroker>(clientName);

        return services;
    }
}
