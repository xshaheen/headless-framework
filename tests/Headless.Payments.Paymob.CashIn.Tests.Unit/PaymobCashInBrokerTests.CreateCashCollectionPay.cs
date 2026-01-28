// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Payments.Paymob.CashIn;
using Headless.Payments.Paymob.CashIn.Models.Payment;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace Tests;

public sealed partial class PaymobCashInBrokerTests
{
    [Fact]
    public async Task should_make_call_and_return_response_when_create_cash_collection_pay_request()
    {
        // given
        var requestPaymentKey = _Faker.Random.String2(10, 50);

        var request = new CashInPayRequest { Source = CashInSource.Cash, PaymentToken = requestPaymentKey };

        var requestJson = JsonSerializer.Serialize(request);
        var response = fixture.AutoFixture.Create<CashInCashCollectionPayResponse>();
        var responseJson = JsonSerializer.Serialize(response);

        fixture
            .Server.Given(Request.Create().WithPath("/acceptance/payments/pay").UsingPost().WithBody(requestJson))
            .RespondWith(Response.Create().WithBody(responseJson));

        // when
        var broker = new PaymobCashInBroker(fixture.HttpClient, null!, fixture.OptionsAccessor);
        var result = await broker.CreateCashCollectionPayAsync(requestPaymentKey, AbortToken);

        // then
        JsonSerializer.Serialize(result).Should().Be(responseJson);
    }
}
