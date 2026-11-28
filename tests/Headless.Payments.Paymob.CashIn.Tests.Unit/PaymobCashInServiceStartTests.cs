// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Text.Json.Nodes;
using Headless.Payments.Paymob;
using Headless.Payments.Paymob.CashIn;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Tests;

// Pins each cash-in start flow: the steps before the pay call move no money, so they reject; once Paymob accepted
// a payment, a missing piece of its answer is unknown, because the payment exists.
public sealed class PaymobCashInServiceStartTests : TestBase
{
    private readonly IPaymobCashInBroker _broker = Substitute.For<IPaymobCashInBroker>();

    protected override Faker Faker { get; set; } = CashInTestData.CreateFaker();

    [Fact]
    public async Task should_return_the_iframe_when_a_card_payment_starts()
    {
        // given
        var orderId = _SetupOrder();
        var key = _SetupPaymentKey();
        _broker.CreateIframeSrc("iframe-1", key).Returns("https://iframe/1");

        // when
        await using var provider = _Provider();
        var result = await provider
            .GetRequiredService<IPaymobCashInService>()
            .StartAsync(new PaymobCardCashInRequest(100, _Customer(), 1, "iframe-1"), AbortToken);

        // then
        result.IsSucceeded.Should().BeTrue();
        result.Data!.IframeSrc.Should().Be("https://iframe/1");
        result.Data.PaymentKey.Should().Be(key);
        result.Data.OrderId.Should().Be(orderId);
    }

    [Theory]
    [InlineData(PaymobCashInOutcome.Rejected)]
    [InlineData(PaymobCashInOutcome.Unknown)]
    public async Task should_reject_the_card_payment_when_the_payment_key_is_not_issued(PaymobCashInOutcome outcome)
    {
        // given - a payment key moves no money, so even a lost answer leaves nothing to resolve
        _SetupOrder();
        var error = new PaymobCashInResultError("key", HttpStatusCode.ServiceUnavailable);
        _broker
            .RequestPaymentKeyAsync(Arg.Any<CashInPaymentKeyRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                outcome is PaymobCashInOutcome.Rejected
                    ? PaymobCashInResult.Rejected<CashInPaymentKeyResponse>(error)
                    : PaymobCashInResult.Unknown<CashInPaymentKeyResponse>(error)
            );

        // when
        await using var provider = _Provider();
        var result = await provider
            .GetRequiredService<IPaymobCashInService>()
            .StartAsync(new PaymobCardCashInRequest(100, _Customer(), 1, "iframe-1"), AbortToken);

        // then
        result.IsRejected.Should().BeTrue();
        result.Error!.Code.Should().Be("cash_in:provider_connection_failed");
    }

    [Fact]
    public async Task should_reject_the_card_payment_when_paymob_issues_an_empty_payment_key()
    {
        // given
        _SetupOrder();
        _broker
            .RequestPaymentKeyAsync(Arg.Any<CashInPaymentKeyRequest>(), Arg.Any<CancellationToken>())
            .Returns(PaymobCashInResult.Succeeded(new CashInPaymentKeyResponse { PaymentKey = " " }));

        // when
        await using var provider = _Provider();
        var result = await provider
            .GetRequiredService<IPaymobCashInService>()
            .StartAsync(new PaymobCardCashInRequest(100, _Customer(), 1, "iframe-1"), AbortToken);

        // then
        result.IsRejected.Should().BeTrue();
    }

    [Fact]
    public async Task should_return_the_redirect_when_a_wallet_payment_starts()
    {
        // given
        var orderId = _SetupOrder();
        _SetupPaymentKey();
        var pay = CashInTestData.WalletPayResponse(Faker);
        _broker
            .CreateWalletPayAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(PaymobCashInResult.Succeeded(pay));

        // when
        await using var provider = _Provider();
        var result = await provider.GetRequiredService<IPaymobCashInService>().StartAsync(_WalletRequest(), AbortToken);

        // then
        result.IsSucceeded.Should().BeTrue();
        result.Data!.RedirectUrl.Should().Be(pay.RedirectUrl);
        result.Data.OrderId.Should().Be(orderId);
    }

