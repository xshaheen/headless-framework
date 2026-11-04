// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Headless.Dashboard.Authentication;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Net.Http.Headers;

namespace Tests;

/// <summary>
/// The host-mode dashboard session on a real host: the host's own scheme proves who the operator is once, and the
/// dashboard's own cookie carries every request after that.
/// </summary>
public sealed class DashboardHostSessionTests : TestBase
{
    private const string _Name = "dashboard-a";
    private const string _OtherName = "dashboard-b";
    private const string _CookieName = "__Host-dashboard-a";
    private const string _ViewersPolicy = "Viewers";
    private const string _OperatorsPolicy = "Operators";
    private const string _ChallengeScheme = "Idp";
    private const string _HostCookieScheme = "HostCookie";

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 4, 9, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task exchanges_a_bearer_token_for_a_session_cookie_that_alone_opens_a_view_endpoint()
    {
        await using var app = await _StartAsync();
        using var client = _Client(app);

        using var exchange = await _SendAsync(client, HttpMethod.Post, "/dash/api/auth/session", bearer: "viewer");

        exchange.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var setCookie = _SetCookie(exchange, _CookieName);
        setCookie.Should().NotBeNull();
        setCookie!.HttpOnly.Should().BeTrue();
        setCookie.Secure.Should().BeTrue();
        setCookie.SameSite.Should().Be(Microsoft.Net.Http.Headers.SameSiteMode.Strict);
        setCookie.Path.Value.Should().Be("/");
        setCookie.Domain.HasValue.Should().BeFalse();

        using var stats = await _SendAsync(client, HttpMethod.Get, "/dash/api/stats", cookie: _Cookie(setCookie));

        stats.StatusCode.Should().Be(HttpStatusCode.OK);
        (await stats.Content.ReadAsStringAsync(AbortToken)).Should().Be("viewer");
    }

    [Fact]
    public async Task refuses_a_session_exchange_without_the_requested_with_header()
    {
        await using var app = await _StartAsync();
        using var client = _Client(app);

        using var exchange = await _SendAsync(
            client,
            HttpMethod.Post,
            "/dash/api/auth/session",
            bearer: "viewer",
            requestedWith: false
        );

        exchange.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _SetCookie(exchange, _CookieName).Should().BeNull();
    }

    [Fact]
    public async Task refuses_a_session_to_a_host_user_who_fails_the_view_policy()
    {
        await using var app = await _StartAsync(host => host.ViewPolicy = _ViewersPolicy);
        using var client = _Client(app);

        using var exchange = await _SendAsync(client, HttpMethod.Post, "/dash/api/auth/session", bearer: "guest");

        exchange.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _SetCookie(exchange, _CookieName).Should().BeNull();
    }

    [Fact]
    public async Task signs_in_a_signed_in_host_user_and_redirects_to_a_return_url_under_the_dashboard()
    {
        await using var app = await _StartAsync();
        using var client = _Client(app);

        using var signIn = await _SendAsync(
            client,
            HttpMethod.Get,
            "/dash/auth/sign-in?returnUrl=%2Fdash%2Fjobs%3Fpage%3D2",
            bearer: "viewer",
            requestedWith: false
        );

        signIn.StatusCode.Should().Be(HttpStatusCode.Redirect);
        signIn.Headers.Location!.OriginalString.Should().Be("/dash/jobs?page=2");
        _SetCookie(signIn, _CookieName).Should().NotBeNull();
    }

    [Theory]
    [InlineData("https%3A%2F%2Fevil.example%2Fdash%2F")]
    [InlineData("%2F%2Fevil.example%2Fdash%2F")]
    [InlineData("%2Fother%2Fpage")]
    [InlineData("%2Fdashboard")]
    public async Task redirects_to_the_dashboard_root_when_the_return_url_is_absolute_or_off_base(string returnUrl)
    {
        await using var app = await _StartAsync();
        using var client = _Client(app);

        using var signIn = await _SendAsync(
            client,
            HttpMethod.Get,
            "/dash/auth/sign-in?returnUrl=" + returnUrl,
            bearer: "viewer",
            requestedWith: false
        );

        signIn.StatusCode.Should().Be(HttpStatusCode.Redirect);
        signIn.Headers.Location!.OriginalString.Should().Be("/dash/");
    }

