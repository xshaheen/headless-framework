// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Framework.Payments.Paymob.CashIn;
using Framework.Payments.Paymob.CashIn.Models.Orders;
using Framework.Testing.Tests;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace Tests;

public partial class PaymobCashInBrokerTests : TestBase
{
    private static readonly JsonSerializerOptions _IgnoreNullOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public async Task should_make_call_and_return_response_when_create_order()
    {
        // given
        var request = _CreateOrderRequest();
        var token = fixture.AutoFixture.Create<string>();
        var authenticator = Substitute.For<IPaymobCashInAuthenticator>();
        authenticator.GetAuthenticationTokenAsync(AbortToken).Returns(token);
        var internalRequest = new CashInCreateOrderInternalRequest(token, request);
        var internalRequestJson = JsonSerializer.Serialize(internalRequest, _IgnoreNullOptions);
        var response = fixture.AutoFixture.Create<CashInCreateOrderResponse>();
        var responseJson = JsonSerializer.Serialize(response);

        fixture
            .Server.Given(Request.Create().WithPath("/ecommerce/orders").UsingPost().WithBody(internalRequestJson))
            .RespondWith(Response.Create().WithBody(responseJson));

        // when
        var broker = new PaymobCashInBroker(fixture.HttpClient, authenticator, fixture.OptionsAccessor);
        var result = await broker.CreateOrderAsync(request, AbortToken);

        // then
        _ = await authenticator.Received(1).GetAuthenticationTokenAsync(AbortToken);
        JsonSerializer.Serialize(result).Should().Be(responseJson);
    }

    [Fact]
    public async Task should_throw_http_request_exception_when_create_order_request_not_success()
    {
        // given
        var request = _CreateOrderRequest();
        var authenticator = Substitute.For<IPaymobCashInAuthenticator>();
        var token = fixture.AutoFixture.Create<string>();
        authenticator.GetAuthenticationTokenAsync(AbortToken).Returns(token);
        var body = fixture.AutoFixture.Create<string>();

        fixture
            .Server.Given(Request.Create().WithPath("/ecommerce/orders").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(HttpStatusCode.InternalServerError).WithBody(body));

        // when
        var broker = new PaymobCashInBroker(fixture.HttpClient, authenticator, fixture.OptionsAccessor);
        var invocation = FluentActions.Awaiting(() => broker.CreateOrderAsync(request));

        // then
        await _ShouldThrowPaymobRequestExceptionAsync(invocation, HttpStatusCode.InternalServerError, body);
        _ = await authenticator.Received(1).GetAuthenticationTokenAsync(AbortToken);
    }

    private static CashInCreateOrderRequest _CreateOrderRequest()
    {
        return CashInCreateOrderRequest.CreateOrder(
            amountCents: _Faker.Random.Int(10_000, 100_000),
            currency: _Faker.Finance.Currency().Code,
            merchantOrderId: Guid.NewGuid().ToString()
        );
    }
}
