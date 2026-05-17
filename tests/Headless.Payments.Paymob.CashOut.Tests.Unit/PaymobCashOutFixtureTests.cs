// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

// Smoke test placeholder. The project ships only a fixture today; this verifies the
// fixture wires up cleanly so the test runner has at least one discoverable test
// (Microsoft Testing Platform reports exit code 8 for empty test assemblies). Replace
// or supplement with broker/authenticator coverage mirroring the CashIn sibling.
public sealed class PaymobCashOutFixtureTests(PaymobCashOutFixture fixture) : IClassFixture<PaymobCashOutFixture>
{
    private readonly PaymobCashOutFixture _fixture = fixture;

    [Fact]
    public void fixture_wires_up_wiremock_server_and_options()
    {
        _fixture.Server.IsStarted.Should().BeTrue();
        _fixture.Server.Urls.Should().NotBeEmpty();
        _fixture.CashOutOptions.ApiBaseUrl.Should().Be(_fixture.Server.Urls[0]);
        _fixture.OptionsAccessor.CurrentValue.Should().BeSameAs(_fixture.CashOutOptions);
        _fixture.HttpClient.Should().NotBeNull();
        _fixture.TimeProvider.Should().BeSameAs(TimeProvider.System);
    }
}
