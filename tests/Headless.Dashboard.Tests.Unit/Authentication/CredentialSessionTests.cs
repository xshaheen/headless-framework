// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Dashboard;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Time.Testing;
using Tests.Modules.Fixtures;

namespace Tests.Authentication;

/// <summary>
/// Basic and API-key sign-in to the dashboard shell: the credential is posted once for the session cookie, the cookie
/// alone then opens the dashboard, and changing the configured credential ends every session issued under the old one.
/// </summary>
public sealed class CredentialSessionTests : TestBase
{
    private static readonly string _OpsBasic = "Basic " + "ops:secret".ToBase64();

    private readonly FakeTimeProvider _time = ShellTestHost.NewTime();

    [Fact]
    public async Task should_issue_a_session_cookie_that_alone_reports_signed_in_when_the_basic_credential_is_correct()
    {
        await using var app = await _StartAsync(dashboard => dashboard.WithBasicAuth("ops", "secret"));
        using var client = ShellTestHost.Client(app);

        using var exchange = await ShellTestHost.ExchangeAsync(client, _OpsBasic, AbortToken);

        exchange.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var setCookie = ShellTestHost.SetCookie(exchange, ShellTestHost.DefaultCookieName);
        setCookie.Should().NotBeNull();
        setCookie!.HttpOnly.Should().BeTrue();
        setCookie.Secure.Should().BeTrue();

        var info = await ShellTestHost.InfoAsync(client, ShellTestHost.Cookie(setCookie), AbortToken);

        info.GetProperty("mode").GetString().Should().Be("basic");
        info.GetProperty("authenticated").GetBoolean().Should().BeTrue();
        info.GetProperty("name").GetString().Should().Be("ops");
        info.GetProperty("modules").GetProperty("diag").GetProperty("canOperate").GetBoolean().Should().BeTrue();
        (await ShellTestHost.InfoAsync(client, cookie: null, AbortToken))
            .GetProperty("authenticated")
            .GetBoolean()
            .Should()
            .BeFalse();
    }

    [Fact]
    public async Task should_open_a_module_endpoint_with_only_the_session_cookie()
    {
        await using var app = await _StartAsync(dashboard => dashboard.WithBasicAuth("ops", "secret"));
        using var client = ShellTestHost.Client(app);
        var cookie = await ShellTestHost.SignInAsync(client, _OpsBasic, AbortToken);

        using var response = await ShellTestHost.GetDiagAsync(client, cookie, AbortToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(AbortToken)).Should().Be("ops");
    }

    [Fact]
    public async Task should_answer_401_without_a_cookie_when_the_basic_credential_is_wrong()
    {
        await using var app = await _StartAsync(dashboard => dashboard.WithBasicAuth("ops", "secret"));
        using var client = ShellTestHost.Client(app);

        using var exchange = await ShellTestHost.ExchangeAsync(client, "Basic " + "ops:wrong".ToBase64(), AbortToken);

        exchange.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        ShellTestHost.SetCookie(exchange, ShellTestHost.DefaultCookieName).Should().BeNull();
    }

