// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Payments.Paymob.CashIn;
using Headless.Payments.Paymob.CashIn.Models;
using Headless.Payments.Paymob.CashIn.Models.Auth;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace Tests;

public sealed partial class PaymobCashInAuthenticatorTests
{
    [Fact]
    public async Task should_make_call_and_return_response_when_send_request()
    {
        // given
        var apiKey = fixture.AutoFixture.Create<string>();
        var config = fixture.CashInOptions with { ApiKey = apiKey };
        fixture.OptionsAccessor.CurrentValue.Returns(config);
        var request = new CashInAuthenticationTokenRequest { ApiKey = apiKey };
        var requestJson = JsonSerializer.Serialize(request);
        var expectedResponse = fixture.AutoFixture.Create<CashInAuthenticationTokenResponse>();
        var expectedResponseJson = JsonSerializer.Serialize(expectedResponse);

        fixture
            .Server.Given(Request.Create().WithPath("/auth/tokens").UsingPost().WithBody(requestJson))
            .RespondWith(Response.Create().WithBody(expectedResponseJson));

        // when
        using var authenticator = new PaymobCashInAuthenticator(
            fixture.HttpClientFactory,
            fixture.TimeProvider,
            fixture.OptionsAccessor
        );
        var result = await authenticator.RequestAuthenticationTokenAsync();

        // then
        JsonSerializer.Serialize(result).Should().BeEquivalentTo(expectedResponseJson);
    }

    [Fact]
    public async Task should_throw_http_request_exception_when_not_success()
    {
        // given
        var apiKey = fixture.AutoFixture.Create<string>();
        var config = fixture.CashInOptions with { ApiKey = apiKey };
        fixture.OptionsAccessor.CurrentValue.Returns(config);
        var request = new CashInAuthenticationTokenRequest { ApiKey = apiKey };
        var requestJson = JsonSerializer.Serialize(request);
        var body = fixture.AutoFixture.Create<string>();

        fixture
            .Server.Given(Request.Create().WithPath("/auth/tokens").UsingPost().WithBody(requestJson))
            .RespondWith(Response.Create().WithStatusCode(HttpStatusCode.InternalServerError).WithBody(body));

        // when
        using var authenticator = new PaymobCashInAuthenticator(
            fixture.HttpClientFactory,
            fixture.TimeProvider,
            fixture.OptionsAccessor
        );

        var invocation = FluentActions.Awaiting(authenticator.RequestAuthenticationTokenAsync);

        // then
        var assertions = await invocation.Should().ThrowAsync<PaymobCashInException>();
        assertions.Which.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        assertions.Which.Message.Should().Be("Paymob Cash In - Http request failed with status code (500).");
        assertions.Which.Body.Should().Be(body);
    }
}