    [Fact]
    public async Task should_return_unknown_with_the_order_when_paymob_accepts_a_wallet_payment_without_a_redirect()
    {
        // given
        var orderId = _SetupOrder();
        _SetupPaymentKey();
        var pay = _With(CashInTestData.WalletPayResponse(Faker), "redirect_url", value: "");
        _broker
            .CreateWalletPayAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(PaymobCashInResult.Succeeded(pay));

        // when
        await using var provider = _Provider();
        var result = await provider.GetRequiredService<IPaymobCashInService>().StartAsync(_WalletRequest(), AbortToken);

        // then - the payment exists even though the customer has no page to finish it on
        result.IsUnknown.Should().BeTrue();
        result.OrderId.Should().Be(orderId);
    }

    [Fact]
    public async Task should_return_the_bill_reference_when_a_kiosk_payment_starts()
    {
        // given
        var orderId = _SetupOrder();
        _SetupPaymentKey();
        var pay = CashInTestData.KioskPayResponse(Faker);
        _broker
            .CreateKioskPayAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(PaymobCashInResult.Succeeded(pay));

        // when
        await using var provider = _Provider();
        var result = await provider.GetRequiredService<IPaymobCashInService>().StartAsync(_KioskRequest(), AbortToken);

        // then
        result.IsSucceeded.Should().BeTrue();
        result.Data!.BillingReference.Should().Be(pay.Data.BillReference.ToString(CultureInfo.InvariantCulture));
        result.Data.OrderId.Should().Be(orderId);
    }

    [Fact]
    public async Task should_return_unknown_with_the_order_when_paymob_accepts_a_kiosk_payment_without_a_bill()
    {
        // given
        var orderId = _SetupOrder();
        _SetupPaymentKey();
        var pay = _With(CashInTestData.KioskPayResponse(Faker), "data", value: null);
        _broker
            .CreateKioskPayAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(PaymobCashInResult.Succeeded(pay));

        // when
        await using var provider = _Provider();
        var result = await provider.GetRequiredService<IPaymobCashInService>().StartAsync(_KioskRequest(), AbortToken);

        // then - Paymob may have issued a bill the customer can still pay
        result.IsUnknown.Should().BeTrue();
        result.OrderId.Should().Be(orderId);
    }

    [Fact]
    public async Task should_return_unknown_with_the_order_when_the_kiosk_pay_outcome_is_unknown()
    {
        // given
        var orderId = _SetupOrder();
        _SetupPaymentKey();
        _broker
            .CreateKioskPayAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(PaymobCashInResult.Unknown<CashInKioskPayResponse>(new PaymobCashInResultError("timeout")));

        // when
        await using var provider = _Provider();
        var result = await provider.GetRequiredService<IPaymobCashInService>().StartAsync(_KioskRequest(), AbortToken);

        // then
        result.IsUnknown.Should().BeTrue();
        result.Error!.Code.Should().Be("cash_in:outcome_unknown");
        result.OrderId.Should().Be(orderId);
    }

    [Fact]
    public async Task should_return_the_transaction_when_a_saved_token_payment_starts()
    {
        // given
        var orderId = _SetupOrder();
        _SetupPaymentKey();
        var pay = CashInTestData.SavedTokenPayResponse(Faker);
        _broker
            .CreateSavedTokenPayAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(PaymobCashInResult.Succeeded(pay));

        // when
        await using var provider = _Provider();
        var result = await provider
            .GetRequiredService<IPaymobCashInService>()
            .StartAsync(_SavedTokenRequest(), AbortToken);

        // then
        result.IsSucceeded.Should().BeTrue();
        result.Data!.TransactionId.Should().Be(pay.Id);
        result.Data.OrderId.Should().Be(orderId);
    }

    [Fact]
    public async Task should_return_unknown_with_the_order_when_the_saved_token_pay_outcome_is_unknown()
    {
        // given
        var orderId = _SetupOrder();
        _SetupPaymentKey();
        _broker
            .CreateSavedTokenPayAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(
                PaymobCashInResult.Unknown<CashInSavedTokenPayResponse>(
                    new PaymobCashInResultError("no response", HttpStatusCode.InternalServerError)
                )
            );

        // when
        await using var provider = _Provider();
        var result = await provider
            .GetRequiredService<IPaymobCashInService>()
            .StartAsync(_SavedTokenRequest(), AbortToken);

        // then - a resent saved-token charge can take the money twice
        result.IsUnknown.Should().BeTrue();
        result.OrderId.Should().Be(orderId);
    }

