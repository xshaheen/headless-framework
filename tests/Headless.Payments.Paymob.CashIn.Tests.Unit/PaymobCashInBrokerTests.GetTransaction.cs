// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Payments.Paymob.CashIn;
using Headless.Testing.Tests;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace Tests;

public sealed partial class PaymobCashInBrokerTests : TestBase
{
    [Fact]
    public async Task should_capture_unknown_response_fields_in_extension_data_when_get_transaction()
    {
        // given - Paymob adds response fields over time; unmapped ones must land in ExtensionData
        var (authenticator, _) = _SetupGentAuthenticationToken();
        const string transactionId = "192";
        const string responseJson = """
            {
              "id": 192,
              "success": true,
              "amount_cents": 1000,
              "currency": "EGP",
              "api_source": "IFRAME",
              "new_paymob_field": {"nested": 123},
              "order": {
                "id": 7,
                "currency": "EGP",
                "payment_method": "tbc",
                "api_source": "OTHER",
                "merchant": {"id": 1},
                "new_order_field": "x"
              }
            }
            """;

        fixture
            .Server.Given(Request.Create().WithPath($"/acceptance/transactions/{transactionId}").UsingGet())
            .RespondWith(Response.Create().WithBody(responseJson));

        // when
        var broker = new PaymobCashInBroker(fixture.HttpClient, authenticator, fixture.OptionsAccessor);
        var result = await broker.GetTransactionAsync(transactionId, AbortToken);

        // then
        result.Should().NotBeNull();
        result.Id.Should().Be(192);
        result.ExtensionData.Should().NotBeNull().And.ContainKey("new_paymob_field");
        result.Order.Should().NotBeNull();
        result.Order.ExtensionData.Should().ContainKey("new_order_field");
    }
}
