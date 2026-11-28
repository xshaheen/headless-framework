// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Payments.Paymob;
using Headless.Payments.Paymob.CashIn;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Tests;

// Pins how the service turns a broker outcome into the caller's result: an unknown money movement must stay
// unknown, a Paymob decline must read as a decline, and a failure before any money moved is a plain rejection.
public sealed class PaymobCashInServiceOutcomeTests : TestBase
{
    private readonly IPaymobCashInBroker _broker = Substitute.For<IPaymobCashInBroker>();

    protected override Faker Faker { get; set; } = CashInTestData.CreateFaker();

    [Fact]
    public async Task should_return_unknown_outcome_when_the_refund_outcome_is_unknown()
    {
        // given
        _broker
            .RefundTransactionAsync(Arg.Any<CashInRefundRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                PaymobCashInResult.Unknown<CashInCallbackTransaction>(
                    new PaymobCashInResultError("no response", HttpStatusCode.GatewayTimeout)
                )
            );

        // when
        await using var provider = _Provider();
        var result = await provider
            .GetRequiredService<IPaymobCashInService>()
            .RefundAsync(new PaymobRefundRequest("tx-1", 10), AbortToken);

        // then
        result.IsUnknown.Should().BeTrue();
        result.Error!.Code.Should().Be("cash_in:outcome_unknown");
        result.OrderId.Should().BeNull("a refund is resolved by the transaction id the caller already holds");
    }

    [Fact]
    public async Task should_return_declined_when_paymob_answers_the_void_with_a_failed_transaction()
    {
        // given
        _broker
            .VoidTransactionAsync(Arg.Any<CashInVoidRefundRequest>(), Arg.Any<CancellationToken>())
            .Returns(PaymobCashInResult.Rejected(new PaymobCashInResultError("declined"), _Transaction()));

        // when
        await using var provider = _Provider();
        var result = await provider
            .GetRequiredService<IPaymobCashInService>()
            .VoidAsync(new PaymobVoidRequest("tx-1"), AbortToken);

        // then
        result.IsRejected.Should().BeTrue();
        result.Error!.Code.Should().Be("cash_in:declined");
    }

    [Fact]
    public async Task should_return_provider_failure_when_paymob_refuses_the_refund_request()
    {
        // given - a 4xx carries no transaction
        _broker
            .RefundTransactionAsync(Arg.Any<CashInRefundRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                PaymobCashInResult.Rejected<CashInCallbackTransaction>(
                    new PaymobCashInResultError("bad request", HttpStatusCode.BadRequest)
                )
            );

        // when
        await using var provider = _Provider();
        var result = await provider
            .GetRequiredService<IPaymobCashInService>()
            .RefundAsync(new PaymobRefundRequest("tx-1", 10), AbortToken);

        // then
        result.IsRejected.Should().BeTrue();
        result.Error!.Code.Should().Be("cash_in:provider_connection_failed");
    }

    [Fact]
    public async Task should_return_the_transaction_when_the_refund_succeeds()
    {
        // given
        var transaction = _Transaction();
        _broker
            .RefundTransactionAsync(Arg.Any<CashInRefundRequest>(), Arg.Any<CancellationToken>())
            .Returns(PaymobCashInResult.Succeeded(transaction));

        // when
        await using var provider = _Provider();
        var result = await provider
            .GetRequiredService<IPaymobCashInService>()
            .RefundAsync(new PaymobRefundRequest("tx-1", 10), AbortToken);

        // then
        result.IsSucceeded.Should().BeTrue();
        result.Data.Should().BeSameAs(transaction);
    }

    [Fact]
    public async Task should_reject_without_paying_when_the_order_outcome_is_unknown()
    {
        // given - no money moves before the pay call, so a lost order is safe to start again
        _broker
            .CreateOrderAsync(Arg.Any<CashInCreateOrderRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                PaymobCashInResult.Unknown<CashInCreateOrderResponse>(
                    new PaymobCashInResultError("no response", HttpStatusCode.BadGateway)
                )
            );

        // when
        await using var provider = _Provider();
        var result = await provider.GetRequiredService<IPaymobCashInService>().StartAsync(_WalletRequest(), AbortToken);

        // then
        result.IsRejected.Should().BeTrue();
        result.Error!.Code.Should().Be("cash_in:provider_connection_failed");
        result.OrderId.Should().BeNull("no order was created");
        await _broker.DidNotReceiveWithAnyArgs().CreateWalletPayAsync(default!, default!, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_return_unknown_outcome_when_the_wallet_pay_outcome_is_unknown()
    {
        // given
        var orderId = _SetupStartedPayment();
        _broker
            .CreateWalletPayAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(
                PaymobCashInResult.Unknown<CashInWalletPayResponse>(
                    new PaymobCashInResultError("timeout", Exception: new TimeoutException())
                )
            );

        // when
        await using var provider = _Provider();
        var result = await provider.GetRequiredService<IPaymobCashInService>().StartAsync(_WalletRequest(), AbortToken);

        // then
        result.IsUnknown.Should().BeTrue();
        result.Error!.Code.Should().Be("cash_in:outcome_unknown");
        result.OrderId.Should().Be(orderId, "the caller resolves the charge by looking this order up");
    }

    [Fact]
    public async Task should_return_declined_when_paymob_declines_the_wallet_pay()
    {
        // given
        var orderId = _SetupStartedPayment();
        _broker
            .CreateWalletPayAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(
                PaymobCashInResult.Rejected(
                    new PaymobCashInResultError("declined"),
                    CashInTestData.WalletPayResponse(Faker)
                )
            );

        // when
        await using var provider = _Provider();
        var result = await provider.GetRequiredService<IPaymobCashInService>().StartAsync(_WalletRequest(), AbortToken);

        // then
        result.IsRejected.Should().BeTrue();
        result.Error!.Code.Should().Be("cash_in:declined");
        result.OrderId.Should().Be(orderId);
    }

    private string _SetupStartedPayment()
    {
        var order = CashInTestData.CreateOrderResponse(Faker);
        _broker
            .CreateOrderAsync(Arg.Any<CashInCreateOrderRequest>(), Arg.Any<CancellationToken>())
            .Returns(PaymobCashInResult.Succeeded(order));
        _broker
            .RequestPaymentKeyAsync(Arg.Any<CashInPaymentKeyRequest>(), Arg.Any<CancellationToken>())
            .Returns(PaymobCashInResult.Succeeded(CashInTestData.PaymentKeyResponse(Faker)));

        return order.Id.ToString(CultureInfo.InvariantCulture);
    }

    private static PaymobWalletCashInRequest _WalletRequest()
    {
        return new PaymobWalletCashInRequest(
            Amount: 100,
            WalletPhoneNumber: "01012345678",
            Customer: new PaymobCashInCustomerData("First", "Last", "01012345678", "first@example.com"),
            WalletIntegrationId: 1
        );
    }

    private static CashInCallbackTransaction _Transaction()
    {
        return JsonSerializer.Deserialize<CashInCallbackTransaction>(CashInTestData.CallbackTransactionJson)!;
    }

    private ServiceProvider _Provider()
    {
        return new ServiceCollection().AddLogging().AddSingleton(_broker).AddPaymobServices().BuildServiceProvider();
    }
}
