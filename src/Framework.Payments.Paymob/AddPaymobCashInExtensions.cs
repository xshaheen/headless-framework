// Copyright (c) Mahmoud Shaheen, 2021. All rights reserved.

using Framework.Kernel.Checks;
using Framework.Payments.Paymob.CashIn.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Framework.Payments.Paymob.CashIn;

public static class AddPaymobCashInExtensions
{
    /// <summary>Adds services required for using paymob cash in.</summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add the services to.</param>
    /// <param name="setupAction">The action used to configure <see cref="PaymobCashInOptions"/>.</param>
    /// <param name="retryPolicy">Retry policy used to override used policy.</param>
    /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
    public static IServiceCollection AddPaymobCashIn(
        this IServiceCollection services,
        Action<PaymobCashInOptions> setupAction,
        Func<PolicyBuilder<HttpResponseMessage>, IAsyncPolicy<HttpResponseMessage>>? retryPolicy = null
    )
    {
        Argument.IsNotNull(services);
        Argument.IsNotNull(setupAction);

        services.AddOptions<PaymobCashInOptions>().PostConfigure(setupAction).ValidateDataAnnotations();
        _AddServices(services, retryPolicy);
        return services;
    }

    /// <summary>Adds services required for using paymob cash in.</summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add the services to.</param>
    /// <param name="cashInSection">The configuration section that contains <see cref="PaymobCashInOptions"/> settings.</param>
    /// <param name="retryPolicy">Retry policy used to override used policy.</param>
    /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
    public static IServiceCollection AddPaymobCashIn(
        this IServiceCollection services,
        IConfigurationSection cashInSection,
        Func<PolicyBuilder<HttpResponseMessage>, IAsyncPolicy<HttpResponseMessage>>? retryPolicy = null
    )
    {
        Argument.IsNotNull(services);
        Argument.IsNotNull(cashInSection);

        services.AddOptions<PaymobCashInOptions>().Bind(cashInSection).ValidateDataAnnotations();
        _AddServices(services, retryPolicy);
        return services;
    }

    private static void _AddServices(
        IServiceCollection services,
        Func<PolicyBuilder<HttpResponseMessage>, IAsyncPolicy<HttpResponseMessage>>? retryPolicy
    )
    {
        services.AddMemoryCache();
        const string clientName = "paymob_cash_in";
        services.AddHttpClient(clientName);

        services
            .AddSingleton<IPaymobCashInAuthenticator, PaymobCashInAuthenticator>()
            .AddHttpClient<IPaymobCashInAuthenticator, PaymobCashInAuthenticator>(clientName)
            .AddTransientHttpErrorPolicy(retryPolicy ?? _ConfigurePolicy);

        services
            .AddScoped<IPaymobCashInBroker, PaymobCashInBroker>()
            .AddHttpClient<IPaymobCashInBroker, PaymobCashInBroker>(clientName)
            .AddTransientHttpErrorPolicy(retryPolicy ?? _ConfigurePolicy);
    }

    private static IAsyncPolicy<HttpResponseMessage> _ConfigurePolicy(PolicyBuilder<HttpResponseMessage> builder)
    {
        return builder.WaitAndRetryAsync(
            new[]
            {
                TimeSpan.FromMilliseconds(100),
                TimeSpan.FromMilliseconds(200),
                TimeSpan.FromMilliseconds(400),
                TimeSpan.FromMilliseconds(800),
            }
        );
    }
}
