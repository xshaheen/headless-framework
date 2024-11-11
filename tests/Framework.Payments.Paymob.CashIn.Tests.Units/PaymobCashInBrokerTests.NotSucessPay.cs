// Copyright (c) Mahmoud Shaheen, 2021. All rights reserved.

using System.Net;
using Framework.Payments.Paymob.CashIn;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace Tests;

public partial class PaymobCashInBrokerTests
{
    public static readonly TheoryData<Func<PaymobCashInBroker, Task<object>>> PayRequests =
    [
        async broker => await broker.CreateWalletPayAsync(Guid.NewGuid().ToString(), Guid.NewGuid().ToString()),
        async broker => await broker.CreateKioskPayAsync(Guid.NewGuid().ToString()),
        async broker => await broker.CreateCashCollectionPayAsync(Guid.NewGuid().ToString()),
        async broker => await broker.CreateSavedTokenPayAsync(Guid.NewGuid().ToString(), Guid.NewGuid().ToString()),
    ];

    [Theory]
    [MemberData(nameof(PayRequests))]
    public async Task should_throw_http_request_exception_when_create_pay_request_not_success(
        Func<PaymobCashInBroker, Task<object>> func
    )
    {
        // given
        var body = fixture.AutoFixture.Create<string>();

        fixture.Server.Reset();

        fixture
            .Server.Given(Request.Create().WithPath("/acceptance/payments/pay").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(HttpStatusCode.InternalServerError).WithBody(body));

        // when
        var broker = new PaymobCashInBroker(fixture.HttpClient, null!, fixture.OptionsAccessor);
        var invocation = FluentActions.Awaiting(() => func(broker));

        // then
        await _ShouldThrowPaymobRequestException(invocation, HttpStatusCode.InternalServerError, body);
    }
}
