// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Payments.Paymob.CashIn;
using Headless.Payments.Paymob.CashIn.Models.Auth;
using Headless.Testing.Tests;
using Microsoft.Extensions.Time.Testing;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace Tests;

public sealed partial class PaymobCashInAuthenticatorTests : TestBase
{
    [Fact]
    public async Task should_capture_unknown_response_fields_in_extension_data_when_token_requested()
    {
        // given - Paymob responses routinely carry fields not present in the model;
        // they must land in [JsonExtensionData] instead of failing deserialization
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var apiKey = fixture.AutoFixture.Create<string>();
        var token = fixture.AutoFixture.Create<string>();
        var config = fixture.CashInOptions with { ApiKey = apiKey };
        fixture.OptionsAccessor.CurrentValue.Returns(config);
        var request = new CashInAuthenticationTokenRequest { ApiKey = apiKey };
        var requestJson = JsonSerializer.Serialize(request);

        fixture
            .Server.Given(Request.Create().WithPath("/auth/tokens").UsingPost().WithBody(requestJson))
            .RespondWith(Response.Create().WithBody($$$"""{"token":"{{{token}}}","unknown_field":{"nested":123}}"""));

        // when
        using var authenticator = new PaymobCashInAuthenticator(
            fixture.HttpClientFactory,
            timeProvider,
            fixture.OptionsAccessor
        );
        var result = await authenticator.RequestAuthenticationTokenAsync();

        // then
        result.Token.Should().Be(token);
        result.ExtensionData.Should().ContainKey("unknown_field");
    }
}
