// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Payments.Paymob.CashIn;
using Headless.Payments.Paymob.CashIn.Models.Intentions;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace Tests;

public sealed partial class PaymobCashInBrokerTests
{
    [Fact]
    public async Task should_serialize_readonly_collections_when_create_intention()
    {
        // given
        var config = fixture.CashInOptions with
        {
            CreateIntentionUrl = fixture.Server.Urls[0] + "/v1/intention/",
        };
        fixture.OptionsAccessor.CurrentValue.Returns(config);

        var (authenticator, _) = _SetupGentAuthenticationToken();

        var request = new CashInCreateIntentionRequest
        {
            Amount = 15_000,
            Currency = "EGP",
            PaymentMethods = [4534567],
            BillingData = new CashInCreateIntentionRequestBillingData
            {
                FirstName = "Alice",
                LastName = "Smith",
                PhoneNumber = "+201000000000",
                Email = "alice@example.com",
            },
            Items =
            [
                new CashInCreateIntentionRequestItem
                {
                    Name = "item-1",
                    Amount = 15_000,
                    Quantity = 1,
                },
            ],
            Extras = new Dictionary<string, object>(StringComparer.Ordinal) { ["merchant_note"] = "vip" },
        };

        var response = fixture.AutoFixture.Create<CashInCreateIntentionResponse>();
        var responseJson = JsonSerializer.Serialize(response);

        fixture
            .Server.Given(Request.Create().WithPath("/v1/intention/").UsingPost())
            .RespondWith(Response.Create().WithBody(responseJson));

        // when
        var broker = new PaymobCashInBroker(fixture.HttpClient, authenticator, fixture.OptionsAccessor);
        var result = await broker.CreateIntentionAsync(request, AbortToken);

        // then — the read-only List/Dictionary properties still serialize into the outbound request body
        result.Should().NotBeNull();
        var sentBody = fixture.Server.LogEntries[^1].RequestMessage?.Body;
        sentBody.Should().NotBeNull();
        sentBody.Should().Contain("payment_methods").And.Contain("4534567");
        sentBody.Should().Contain("items").And.Contain("item-1");
        sentBody.Should().Contain("extras").And.Contain("merchant_note");
    }
}
