// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;

namespace Headless.Payments.Paymob.CashOut;

// The AddPaymobCashOut overloads, options validation, and the named client with its effect-derived resilience
// pipeline are generated from PaymobCashOutOptions. Only the package's own services are wired here.
public static partial class SetupPaymobCashOut
{
    /// <summary>
    /// Registers <c>IPaymobCashOutAuthenticator</c> as a singleton (the token cache is process-scoped) and
    /// <c>IPaymobCashOutBroker</c> as a scoped typed client over the generated named client.
    /// </summary>
    private static partial void _AddServices(IServiceCollection services)
    {
        services.AddSingleton<IPaymobCashOutAuthenticator, PaymobCashOutAuthenticator>();

        services
            .AddScoped<IPaymobCashOutBroker, PaymobCashOutBroker>()
            .AddHttpClient<IPaymobCashOutBroker, PaymobCashOutBroker>(HttpClientName);
    }
}
