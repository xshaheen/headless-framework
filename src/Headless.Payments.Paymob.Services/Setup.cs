// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Payments.Paymob.Services.CashIn;
using Headless.Payments.Paymob.Services.CashOut;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.Payments.Paymob.Services;

/// <summary>
/// Registers the high-level Paymob service layer — <see cref="IPaymobCashInService"/>,
/// <see cref="ICashOutService"/>, and <see cref="IPaymobCashInFeesCalculator"/> — that sits on top of
/// the CashIn and CashOut brokers.
/// </summary>
[PublicAPI]
public static class SetupPaymobServices
{
    /// <summary>
    /// Registers the Paymob CashIn/CashOut orchestration services and the default fees calculator.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add the services to.</param>
    /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
    /// <remarks>
    /// These services depend on <c>IPaymobCashInBroker</c> and <c>IPaymobCashOutBroker</c>; register the
    /// brokers first via <c>SetupPaymobCashIn.AddPaymobCashIn</c> and <c>SetupPaymobCashOut.AddPaymobCashOut</c>.
    /// The fees calculator is registered with Paymob's default fee structure (6 EGP fixed fee, 2.5% rate,
    /// 14% VAT on the fee); register your own <see cref="IPaymobCashInFeesCalculator"/> before this call to
    /// override it.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    public static IServiceCollection AddPaymobServices(this IServiceCollection services)
    {
        Argument.IsNotNull(services);

        services.TryAddScoped<IPaymobCashInService, PaymobCashInService>();
        services.TryAddScoped<ICashOutService, PaymobCashOutService>();
        services.TryAddSingleton<IPaymobCashInFeesCalculator>(_ => new PaymobCashInFeesCalculator());

        return services;
    }
}
