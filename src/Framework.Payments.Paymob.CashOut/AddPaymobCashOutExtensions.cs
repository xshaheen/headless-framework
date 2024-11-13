// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Kernel.Checks;
using Framework.Payments.Paymob.CashOut.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;

namespace Framework.Payments.Paymob.CashOut;

[PublicAPI]
public static class AddPaymobCashOutExtensions
{
    public static IServiceCollection AddPaymobCashOut(
        this IServiceCollection services,
        Action<PaymobCashOutOptions> setupAction,
        Action<HttpClient>? configureClient = null,
        Action<HttpStandardResilienceOptions>? configureResilience = null
    )
    {
        Argument.IsNotNull(services);
        Argument.IsNotNull(setupAction);

        services.ConfigureOptions<PaymobCashOutOptions, PaymobCashOutOptionsValidator>(setupAction);

        return _AddCore(services, configureClient, configureResilience);
    }

    public static IServiceCollection AddPaymobCashOut(
        this IServiceCollection services,
        Action<PaymobCashOutOptions, IServiceProvider> setupAction,
        Action<HttpClient>? configureClient = null,
        Action<HttpStandardResilienceOptions>? configureResilience = null
    )
    {
        Argument.IsNotNull(services);
        Argument.IsNotNull(setupAction);

        services.ConfigureOptions<PaymobCashOutOptions, PaymobCashOutOptionsValidator>(setupAction);

        return _AddCore(services, configureClient, configureResilience);
    }

    public static IServiceCollection AddPaymobCashOut(
        this IServiceCollection services,
        IConfiguration config,
        Action<HttpClient>? configureClient = null,
        Action<HttpStandardResilienceOptions>? configureResilience = null
    )
    {
        Argument.IsNotNull(services);
        Argument.IsNotNull(config);

        services.ConfigureOptions<PaymobCashOutOptions, PaymobCashOutOptionsValidator>(config);

        return _AddCore(services, configureClient, configureResilience);
    }

    private static IServiceCollection _AddCore(
        IServiceCollection services,
        Action<HttpClient>? configureClient = null,
        Action<HttpStandardResilienceOptions>? configureResilience = null
    )
    {
        const string clientName = "paymob_cash_out";

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

        services
            .AddSingleton<IPaymobCashOutAuthenticator, PaymobCashOutAuthenticator>()
            .AddHttpClient<IPaymobCashOutAuthenticator, PaymobCashOutAuthenticator>(clientName);

        services
            .AddScoped<IPaymobCashOutBroker, PaymobCashOutBroker>()
            .AddHttpClient<IPaymobCashOutBroker, PaymobCashOutBroker>(clientName);

        return services;
    }
}
