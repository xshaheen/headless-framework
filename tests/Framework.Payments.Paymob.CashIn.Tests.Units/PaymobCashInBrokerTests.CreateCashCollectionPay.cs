// Copyright (c) Mahmoud Shaheen, 2021. All rights reserved.

using System.Text.Json;
using Framework.Payments.Paymob.CashIn;
using Framework.Payments.Paymob.CashIn.Models.Payment;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace Tests;

public partial class PaymobCashInBrokerTests
{
    [Fact]
    public async Task should_make_call_and_return_response_when_create_cash_collection_pay_request()
    {
        // given
        var requestPaymentKey = _Faker.Random.String2(10, 50);

        var request = new CashInPayRequest { Source = CashInSource.Cash, PaymentToken = requestPaymentKey };

        var requestJson = JsonSerializer.Serialize(request);
        var response = _fixture.AutoFixture.Create<CashInCashCollectionPayResponse>();
        var responseJson = JsonSerializer.Serialize(response);

        _fixture
            .Server.Given(Request.Create().WithPath("/acceptance/payments/pay").UsingPost().WithBody(requestJson))
            .RespondWith(Response.Create().WithBody(responseJson));

        // when
        var broker = new PaymobCashInBroker(_fixture.HttpClient, null!, _fixture.Options);
        var result = await broker.CreateCashCollectionPayAsync(requestPaymentKey);

        // then
        JsonSerializer.Serialize(result).Should().Be(responseJson);
    }
}
