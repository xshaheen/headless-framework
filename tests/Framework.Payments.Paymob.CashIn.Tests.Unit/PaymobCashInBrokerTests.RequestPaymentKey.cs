// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Framework.Payments.Paymob.CashIn;
using Framework.Payments.Paymob.CashIn.Models.Payment;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace Tests;

public partial class PaymobCashInBrokerTests
{
    public static Fixture AutoFixture { get; } = new();

    public static readonly TheoryData<CashInPaymentKeyRequest> RequestPaymentKeyData =
    [
        _GetPaymentKeyRequest(expiration: null),
        _GetPaymentKeyRequest(expiration: AutoFixture.Create<int>()),
    ];

    [Theory]
    [MemberData(nameof(RequestPaymentKeyData))]
    public async Task should_make_call_and_return_response_when_request_payment_key(CashInPaymentKeyRequest request)
    {
        // given
        var (authenticator, token) = _SetupGentAuthenticationToken();
        var expiration = fixture.AutoFixture.Create<int>();
        var config = fixture.CashInOptions with { ExpirationPeriod = expiration };
        fixture.OptionsAccessor.CurrentValue.Returns(config);
        var internalRequest = new CashInPaymentKeyInternalRequest(request, token, expiration);
        var internalRequestJson = JsonSerializer.Serialize(internalRequest);
        var response = fixture.AutoFixture.Create<CashInPaymentKeyResponse>();
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
        JsonSerializer.Serialize(result).Should().Be(responseJson);
        _ = await authenticator.Received(1).GetAuthenticationTokenAsync(AbortToken);
    }

    [Fact]
    public async Task should_throw_http_request_exception_when_request_payment_key_not_success()
    {
        // given
        var request = fixture.AutoFixture.Create<CashInPaymentKeyRequest>();
        var (authenticator, _) = _SetupGentAuthenticationToken();
        var body = fixture.AutoFixture.Create<string>();

        fixture
            .Server.Given(Request.Create().WithPath("/acceptance/payment_keys").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(HttpStatusCode.InternalServerError).WithBody(body));

        // when
        var broker = new PaymobCashInBroker(fixture.HttpClient, authenticator, fixture.OptionsAccessor);
        var invocation = FluentActions.Awaiting(() => broker.RequestPaymentKeyAsync(request, AbortToken));

        // then
        await _ShouldThrowPaymobRequestExceptionAsync(invocation, HttpStatusCode.InternalServerError, body);
        _ = await authenticator.Received(1).GetAuthenticationTokenAsync(AbortToken);
    }

    private static CashInPaymentKeyRequest _GetPaymentKeyRequest(int? expiration)
    {
        return AutoFixture
            .Build<CashInPaymentKeyRequest>()
            .FromFactory(
                () =>
                    new CashInPaymentKeyRequest(
                        integrationId: AutoFixture.Create<int>(),
                        orderId: AutoFixture.Create<int>(),
                        billingData: AutoFixture.Create<CashInBillingData>(),
                        amountCents: AutoFixture.Create<int>(),
                        currency: AutoFixture.Create<string>(),
                        lockOrderWhenPaid: AutoFixture.Create<bool>(),
                        expiration: expiration
                    )
            )
            .Create();
    }
}
