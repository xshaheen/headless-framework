// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Payments.Paymob.CashIn;
using Headless.Testing.Tests;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace Tests;

public sealed partial class PaymobCashInBrokerTests : TestBase
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
        var token = CashInTestData.Text(Faker);
        var authenticator = Substitute.For<IPaymobCashInAuthenticator>();
        authenticator.GetAuthenticationTokenAsync(AbortToken).Returns(PaymobCashInResult.Succeeded(token));
        var internalRequest = new CashInCreateOrderInternalRequest(token, request);
        var internalRequestJson = JsonSerializer.Serialize(internalRequest, _IgnoreNullOptions);
        var response = CashInTestData.CreateOrderResponse(Faker);
        var responseJson = JsonSerializer.Serialize(response);

        fixture
            .Server.Given(Request.Create().WithPath("/ecommerce/orders").UsingPost().WithBody(internalRequestJson))
            .RespondWith(Response.Create().WithBody(responseJson));

        // when
        var broker = new PaymobCashInBroker(fixture.HttpClient, authenticator, fixture.OptionsAccessor);
        var result = await broker.CreateOrderAsync(request, AbortToken);

        // then
        _ = await authenticator.Received(1).GetAuthenticationTokenAsync(AbortToken);
        JsonSerializer.Serialize(result.Value).Should().Be(responseJson);
    }

    [Fact]
    public async Task should_return_unknown_when_create_order_answers_5xx()
    {
        // given
        var request = _CreateOrderRequest();
        var authenticator = Substitute.For<IPaymobCashInAuthenticator>();
        var token = CashInTestData.Text(Faker);
        authenticator.GetAuthenticationTokenAsync(AbortToken).Returns(PaymobCashInResult.Succeeded(token));
        var body = CashInTestData.Text(Faker);

        fixture
            .Server.Given(Request.Create().WithPath("/ecommerce/orders").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(HttpStatusCode.InternalServerError).WithBody(body));

        // when
        var broker = new PaymobCashInBroker(fixture.HttpClient, authenticator, fixture.OptionsAccessor);
        var invocation = FluentActions.Awaiting(() => broker.CreateOrderAsync(request, AbortToken));

        // then
        await _ShouldBeUnknownAsync(invocation, HttpStatusCode.InternalServerError, body);
        _ = await authenticator.Received(1).GetAuthenticationTokenAsync(AbortToken);
    }

    private CashInCreateOrderRequest _CreateOrderRequest()
    {
        return CashInCreateOrderRequest.CreateOrder(
            amountCents: Faker.Random.Int(10_000, 100_000),
            currency: Faker.Finance.Currency().Code,
            merchantOrderId: Guid.NewGuid().ToString()
        );
    }
}
