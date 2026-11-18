// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Payments.Paymob;
using Headless.Payments.Paymob.CashIn;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Tests;

public sealed class PaymobCashInServiceRefundTests : TestBase
{
    private readonly IPaymobCashInBroker _broker = Substitute.For<IPaymobCashInBroker>();
    private readonly List<string> _refundedCents = [];

    public PaymobCashInServiceRefundTests()
    {
        _broker
            .RefundTransactionAsync(
                Arg.Do<CashInRefundRequest>(r => _refundedCents.Add(r.AmountCents)),
                Arg.Any<CancellationToken>()
            )
            .Returns((CashInCallbackTransaction?)null);
    }

    [Fact]
    public async Task should_never_refund_more_than_the_charge_when_three_partial_refunds_split_it()
    {
        // given: a 100.00 charge (10000 cents) refunded in three equal thirds
        await using var provider = _Provider();
        var sut = provider.GetRequiredService<IPaymobCashInService>();

        // when
        for (var i = 0; i < 3; i++)
        {
            await sut.RefundAsync(new PaymobRefundRequest("tx-1", 100m / 3), AbortToken);
        }

        // then: each third rounds down to 3333 cents, so the refunds total 9999 of the 10000 charged
        _refundedCents.Should().Equal("3333", "3333", "3333");
    }

    [Theory]
    [InlineData("10", "1000")]
    [InlineData("10.005", "1000")]
    [InlineData("0.01", "1")]
    public async Task should_round_a_fraction_of_a_cent_down_when_refunding(string amount, string expectedCents)
    {
        // given
        await using var provider = _Provider();
        var sut = provider.GetRequiredService<IPaymobCashInService>();

        // when
        await sut.RefundAsync(
            new PaymobRefundRequest("tx-1", decimal.Parse(amount, CultureInfo.InvariantCulture)),
            AbortToken
        );

        // then
        _refundedCents.Should().Equal(expectedCents);
    }

    [Fact]
    public async Task should_refuse_a_refund_under_one_cent_before_calling_paymob()
    {
        // given
        await using var provider = _Provider();
        var sut = provider.GetRequiredService<IPaymobCashInService>();

        // when
        var act = async () => await sut.RefundAsync(new PaymobRefundRequest("tx-1", 0.004m), AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
        _refundedCents.Should().BeEmpty();
    }

    private ServiceProvider _Provider()
    {
        return new ServiceCollection().AddLogging().AddSingleton(_broker).AddPaymobServices().BuildServiceProvider();
    }
}
