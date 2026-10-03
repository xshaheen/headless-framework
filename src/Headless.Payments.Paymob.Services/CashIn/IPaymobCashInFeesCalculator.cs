// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Payments.Paymob.Services.CashIn;

/// <summary>
/// Calculates Paymob Accept payment-gateway fees for a given transaction amount.
/// </summary>
/// <remarks>
/// All calculations apply ceiling rounding at two decimal places. The default fee structure
/// used by <c>PaymobCashInFeesCalculator</c> is: fixed fee of 6 EGP, 2.5% percentage fee,
/// and 14% VAT on the total fee. Instantiate <c>PaymobCashInFeesCalculator</c> directly
/// with custom parameters when the merchant's negotiated rates differ.
/// </remarks>
[PublicAPI]
public interface IPaymobCashInFeesCalculator
{
    /// <summary>Calculate the fess that the payment gateway will deduct from the <paramref name="amount"/>.</summary>
    /// <returns>Fees and tax on that fees.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="amount"/> cannot be zero or negative.</exception>
    decimal CalculateDeductFees(decimal amount);

    /// <summary>Calculate the fess that the payment gateway will deduct from the <paramref name="amount"/>.</summary>
    /// <returns>Fees and tax on that fees.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="amount"/> cannot be zero or negative.</exception>
    (decimal Fees, decimal Tax) CalculateDeductFeesAndTax(decimal amount);

    /// <summary>Add the payment getaway fees to the <paramref name="net"/>. The value may have an extra + 1.</summary>
    /// <returns>Amount + Fees</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="net"/> cannot be zero or negative.</exception>
    /// <remarks>
    /// This is based on inverse of the Net function.
    /// net(amount) = amount - totalFees
    ///             = amount - (ceilingRound(amount * percentageFees, 2) * vatScaler) - ceilingRound((fixedFees * vatScaler), 2)
    ///             = amount - (ceilingRound(amount * percentageFees, 2) * vatScaler) - (fixedFees * vatScaler)
    ///             ~ amount * (1 - (vatScaler * percentageFees)) - (fixedFees * vatScaler)
    /// amount(net) ~ (Net + (fixedFees * vatScaler)) / (1 - (vatScaler * percentageFees))
    /// </remarks>
    decimal AddFeesForNet(decimal net);

    /// <summary>Calculate the payment getaway fees to get net <paramref name="net"/>.</summary>
    /// <returns>Payment fees to add to the <paramref name="net"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="net"/> cannot be zero or negative.</exception>
    decimal CalcFeesForNet(decimal net);
}