    [Fact]
    public async Task should_refuse_an_api_key_session_at_its_next_request_when_the_configured_key_changes()
    {
        // Two instances share one key ring, as a restarted or scaled-out host does, so each can read the other's cookie.
        var keyRing = new EphemeralDataProtectionProvider();
        await using var issuing = await _StartAsync(dashboard => dashboard.WithApiKey("k1"), keyRing);
        await using var rotated = await _StartAsync(dashboard => dashboard.WithApiKey("k2"), keyRing);
        await using var unchanged = await _StartAsync(dashboard => dashboard.WithApiKey("k1"), keyRing);
        using var issuingClient = ShellTestHost.Client(issuing);
        using var rotatedClient = ShellTestHost.Client(rotated);
        using var unchangedClient = ShellTestHost.Client(unchanged);
        var cookie = await ShellTestHost.SignInAsync(issuingClient, "Bearer k1", AbortToken);

        using var refused = await ShellTestHost.GetDiagAsync(rotatedClient, cookie, AbortToken);
        using var accepted = await ShellTestHost.GetDiagAsync(unchangedClient, cookie, AbortToken);

        refused.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        ShellTestHost.IsDeletion(ShellTestHost.SetCookie(refused, ShellTestHost.DefaultCookieName)).Should().BeTrue();
        accepted.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task should_refuse_a_basic_session_at_its_next_request_when_the_configured_password_changes()
    {
        var keyRing = new EphemeralDataProtectionProvider();
        await using var issuing = await _StartAsync(dashboard => dashboard.WithBasicAuth("ops", "secret"), keyRing);
        await using var rotated = await _StartAsync(dashboard => dashboard.WithBasicAuth("ops", "changed"), keyRing);
        using var issuingClient = ShellTestHost.Client(issuing);
        using var rotatedClient = ShellTestHost.Client(rotated);
        var cookie = await ShellTestHost.SignInAsync(issuingClient, _OpsBasic, AbortToken);

        using var refused = await ShellTestHost.GetDiagAsync(rotatedClient, cookie, AbortToken);
        var info = await ShellTestHost.InfoAsync(rotatedClient, cookie, AbortToken);

        refused.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        info.GetProperty("authenticated").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task should_refuse_a_credential_session_when_the_host_switches_to_host_authentication()
    {
        var keyRing = new EphemeralDataProtectionProvider();
        await using var issuing = await _StartAsync(dashboard => dashboard.WithApiKey("k1"), keyRing);
        await using var switched = await _StartAsync(dashboard => dashboard.WithHostAuthentication(), keyRing);
        using var issuingClient = ShellTestHost.Client(issuing);
        using var switchedClient = ShellTestHost.Client(switched);
        var cookie = await ShellTestHost.SignInAsync(issuingClient, "Bearer k1", AbortToken);

        using var refused = await ShellTestHost.GetDiagAsync(switchedClient, cookie, AbortToken);

        refused.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("Bearer k1", HttpStatusCode.OK)]
    [InlineData("Bearer k2", HttpStatusCode.Unauthorized)]
    public async Task should_authenticate_a_script_that_sends_the_api_key_header_without_a_cookie(
        string authorization,
        HttpStatusCode expected
    )
    {
        await using var app = await _StartAsync(dashboard => dashboard.WithApiKey("k1"));
        using var client = ShellTestHost.Client(app);

        using var response = await ShellTestHost.GetDiagAsync(client, cookie: null, AbortToken, authorization);

        response.StatusCode.Should().Be(expected);
        ShellTestHost.SetCookie(response, ShellTestHost.DefaultCookieName).Should().BeNull();
    }

    [Fact]
    public async Task should_delete_the_session_cookie_on_sign_out_under_basic_authentication()
    {
        await using var app = await _StartAsync(dashboard => dashboard.WithBasicAuth("ops", "secret"));
        using var client = ShellTestHost.Client(app);
        var cookie = await ShellTestHost.SignInAsync(client, _OpsBasic, AbortToken);

        using var signOut = await ShellTestHost.SendAsync(
            client,
            HttpMethod.Post,
            "/headless/api/auth/sign-out",
            AbortToken,
            cookie: cookie
        );

        signOut.StatusCode.Should().Be(HttpStatusCode.NoContent);
        ShellTestHost.IsDeletion(ShellTestHost.SetCookie(signOut, ShellTestHost.DefaultCookieName)).Should().BeTrue();
    }

    [Fact]
    public async Task should_answer_404_to_a_session_exchange_when_the_dashboard_runs_without_authentication()
    {
        await using var app = await _StartAsync(dashboard => dashboard.WithNoAuth());
        using var client = ShellTestHost.Client(app);

        using var exchange = await ShellTestHost.ExchangeAsync(client, _OpsBasic, AbortToken);

        exchange.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private Task<WebApplication> _StartAsync(
        Action<HeadlessDashboardSetupBuilder> chooseMode,
        IDataProtectionProvider? keyRing = null
    )
    {
        return ShellTestHost.StartAsync(
            dashboard =>
            {
                chooseMode(dashboard);
                dashboard.AddModule<DiagEndpointModule, DiagModuleOptions>("diag");
            },
            new ShellTestHostOptions { Time = _time, DataProtection = keyRing },
            AbortToken
        );
    }
}
