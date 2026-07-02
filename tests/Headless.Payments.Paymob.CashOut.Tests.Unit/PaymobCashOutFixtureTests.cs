// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

// Smoke test placeholder. The project ships only a fixture today; this verifies the
// fixture wires up cleanly so the test runner has at least one discoverable test
// (Microsoft Testing Platform reports exit code 8 for empty test assemblies). Replace
// or supplement with broker/authenticator coverage mirroring the CashIn sibling.
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