    [Fact]
    public async Task challenges_the_challenge_scheme_once_and_answers_401_on_the_marked_return()
    {
        await using var app = await _StartAsync(host => host.ChallengeScheme = _ChallengeScheme);
        using var client = _Client(app);

        using var first = await _SendAsync(
            client,
            HttpMethod.Get,
            "/dash/auth/sign-in?returnUrl=%2Fdash%2F",
            requestedWith: false
        );

        first.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var location = first.Headers.Location!.OriginalString;
        location.Should().StartWith("/idp/login?redirect=");
        var redirectUri = Uri.UnescapeDataString(location["/idp/login?redirect=".Length..]);
        redirectUri.Should().Be("/dash/auth/sign-in?returnUrl=%2Fdash%2F&attempt=1");

        using var second = await _SendAsync(client, HttpMethod.Get, redirectUri, requestedWith: false);

        second.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        second.Headers.Location.Should().BeNull();
        _SetCookie(second, _CookieName).Should().BeNull();
    }

    [Fact]
    public async Task answers_401_to_sign_in_without_a_host_user_or_a_challenge_scheme()
    {
        await using var app = await _StartAsync();
        using var client = _Client(app);

        using var signIn = await _SendAsync(
            client,
            HttpMethod.Get,
            "/dash/auth/sign-in?returnUrl=%2Fdash%2F",
            requestedWith: false
        );

        signIn.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        signIn.Headers.Location.Should().BeNull();
    }

    [Fact]
    public async Task completes_sign_in_from_a_cookie_challenge_scheme_when_the_default_scheme_is_bearer()
    {
        await using var app = await _StartAsync(host => host.ChallengeScheme = _HostCookieScheme);
        using var client = _Client(app);
        using var hostLogin = await _SendAsync(client, HttpMethod.Post, "/host/login?role=viewer");
        var hostCookie = _Cookie(_SetCookie(hostLogin, ".AspNetCore." + _HostCookieScheme)!);

        using var signIn = await _SendAsync(
            client,
            HttpMethod.Get,
            "/dash/auth/sign-in?returnUrl=%2Fdash%2F&attempt=1",
            cookie: hostCookie,
            requestedWith: false
        );

        signIn.StatusCode.Should().Be(HttpStatusCode.Redirect);
        signIn.Headers.Location!.OriginalString.Should().Be("/dash/");
        var session = _SetCookie(signIn, _CookieName);
        session.Should().NotBeNull();
        (await _NameAsync(client, _Cookie(session!))).Should().Be("viewer");
    }

