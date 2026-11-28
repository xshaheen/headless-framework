// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Payments.Paymob.CashIn;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace Tests;

public sealed partial class PaymobCashInBrokerTests
{
    public static readonly TheoryData<int?> RequestPaymentKeyData = [default(int?), 3_600];

    [Theory]
    [MemberData(nameof(RequestPaymentKeyData))]
    public async Task should_make_call_and_return_response_when_request_payment_key(int? requestExpiration)
    {
        // given
        var request = _GetPaymentKeyRequest(requestExpiration);
        var (authenticator, token) = _SetupGentAuthenticationToken();
        var expiration = Faker.Random.Int(1, 100_000);
        var config = fixture.CashInOptions with { ExpirationPeriod = expiration };
        fixture.OptionsAccessor.CurrentValue.Returns(config);
        var internalRequest = new CashInPaymentKeyInternalRequest(request, token, expiration);
        var internalRequestJson = JsonSerializer.Serialize(internalRequest);
        var response = CashInTestData.PaymentKeyResponse(Faker);
        var responseJson = JsonSerializer.Serialize(response);

        fixture
            .Server.Given(
                Request.Create().WithPath("/acceptance/payment_keys").UsingPost().WithBody(internalRequestJson)
            )
            .RespondWith(Response.Create().WithBody(responseJson));

        // when
        var broker = new PaymobCashInBroker(fixture.HttpClient, authenticator, fixture.OptionsAccessor);
        var result = await broker.RequestPaymentKeyAsync(request, AbortToken);

        // then
        JsonSerializer.Serialize(result.Value).Should().Be(responseJson);
        _ = await authenticator.Received(1).GetAuthenticationTokenAsync(AbortToken);
    }

    [Fact]
    public async Task should_return_unknown_when_request_payment_key_answers_5xx()
    {
        // given
        var request = _GetPaymentKeyRequest(Faker.Random.Int(1, 100_000));
        var (authenticator, _) = _SetupGentAuthenticationToken();
        var body = CashInTestData.Text(Faker);

        fixture
            .Server.Given(Request.Create().WithPath("/acceptance/payment_keys").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(HttpStatusCode.InternalServerError).WithBody(body));

        // when
        var broker = new PaymobCashInBroker(fixture.HttpClient, authenticator, fixture.OptionsAccessor);
        var invocation = FluentActions.Awaiting(() => broker.RequestPaymentKeyAsync(request, AbortToken));

        // then
        await _ShouldBeUnknownAsync(invocation, HttpStatusCode.InternalServerError, body);
        _ = await authenticator.Received(1).GetAuthenticationTokenAsync(AbortToken);
    }

    private CashInPaymentKeyRequest _GetPaymentKeyRequest(int? expiration)
    {
        return CashInTestData.PaymentKeyRequest(Faker, expiration);
    }
}
