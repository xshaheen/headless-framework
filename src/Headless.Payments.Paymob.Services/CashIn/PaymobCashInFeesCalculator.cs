// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Payments.Paymob.Services.CashIn;

/// <summary>
/// Default <see cref="IPaymobCashInFeesCalculator"/> implementation using Paymob Accept's fee structure.
/// Construct directly with custom parameters when the merchant's negotiated rates differ from the defaults
/// (6 EGP fixed fee, 2.5% percentage fee, 14% VAT on the fee).
/// </summary>
[PublicAPI]
public sealed class PaymobCashInFeesCalculator(
    decimal fixedFeesPerTransaction = 6,
    decimal percentageFeesPerTransaction = 0.025m,
    decimal vatPercentOnFees = 0.14m
) : IPaymobCashInFeesCalculator
{
    private const MidpointRounding _Mode = MidpointRounding.ToPositiveInfinity;

    public decimal CalculateDeductFees(decimal amount)
    {
        var (fees, tax) = CalculateDeductFeesAndTax(amount);

        return fees + tax;
    }

    public (decimal Fees, decimal Tax) CalculateDeductFeesAndTax(decimal amount)
    {
        Argument.IsPositive(amount);

        var fees = decimal.Round((amount * percentageFeesPerTransaction) + fixedFeesPerTransaction, 2, _Mode);

        var tax = decimal.Round(fees * vatPercentOnFees, 2, _Mode);

        return (fees, tax);
    }

    public decimal AddFeesForNet(decimal net)
    {
        Argument.IsPositive(net);

        var vatScaler = 1m + vatPercentOnFees;
        var vatFixedFees = fixedFeesPerTransaction * vatScaler;
        var vatPercentageFees = percentageFeesPerTransaction * vatScaler;

        var amount = decimal.Round((net + vatFixedFees) / (1 - vatPercentageFees), 2, _Mode);

        return Math.Ceiling(amount + 0.01m);
    }

    public decimal CalcFeesForNet(decimal net)
    {
        return AddFeesForNet(net) - net;
    }
}