    [Fact]
    public async Task refuses_an_operator_action_to_a_session_that_passes_view_but_fails_operate()
    {
        await using var app = await _StartAsync(host =>
        {
            host.ViewPolicy = _ViewersPolicy;
            host.OperatePolicy = _OperatorsPolicy;
        });
        using var client = _Client(app);
        var viewer = await _SignInAsync(client, "viewer");
        var operatorCookie = await _SignInAsync(client, "operator");

        (await _SendAsync(client, HttpMethod.Get, "/dash/api/stats", cookie: viewer))
            .StatusCode.Should()
            .Be(HttpStatusCode.OK);
        (await _SendAsync(client, HttpMethod.Post, "/dash/api/requeue", cookie: viewer))
            .StatusCode.Should()
            .Be(HttpStatusCode.Forbidden);
        (await _SendAsync(client, HttpMethod.Post, "/dash/api/requeue", cookie: operatorCookie))
            .StatusCode.Should()
            .Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task lets_a_viewer_operate_when_no_operate_policy_is_set()
    {
        await using var app = await _StartAsync(host => host.ViewPolicy = _ViewersPolicy);
        using var client = _Client(app);
        var viewer = await _SignInAsync(client, "viewer");

        (await _SendAsync(client, HttpMethod.Post, "/dash/api/requeue", cookie: viewer))
            .StatusCode.Should()
            .Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task refuses_an_operate_policy_that_lists_a_scheme_the_session_was_not_issued_from()
    {
        await using var app = await _StartAsync(host => host.OperatePolicy = "OperatorsFromIdp");
        using var client = _Client(app);
        var operatorCookie = await _SignInAsync(client, "operator");

        (await _SendAsync(client, HttpMethod.Get, "/dash/api/stats", cookie: operatorCookie))
            .StatusCode.Should()
            .Be(HttpStatusCode.OK);
        (await _SendAsync(client, HttpMethod.Post, "/dash/api/requeue", cookie: operatorCookie))
            .StatusCode.Should()
            .Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ends_a_session_at_its_lifetime_even_when_requests_keep_renewing_it()
    {
        await using var app = await _StartAsync(host =>
        {
            host.SessionIdleTimeout = TimeSpan.FromMinutes(30);
            host.SessionLifetime = TimeSpan.FromHours(1);
        });
        using var client = _Client(app);
        var cookie = await _SignInAsync(client, "viewer");

        _time.Advance(TimeSpan.FromMinutes(20));
        cookie = await _RenewedAsync(client, cookie);
        _time.Advance(TimeSpan.FromMinutes(20));
        cookie = await _RenewedAsync(client, cookie);
        _time.Advance(TimeSpan.FromMinutes(20));

        // Twenty minutes after the last renewal the idle window is still open, but an hour has passed since sign-in.
        (await _SendAsync(client, HttpMethod.Get, "/dash/api/stats", cookie: cookie))
            .StatusCode.Should()
            .Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ends_a_session_left_idle_past_its_idle_timeout()
    {
        await using var app = await _StartAsync(host => host.SessionIdleTimeout = TimeSpan.FromMinutes(30));
        using var client = _Client(app);
        var cookie = await _SignInAsync(client, "viewer");

        _time.Advance(TimeSpan.FromMinutes(31));

        (await _SendAsync(client, HttpMethod.Get, "/dash/api/stats", cookie: cookie))
            .StatusCode.Should()
            .Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task renews_the_cookie_only_after_half_the_idle_window()
    {
        await using var app = await _StartAsync(host => host.SessionIdleTimeout = TimeSpan.FromMinutes(30));
        using var client = _Client(app);
        var cookie = await _SignInAsync(client, "viewer");

        _time.Advance(TimeSpan.FromMinutes(10));
        using var early = await _SendAsync(client, HttpMethod.Get, "/dash/api/stats", cookie: cookie);
        _time.Advance(TimeSpan.FromMinutes(10));
        using var late = await _SendAsync(client, HttpMethod.Get, "/dash/api/stats", cookie: cookie);

        early.StatusCode.Should().Be(HttpStatusCode.OK);
        _SetCookie(early, _CookieName).Should().BeNull();
        late.StatusCode.Should().Be(HttpStatusCode.OK);
        _SetCookie(late, _CookieName).Should().NotBeNull();
    }

    [Fact]
    public async Task revalidates_at_most_once_per_interval_and_ends_the_session_when_revalidation_fails()
    {
        var calls = 0;
        var allowed = true;
        await using var app = await _StartAsync(host =>
        {
            host.RevalidationInterval = TimeSpan.FromMinutes(5);
            host.Revalidate = (_, _) =>
            {
                calls++;
                return ValueTask.FromResult(allowed);
            };
        });
        using var client = _Client(app);
        var cookie = await _SignInAsync(client, "viewer");

        _time.Advance(TimeSpan.FromMinutes(2));
        (await _SendAsync(client, HttpMethod.Get, "/dash/api/stats", cookie: cookie))
            .StatusCode.Should()
            .Be(HttpStatusCode.OK);
        calls.Should().Be(0);

        _time.Advance(TimeSpan.FromMinutes(4));
        cookie = await _RenewedAsync(client, cookie);
        calls.Should().Be(1);

        _time.Advance(TimeSpan.FromMinutes(1));
        (await _SendAsync(client, HttpMethod.Get, "/dash/api/stats", cookie: cookie))
            .StatusCode.Should()
            .Be(HttpStatusCode.OK);
        calls.Should().Be(1);

        allowed = false;
        _time.Advance(TimeSpan.FromMinutes(5));
        using var refused = await _SendAsync(client, HttpMethod.Get, "/dash/api/stats", cookie: cookie);

        refused.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        calls.Should().Be(2);
        _IsDeletion(_SetCookie(refused, _CookieName)).Should().BeTrue();
    }

    [Fact]
    public async Task keeps_the_host_identity_name_claim_type_in_the_session()
    {
        // The bearer handler names its user through a custom claim type, not ClaimTypes.Name.
        await using var app = await _StartAsync();
        using var client = _Client(app);
        var cookie = await _SignInAsync(client, "operator");

        (await _NameAsync(client, cookie)).Should().Be("operator");
    }

    [Fact]
    public async Task reports_the_session_state_on_auth_info()
    {
        await using var app = await _StartAsync(host =>
        {
            host.ChallengeScheme = _ChallengeScheme;
            host.OperatePolicy = _OperatorsPolicy;
        });
        using var client = _Client(app);

        var anonymous = await _InfoAsync(client, cookie: null);
        var viewer = await _InfoAsync(client, await _SignInAsync(client, "viewer"));
        var operatorInfo = await _InfoAsync(client, await _SignInAsync(client, "operator"));

        anonymous.GetProperty("mode").GetString().Should().Be("host");
        anonymous.GetProperty("enabled").GetBoolean().Should().BeTrue();
        anonymous.GetProperty("signInAvailable").GetBoolean().Should().BeTrue();
        anonymous.GetProperty("authenticated").GetBoolean().Should().BeFalse();
        viewer.GetProperty("authenticated").GetBoolean().Should().BeTrue();
        viewer.GetProperty("name").GetString().Should().Be("viewer");
        viewer.GetProperty("canOperate").GetBoolean().Should().BeFalse();
        operatorInfo.GetProperty("name").GetString().Should().Be("operator");
        operatorInfo.GetProperty("canOperate").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task deletes_the_session_cookie_on_sign_out()
    {
        await using var app = await _StartAsync();
        using var client = _Client(app);
        var cookie = await _SignInAsync(client, "viewer");

        using var signOut = await _SendAsync(client, HttpMethod.Post, "/dash/api/auth/sign-out", cookie: cookie);

        signOut.StatusCode.Should().Be(HttpStatusCode.NoContent);
        _IsDeletion(_SetCookie(signOut, _CookieName)).Should().BeTrue();
    }

    [Fact]
    public async Task deletes_the_session_cookie_from_a_host_endpoint()
    {
        await using var app = await _StartAsync();
        using var client = _Client(app);
        var cookie = await _SignInAsync(client, "viewer");

        using var signOut = await _SendAsync(client, HttpMethod.Post, "/host/logout", cookie: cookie);

        signOut.StatusCode.Should().Be(HttpStatusCode.OK);
        _IsDeletion(_SetCookie(signOut, _CookieName)).Should().BeTrue();
    }

    [Fact]
    public async Task answers_401_without_a_redirect_on_an_iresult_endpoint()
    {
        await using var app = await _StartAsync();
        using var client = _Client(app);

        using var response = await _SendAsync(client, HttpMethod.Get, "/dash/api/result", requestedWith: false);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.Location.Should().BeNull();
    }

    [Fact]
    public async Task keeps_two_dashboards_sessions_independent()
    {
        await using var app = await _StartAsync(mapOther: true);
        using var client = _Client(app);

        using var exchange = await _SendAsync(client, HttpMethod.Post, "/other/api/auth/session", bearer: "viewer");
        var otherCookie = _SetCookie(exchange, "__Host-dashboard-b");

        otherCookie.Should().NotBeNull();
        _SetCookie(exchange, _CookieName).Should().BeNull();
        (await _SendAsync(client, HttpMethod.Get, "/other/api/stats", cookie: _Cookie(otherCookie!)))
            .StatusCode.Should()
            .Be(HttpStatusCode.OK);

        // The same ticket under the first dashboard's cookie name is protected for another scheme, so it is refused.
        (await _SendAsync(client, HttpMethod.Get, "/dash/api/stats", cookie: $"{_CookieName}={otherCookie!.Value}"))
            .StatusCode.Should()
            .Be(HttpStatusCode.Unauthorized);
    }

    private static HttpClient _Client(WebApplication app)
    {
        var client = app.GetTestClient();
        // The session cookie is Secure, so the handler issues and reads it only on an https request.
        client.BaseAddress = new Uri("https://localhost");
        return client;
    }

    private async Task<string> _SignInAsync(HttpClient client, string role)
    {
        using var exchange = await _SendAsync(client, HttpMethod.Post, "/dash/api/auth/session", bearer: role);
        exchange.StatusCode.Should().Be(HttpStatusCode.NoContent);
        return _Cookie(_SetCookie(exchange, _CookieName)!);
    }

    private async Task<string> _RenewedAsync(HttpClient client, string cookie)
    {
        using var response = await _SendAsync(client, HttpMethod.Get, "/dash/api/stats", cookie: cookie);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var renewed = _SetCookie(response, _CookieName);
        renewed.Should().NotBeNull();
        return _Cookie(renewed!);
    }

    private async Task<string> _NameAsync(HttpClient client, string cookie)
    {
        using var response = await _SendAsync(client, HttpMethod.Get, "/dash/api/stats", cookie: cookie);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.Content.ReadAsStringAsync(AbortToken);
    }

    private async Task<JsonElement> _InfoAsync(HttpClient client, string? cookie)
    {
        using var response = await _SendAsync(client, HttpMethod.Get, "/dash/api/auth/info", cookie: cookie);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(AbortToken));
        return document.RootElement.Clone();
    }

    private static SetCookieHeaderValue? _SetCookie(HttpResponseMessage response, string name)
    {
        return response.Headers.TryGetValues("Set-Cookie", out var values)
            ? SetCookieHeaderValue
                .ParseList([.. values])
                .FirstOrDefault(cookie => string.Equals(cookie.Name.Value, name, StringComparison.Ordinal))
            : null;
    }

    private static string _Cookie(SetCookieHeaderValue setCookie) => $"{setCookie.Name}={setCookie.Value}";

    private static bool _IsDeletion(SetCookieHeaderValue? setCookie)
    {
        return setCookie is { Expires: { } expires } && expires < DateTimeOffset.UnixEpoch.AddYears(1);
    }

    private async Task<HttpResponseMessage> _SendAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        string? bearer = null,
        string? cookie = null,
        bool requestedWith = true
    )
    {
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        if (bearer is not null)
        {
            request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + bearer);
        }

        if (cookie is not null)
        {
            request.Headers.TryAddWithoutValidation("Cookie", cookie);
        }

        if (requestedWith)
        {
            request.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
        }

        return await client.SendAsync(request, AbortToken);
    }

    private async Task<WebApplication> _StartAsync(
        Action<DashboardHostAuthenticationOptions>? configureHost = null,
        bool mapOther = false
    )
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton<TimeProvider>(_time);
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        builder
            .Services.AddAuthentication(BearerHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, BearerHandler>(BearerHandler.SchemeName, _ => { })
            .AddScheme<AuthenticationSchemeOptions, IdpHandler>(_ChallengeScheme, _ => { })
            .AddCookie(_HostCookieScheme);
        builder
            .Services.AddAuthorizationBuilder()
            .AddPolicy(_ViewersPolicy, policy => policy.RequireRole("viewer", "operator"))
            .AddPolicy(_OperatorsPolicy, policy => policy.RequireRole("operator"))
            .AddPolicy(
                "OperatorsFromIdp",
                policy => policy.AddAuthenticationSchemes(_ChallengeScheme).RequireRole("operator")
            );
        builder.Services.AddDashboardAuthentication(
            _Name,
            auth =>
            {
                auth.Mode = AuthMode.Host;
                configureHost?.Invoke(auth.Host);
            }
        );
        builder.Services.AddDashboardAuthentication(_OtherName, auth => auth.Mode = AuthMode.Host);

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();

        _MapDashboard(app, "/dash", _Name);
        if (mapOther)
        {
            _MapDashboard(app, "/other", _OtherName);
        }

        app.MapPost(
            "/host/login",
            async (HttpContext context, string role) =>
            {
                var identity = new ClaimsIdentity(
                    [new(BearerHandler.NameType, role), new(BearerHandler.RoleType, role)],
                    _HostCookieScheme,
                    BearerHandler.NameType,
                    BearerHandler.RoleType
                );
                await context.SignInAsync(_HostCookieScheme, new ClaimsPrincipal(identity));
            }
        );
        app.MapPost("/host/logout", (HttpContext context) => context.SignOutHeadlessDashboardAsync(_Name));

        await app.StartAsync(AbortToken);
        return app;
    }

    private static void _MapDashboard(WebApplication app, string basePath, string name)
    {
        var dashboard = app.MapGroup(basePath);
        dashboard.MapDashboardAuthEndpoints(name);
        var api = dashboard.MapGroup("/api").RequireDashboardAuthentication(name).RequireDashboardRequestIntegrity();
        api.MapGet("/stats", (HttpContext context) => context.User.Identity?.Name ?? "<anonymous>");
        api.MapGet("/result", () => Results.Ok());
        api.MapPost("/requeue", () => "requeued").RequireDashboardOperation(name);
    }

    /// <summary>
    /// Signs in <c>Authorization: Bearer &lt;role&gt;</c> as a user named for, and in, that role, through claim types of
    /// its own rather than <see cref="ClaimTypes.Name"/> and <see cref="ClaimTypes.Role"/>.
    /// </summary>
    private sealed class BearerHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder
    ) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "Bearer";
        public const string NameType = "username";
        public const string RoleType = "group";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var header = Request.Headers.Authorization.ToString();
            if (!header.StartsWith("Bearer ", StringComparison.Ordinal))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var role = header["Bearer ".Length..];
            var identity = new ClaimsIdentity(
                [new(NameType, role), new(RoleType, role)],
                SchemeName,
                NameType,
                RoleType
            );

            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new(identity), SchemeName)));
        }
    }

    /// <summary>
    /// Stands in for a remote identity provider: it authenticates no one, and its challenge redirects to its login page
    /// carrying the return address the challenge asked for.
    /// </summary>
    private sealed class IdpHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder
    ) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        protected override Task HandleChallengeAsync(AuthenticationProperties properties)
        {
            Response.Redirect("/idp/login?redirect=" + Uri.EscapeDataString(properties.RedirectUri!));
            return Task.CompletedTask;
        }
    }
}