    [Fact]
    public async Task should_return_the_intention_when_paymob_creates_it()
    {
        // given
        var intention = CashInTestData.CreateIntentionResponse(Faker);
        _broker
            .CreateIntentionAsync(Arg.Any<CashInCreateIntentionRequest>(), Arg.Any<CancellationToken>())
            .Returns(PaymobCashInResult.Succeeded(intention));

        // when
        await using var provider = _Provider();
        var result = await provider.GetRequiredService<IPaymobCashInService>().StartAsync(_Intention(), AbortToken);

        // then
        result.IsSucceeded.Should().BeTrue();
        result.Data!.Should().BeSameAs(intention);
    }

    [Fact]
    public async Task should_reject_an_intention_paymob_did_not_confirm()
    {
        // given - an intention moves no money until the customer pays, so a lost answer leaves nothing to resolve
        _broker
            .CreateIntentionAsync(Arg.Any<CashInCreateIntentionRequest>(), Arg.Any<CancellationToken>())
            .Returns(PaymobCashInResult.Unknown<CashInCreateIntentionResponse>(new PaymobCashInResultError("timeout")));

        // when
        await using var provider = _Provider();
        var result = await provider.GetRequiredService<IPaymobCashInService>().StartAsync(_Intention(), AbortToken);

        // then
        result.IsRejected.Should().BeTrue();
    }

    [Fact]
    public async Task should_return_unknown_outcome_when_the_void_outcome_is_unknown()
    {
        // given
        _broker
            .VoidTransactionAsync(Arg.Any<CashInVoidRefundRequest>(), Arg.Any<CancellationToken>())
            .Returns(PaymobCashInResult.Unknown<CashInCallbackTransaction>(new PaymobCashInResultError("timeout")));

        // when
        await using var provider = _Provider();
        var result = await provider
            .GetRequiredService<IPaymobCashInService>()
            .VoidAsync(new PaymobVoidRequest("tx-1"), AbortToken);

        // then
        result.IsUnknown.Should().BeTrue();
        result.Error!.Code.Should().Be("cash_in:outcome_unknown");
    }

    private string _SetupOrder()
    {
        var order = CashInTestData.CreateOrderResponse(Faker);
        _broker
            .CreateOrderAsync(Arg.Any<CashInCreateOrderRequest>(), Arg.Any<CancellationToken>())
            .Returns(PaymobCashInResult.Succeeded(order));

        return order.Id.ToString(CultureInfo.InvariantCulture);
    }

    private string _SetupPaymentKey()
    {
        var key = CashInTestData.PaymentKeyResponse(Faker);
        _broker
            .RequestPaymentKeyAsync(Arg.Any<CashInPaymentKeyRequest>(), Arg.Any<CancellationToken>())
            .Returns(PaymobCashInResult.Succeeded(key));

        return key.PaymentKey;
    }

    private static T _With<T>(T response, string property, string? value)
    {
        var node = JsonSerializer.SerializeToNode(response)!;
        node[property] = value is null ? null : JsonValue.Create(value);

        return node.Deserialize<T>()!;
    }

    private static PaymobCashInCustomerData _Customer()
    {
        return new("First", "Last", "01012345678", "first@example.com");
    }

    private static PaymobWalletCashInRequest _WalletRequest()
    {
        return new(100, "01012345678", _Customer(), WalletIntegrationId: 1);
    }

    private static PaymobKioskCashInRequest _KioskRequest()
    {
        return new(100, _Customer(), KioskIntegrationId: 1);
    }

    private static PaymobCardSavedTokenCashInRequest _SavedTokenRequest()
    {
        return new(100, _Customer(), SavedTokenIntegrationId: 1, CardToken: "card-token");
    }

    private static CashInCreateIntentionRequest _Intention()
    {
        return new()
        {
            Amount = 1000,
            Currency = "EGP",
            PaymentMethods = [1],
            Items = [],
            BillingData = new()
            {
                FirstName = "First",
                LastName = "Last",
                Email = "first@example.com",
                PhoneNumber = "01012345678",
            },
        };
    }

    private ServiceProvider _Provider()
    {
        return new ServiceCollection().AddLogging().AddSingleton(_broker).AddPaymobServices().BuildServiceProvider();
    }
}
