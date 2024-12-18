// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Payments.Paymob.Services.CashIn;

public sealed class PaymobCashInFeesCalculator(
    decimal fixedFeesPerTransaction = 6,
    decimal percentageFeesPerTransaction = 0.025m,
    decimal vatPercentOnFees = 0.14m
)
{
    private const MidpointRounding _Mode = MidpointRounding.ToPositiveInfinity;

    /// <summary>Calculate the fess that the payment gateway will deduct from the <paramref name="amount"/>.</summary>
    /// <returns>Fees and tax on that fees.</returns>
    /// <exception cref="ArgumentException"><paramref name="amount"/> cannot be zero or negative.</exception>
    public decimal CalculateDeductFees(decimal amount)
    {
        var (fees, tax) = CalculateDeductFeesAndTax(amount);

        return fees + tax;
    }

    /// <summary>Calculate the fess that the payment gateway will deduct from the <paramref name="amount"/>.</summary>
    /// <returns>Fees and tax on that fees.</returns>
    /// <exception cref="ArgumentException"><paramref name="amount"/> cannot be zero or negative.</exception>
    public (decimal Fees, decimal Tax) CalculateDeductFeesAndTax(decimal amount)
    {
        var fees = decimal.Round((amount * percentageFeesPerTransaction) + fixedFeesPerTransaction, 2, _Mode);

        var tax = decimal.Round(fees * vatPercentOnFees, 2, _Mode);

        return (fees, tax);
    }

    /// <summary>Add the payment getaway fees to the <paramref name="net"/>. The value may have an extra + 1.</summary>
    /// <returns>Amount + Fees</returns>
    /// <exception cref="ArgumentException"><paramref name="net"/> cannot be zero or negative.</exception>
    /// <remarks>
    /// This is based on inverse of the Net function.
    /// net(amount) = amount - totalFees
    ///             = amount - (ceilingRound(amount * percentageFees, 2) * vatScaler) - ceilingRound((fixedFees * vatScaler), 2)
    ///             = amount - (ceilingRound(amount * percentageFees, 2) * vatScaler) - (fixedFees * vatScaler)
    ///             ~ amount * (1 - (vatScaler * percentageFees)) - (fixedFees * vatScaler)
    /// amount(net) ~ (Net + (fixedFees * vatScaler)) / (1 - (vatScaler * percentageFees))
    /// </remarks>
    public decimal AddFeesForNet(decimal net)
    {
        var vatScaler = 1m + vatPercentOnFees;
        var vatFixedFees = fixedFeesPerTransaction * vatScaler;
        var vatPercentageFees = vatPercentOnFees * vatScaler;

        var amount = decimal.Round((net + vatFixedFees) / (1 - vatPercentageFees), 2, _Mode);

        return Math.Ceiling(amount + 0.01m);
    }

    /// <summary>Calculate the payment getaway fees to get net <paramref name="net"/>.</summary>
    /// <returns>Payment fees to add to the <paramref name="net"/>.</returns>
    /// <exception cref="ArgumentException"><paramref name="net"/> cannot be zero or negative.</exception>
    public decimal CalcFeesForNet(decimal net)
    {
        return AddFeesForNet(net) - net;
    }
}
