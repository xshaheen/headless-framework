// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;

namespace Headless.Payments.Paymob.CashIn;

// The AddPaymobCashIn overloads, options validation, and the named client with its effect-derived resilience
// pipeline are generated from PaymobCashInOptions. Only the package's own services are wired here.
public static partial class SetupPaymobCashIn
{
    /// <summary>
    /// Registers <c>IPaymobCashInAuthenticator</c> as a singleton (the token cache is process-scoped) and
    /// <c>IPaymobCashInBroker</c> as a scoped typed client over the generated named client.
    /// </summary>
    private static partial void _AddServices(IServiceCollection services)
    {
        services.AddSingleton<IPaymobCashInAuthenticator, PaymobCashInAuthenticator>();

        services
            .AddScoped<IPaymobCashInBroker, PaymobCashInBroker>()
            .AddHttpClient<IPaymobCashInBroker, PaymobCashInBroker>(HttpClientName);
    }
}
