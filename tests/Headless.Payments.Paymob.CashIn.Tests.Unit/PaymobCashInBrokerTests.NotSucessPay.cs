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
    public async Task should_return_unknown_when_create_pay_request_answers_5xx(PayRequestKind kind)
    {
        // given
        var body = CashInTestData.Text(Faker);

        fixture.Server.Reset();

        fixture
            .Server.Given(Request.Create().WithPath("/acceptance/payments/pay").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(HttpStatusCode.InternalServerError).WithBody(body));

        // when
        var broker = new PaymobCashInBroker(fixture.HttpClient, null!, fixture.OptionsAccessor);

        Func<Task<PaymobCashInOutcome>> func = kind switch
        {
            PayRequestKind.Wallet => async () =>
                (
                    await broker.CreateWalletPayAsync(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), AbortToken)
                ).Outcome,
            PayRequestKind.Kiosk => async () =>
                (await broker.CreateKioskPayAsync(Guid.NewGuid().ToString(), AbortToken)).Outcome,
            PayRequestKind.CashCollection => async () =>
                (await broker.CreateCashCollectionPayAsync(Guid.NewGuid().ToString(), AbortToken)).Outcome,
            PayRequestKind.SavedToken => async () =>
                (
                    await broker.CreateSavedTokenPayAsync(
                        Guid.NewGuid().ToString(),
                        Guid.NewGuid().ToString(),
                        AbortToken
                    )
                ).Outcome,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        var outcome = await func();

        // then - a 5xx leaves the charge open, so it must not read as a rejection
        outcome.Should().Be(PaymobCashInOutcome.Unknown);
    }
}
