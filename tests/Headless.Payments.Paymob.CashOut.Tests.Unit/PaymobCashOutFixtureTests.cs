// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

// Smoke test verifying the shared fixture wires up cleanly. Broker/authenticator
// coverage lives in PaymobCashOutBrokerTests and PaymobCashOutAuthenticatorTests.
public sealed class PaymobCashOutFixtureTests(PaymobCashOutFixture fixture) : IClassFixture<PaymobCashOutFixture>
{
    [Fact]
    public void fixture_wires_up_wiremock_server_and_options()
    {
        fixture.Server.IsStarted.Should().BeTrue();
        fixture.Server.Urls.Should().NotBeEmpty();
        fixture.CashOutOptions.ApiBaseUrl.Should().Be(fixture.Server.Urls[0]);
        fixture.OptionsAccessor.CurrentValue.Should().BeSameAs(fixture.CashOutOptions);
        fixture.HttpClient.Should().NotBeNull();
        fixture.TimeProvider.Should().BeSameAs(TimeProvider.System);
    }
}
