// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Payments.Paymob.Services.CashIn;
using Headless.Testing.Tests;

namespace Tests;

public sealed class PaymobCashInFeesCalculatorTests : TestBase
{
    // Paymob Accept defaults: 6 EGP fixed fee, 2.5% of the amount, 14% VAT on the resulting fee.
    private static readonly PaymobCashInFeesCalculator _Default = new();

    [Theory]
    [InlineData(1000, 31, 4.34)]
    [InlineData(110, 8.75, 1.23)]
    [InlineData(100, 8.5, 1.19)]
    public void should_deduct_fixed_and_percentage_fee_plus_vat_on_that_fee(
        decimal amount,
        decimal expectedFees,
        decimal expectedTax
    )
    {
        var (fees, tax) = _Default.CalculateDeductFeesAndTax(amount);

        fees.Should().Be(expectedFees);
        tax.Should().Be(expectedTax);
        _Default.CalculateDeductFees(amount).Should().Be(expectedFees + expectedTax);
    }

    [Theory]
    [InlineData(1, 9)]
    [InlineData(10, 18)]
    [InlineData(100, 110)]
    [InlineData(500, 522)]
    [InlineData(1000, 1037)]
    [InlineData(12345.67, 12715)]
    public void should_gross_up_net_with_the_percentage_fee_rate_in_the_denominator(decimal net, decimal expected)
    {
        // amount(net) = (net + fixedFees * vatScaler) / (1 - percentageFees * vatScaler), ceiling-rounded.
        // For net = 100: (100 + 6.84) / (1 - 0.0285) = 109.9743 -> 109.98 -> ceil(109.99) = 110.
        _Default.AddFeesForNet(net).Should().Be(expected);
    }

    [Fact]
    public void should_cover_the_net_when_vat_is_zero_because_only_the_percentage_rate_drives_the_denominator()
    {
        // With no VAT and no fixed fee the inverse collapses to net / (1 - percentageFees); a denominator
        // built from the VAT rate would degenerate to 1 here and under-charge.
        var calculator = new PaymobCashInFeesCalculator(
            fixedFeesPerTransaction: 0,
            percentageFeesPerTransaction: 0.025m,
            vatPercentOnFees: 0
        );

        var amount = calculator.AddFeesForNet(100m);

        amount.Should().Be(103m);
        (amount - calculator.CalculateDeductFees(amount)).Should().BeGreaterThanOrEqualTo(100m);
    }

    [Fact]
    public void should_return_only_the_fee_portion_of_the_gross_up()
    {
        _Default.CalcFeesForNet(100m).Should().Be(_Default.AddFeesForNet(100m) - 100m);
        _Default.CalcFeesForNet(100m).Should().Be(10m);
    }

    [Fact]
    public void should_always_cover_the_requested_net_by_at_most_one_unit_across_rates_and_amounts()
    {
        decimal[] fixedFees = [0m, 3m, 6m, 15m];
        decimal[] percentageFees = [0.005m, 0.0175m, 0.025m, 0.04m, 0.06m];
        decimal[] vatRates = [0m, 0.05m, 0.14m, 0.20m];

        // Deterministic spread: a fixed seed keeps any failure reproducible.
        var randomizer = new Randomizer(localSeed: 20260802);

        var nets = new List<decimal> { 0.01m, 0.5m, 1m, 9.99m, 100m, 999.99m, 25_000m };
        nets.AddRange(
            Enumerable
                .Range(0, 40)
                .Select(_ => decimal.Round(randomizer.Decimal(0.01m, 50_000m), 2, MidpointRounding.ToEven))
        );

        foreach (var fixedFee in fixedFees)
        {
            foreach (var percentageFee in percentageFees)
            {
                foreach (var vatRate in vatRates)
                {
                    var calculator = new PaymobCashInFeesCalculator(fixedFee, percentageFee, vatRate);

                    foreach (var net in nets)
                    {
                        var amount = calculator.AddFeesForNet(net);
                        var received = amount - calculator.CalculateDeductFees(amount);

                        received
                            .Should()
                            .BeGreaterThanOrEqualTo(
                                net,
                                "gross-up for net {0} at ({1}, {2}, {3}) returned {4}",
                                net,
                                fixedFee,
                                percentageFee,
                                vatRate,
                                amount
                            );

                        // The ceiling to the next whole unit is the only source of over-collection.
                        received
                            .Should()
                            .BeLessThanOrEqualTo(
                                net + 1m,
                                "gross-up for net {0} at ({1}, {2}, {3}) returned {4}",
                                net,
                                fixedFee,
                                percentageFee,
                                vatRate,
                                amount
                            );
                    }
                }
            }
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void should_reject_non_positive_input(decimal value)
    {
        FluentActions.Invoking(() => _Default.AddFeesForNet(value)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => _Default.CalcFeesForNet(value)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => _Default.CalculateDeductFees(value)).Should().Throw<ArgumentOutOfRangeException>();
    }
}
