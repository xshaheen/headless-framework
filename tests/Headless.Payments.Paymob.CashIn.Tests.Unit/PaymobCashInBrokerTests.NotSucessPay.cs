// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Payments.Paymob.CashIn;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace Tests;

public sealed partial class PaymobCashInBrokerTests
{
    public enum PayRequestKind
    {
        Wallet,
        Kiosk,
        CashCollection,
        SavedToken,
    }

    // Serializable discriminator (enum) instead of a Func<> so Test Explorer can enumerate each
    // pay-request case as its own named, individually-runnable row (xUnit1046). The kind is mapped
    // to the broker call inside the test.
    public static readonly TheoryData<PayRequestKind> PayRequests =
    [
        PayRequestKind.Wallet,
        PayRequestKind.Kiosk,
        PayRequestKind.CashCollection,
        PayRequestKind.SavedToken,
    ];

    [Theory]
    [MemberData(nameof(PayRequests))]
    public async Task should_throw_http_request_exception_when_create_pay_request_not_success(PayRequestKind kind)
    {
        // given
        var body = fixture.AutoFixture.Create<string>();

        fixture.Server.Reset();

        fixture
            .Server.Given(Request.Create().WithPath("/acceptance/payments/pay").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(HttpStatusCode.InternalServerError).WithBody(body));

        // when
        var broker = new PaymobCashInBroker(fixture.HttpClient, null!, fixture.OptionsAccessor);

        Func<Task<object>> func = kind switch
        {
            PayRequestKind.Wallet => async () =>
                await broker.CreateWalletPayAsync(Guid.NewGuid().ToString(), Guid.NewGuid().ToString()),
            PayRequestKind.Kiosk => async () => await broker.CreateKioskPayAsync(Guid.NewGuid().ToString()),
            PayRequestKind.CashCollection => async () =>
                await broker.CreateCashCollectionPayAsync(Guid.NewGuid().ToString()),
            PayRequestKind.SavedToken => async () =>
                await broker.CreateSavedTokenPayAsync(Guid.NewGuid().ToString(), Guid.NewGuid().ToString()),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        var invocation = FluentActions.Awaiting(func);

        // then
        await _ShouldThrowPaymobRequestExceptionAsync(invocation, HttpStatusCode.InternalServerError, body);
    }
}
