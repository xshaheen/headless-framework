// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Payments.Paymob.CashIn;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace Tests;

public sealed partial class PaymobCashInAuthenticatorTests
{
    [Fact]
    public async Task should_make_call_and_return_response_when_send_request()
    {
        // given
        var apiKey = CashInTestData.Text(Faker);
        var config = fixture.CashInOptions with { ApiKey = apiKey };
        fixture.OptionsAccessor.CurrentValue.Returns(config);
        var request = new CashInAuthenticationTokenRequest { ApiKey = apiKey };
        var requestJson = JsonSerializer.Serialize(request);
        var expectedResponse = CashInTestData.AuthenticationTokenResponse(Faker);
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
        var result = await authenticator.RequestAuthenticationTokenAsync(AbortToken);

        // then
        JsonSerializer.Serialize(result.Value).Should().BeEquivalentTo(expectedResponseJson);
    }

    [Fact]
    public async Task should_return_unknown_and_cache_nothing_when_token_request_answers_5xx()
    {
        // given
        var apiKey = CashInTestData.Text(Faker);
        var config = fixture.CashInOptions with { ApiKey = apiKey };
        fixture.OptionsAccessor.CurrentValue.Returns(config);
        var request = new CashInAuthenticationTokenRequest { ApiKey = apiKey };
        var requestJson = JsonSerializer.Serialize(request);
        var body = CashInTestData.Text(Faker);

        fixture
            .Server.Given(Request.Create().WithPath("/auth/tokens").UsingPost().WithBody(requestJson))
            .RespondWith(Response.Create().WithStatusCode(HttpStatusCode.InternalServerError).WithBody(body));

        // when
        using var authenticator = new PaymobCashInAuthenticator(
            fixture.HttpClientFactory,
            fixture.TimeProvider,
            fixture.OptionsAccessor
        );

        var result = await authenticator.RequestAuthenticationTokenAsync(AbortToken);
        var cached = await authenticator.GetAuthenticationTokenAsync(AbortToken);

        // then
        result.IsUnknown.Should().BeTrue();
        result.Error!.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        result
            .Error.Message.Should()
            .Be("Paymob Cash In - Paymob answered with status code (500); the request may have been applied.");
        result.Error.Body.Should().Be(body);
        cached.IsSucceeded.Should().BeFalse("a failed token request caches nothing");
    }
}
