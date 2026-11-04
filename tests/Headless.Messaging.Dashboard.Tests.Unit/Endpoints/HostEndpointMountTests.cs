// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Headless.Messaging;
using Headless.Messaging.Dashboard;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tests.Endpoints;

/// <summary>
/// The dashboard mounted on the host's endpoint routing, in a host whose fallback policy requires an authenticated user
/// with a tenant claim. The host's own authorization must evaluate the dashboard's endpoints by their own metadata, not
/// by the fallback policy.
/// </summary>
public sealed class HostEndpointMountTests : TestBase
{
    private const string _HostProbePath = "/host-probe";

    [Fact]
    public async Task should_serve_the_shell_and_assets_anonymously_while_the_host_fallback_refuses_other_paths()
    {
        await using var app = await _StartAsync(dashboard => dashboard.WithHostAuthentication(TokenHandler.Policy));
        using var client = app.GetTestClient();

        // The fallback policy is live for the host's own endpoints.
        (await _SendAsync(client, HttpMethod.Get, _HostProbePath, authorization: null))
            .StatusCode.Should()
            .Be(HttpStatusCode.Unauthorized);

        foreach (var shellPath in new[] { "/messaging", "/messaging/", "/messaging/published/failed" })
        {
            using var shell = await _SendAsync(client, HttpMethod.Get, shellPath, authorization: null);
            shell.StatusCode.Should().Be(HttpStatusCode.OK, shellPath);
            shell.Content.Headers.ContentType!.MediaType.Should().Be("text/html");
            var html = await shell.Content.ReadAsStringAsync(AbortToken);
            html.Should().Contain("window.MessagingConfig = ").And.Contain("\"mode\":\"host\"");
        }

        using var asset = await _SendAsync(client, HttpMethod.Get, "/messaging/logo.svg", authorization: null);
        asset.StatusCode.Should().Be(HttpStatusCode.OK);
        asset.Content.Headers.ContentType!.MediaType.Should().Be("image/svg+xml");
    }

    [Fact]
    public async Task should_keep_the_auth_and_health_endpoints_anonymous()
    {
        await using var app = await _StartAsync(dashboard => dashboard.WithHostAuthentication(TokenHandler.Policy));
        using var client = app.GetTestClient();

        (await _SendAsync(client, HttpMethod.Get, "/messaging/api/auth/info", authorization: null))
            .StatusCode.Should()
            .Be(HttpStatusCode.OK);
        (await _SendAsync(client, HttpMethod.Get, "/messaging/api/health", authorization: null))
            .StatusCode.Should()
            .Be(HttpStatusCode.OK);

        // validate is reachable anonymously and reports the caller's own state.
        (await _SendAsync(client, HttpMethod.Post, "/messaging/api/auth/validate", authorization: null))
            .StatusCode.Should()
            .Be(HttpStatusCode.Unauthorized);
        (await _SendAsync(client, HttpMethod.Post, "/messaging/api/auth/validate", "Token operator"))
            .StatusCode.Should()
            .Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task should_apply_the_dashboard_policy_to_the_api_instead_of_the_host_fallback()
    {
        await using var app = await _StartAsync(dashboard => dashboard.WithHostAuthentication(TokenHandler.Policy));
        using var client = app.GetTestClient();

        (await _SendAsync(client, HttpMethod.Get, "/messaging/api/stats", authorization: null))
            .StatusCode.Should()
            .Be(HttpStatusCode.Unauthorized);
        (await _SendAsync(client, HttpMethod.Get, "/messaging/api/stats", "Token viewer;tenant-a"))
            .StatusCode.Should()
            .Be(HttpStatusCode.Forbidden);

        // A tenantless operator fails the host fallback but satisfies the dashboard policy.
        (await _SendAsync(client, HttpMethod.Get, _HostProbePath, "Token operator"))
            .StatusCode.Should()
            .Be(HttpStatusCode.Forbidden);
        (await _SendAsync(client, HttpMethod.Get, "/messaging/api/stats", "Token operator"))
            .StatusCode.Should()
            .Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task should_apply_the_default_policy_when_host_authentication_names_none()
    {
        await using var app = await _StartAsync(dashboard => dashboard.WithHostAuthentication());
        using var client = app.GetTestClient();

        (await _SendAsync(client, HttpMethod.Get, "/messaging/api/stats", authorization: null))
            .StatusCode.Should()
            .Be(HttpStatusCode.Unauthorized);
        (await _SendAsync(client, HttpMethod.Get, "/messaging/api/stats", "Token viewer"))
            .StatusCode.Should()
            .Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task should_not_serve_the_shell_for_an_unmatched_api_path()
    {
        await using var app = await _StartAsync(dashboard => dashboard.WithHostAuthentication(TokenHandler.Policy));
        using var client = app.GetTestClient();

        using var response = await _SendAsync(client, HttpMethod.Get, "/messaging/api/no-such-route", null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync(AbortToken)).Should().NotContain("MessagingConfig");
    }

    [Fact]
    public async Task should_enforce_the_dashboard_credential_under_basic_auth_without_the_host_fallback()
    {
        const string credential = "ops:secret";
        await using var app = await _StartAsync(dashboard => dashboard.WithBasicAuth("ops", "secret"));
        using var client = app.GetTestClient();

        (await _SendAsync(client, HttpMethod.Get, "/messaging/api/stats", authorization: null))
            .StatusCode.Should()
            .Be(HttpStatusCode.Unauthorized);
        (await _SendAsync(client, HttpMethod.Get, "/messaging/api/stats", "Basic " + "ops:wrong".ToBase64()))
            .StatusCode.Should()
            .Be(HttpStatusCode.Unauthorized);
        (await _SendAsync(client, HttpMethod.Get, "/messaging/api/stats", "Basic " + credential.ToBase64()))
            .StatusCode.Should()
            .Be(HttpStatusCode.OK);
        (await _SendAsync(client, HttpMethod.Get, "/messaging", authorization: null))
            .StatusCode.Should()
            .Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task should_honor_a_custom_base_path()
    {
        await using var app = await _StartAsync(dashboard =>
            dashboard.SetBasePath("/ops/messages").WithHostAuthentication(TokenHandler.Policy)
        );
        using var client = app.GetTestClient();

        using var shell = await _SendAsync(client, HttpMethod.Get, "/ops/messages", authorization: null);
        shell.StatusCode.Should().Be(HttpStatusCode.OK);
        (await shell.Content.ReadAsStringAsync(AbortToken)).Should().Contain("\"basePath\":\"/ops/messages\"");
        (await _SendAsync(client, HttpMethod.Get, "/ops/messages/api/stats", "Token operator"))
            .StatusCode.Should()
            .Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task should_skip_the_branch_mount_once_the_dashboard_is_mapped_on_host_endpoints()
    {
        var config = new MessagingDashboardOptionsBuilder().WithNoAuth();
        config.EndpointMounted = true;
        var app = new ApplicationBuilder(new ServiceCollection().BuildServiceProvider());
        var nextRan = false;

        new MessagingDashboardStartupFilter(config).Configure(_ => nextRan = true)(app);
        var pipeline = app.Build();
        var context = new DefaultHttpContext { Request = { Path = "/messaging/api/health" } };
        await pipeline(context);

        nextRan.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status404NotFound);
    }

    [Fact]
    public async Task should_reject_branch_only_middleware_hooks()
    {
        var act = () =>
            _StartAsync(dashboard =>
            {
                dashboard.WithNoAuth();
                dashboard.CustomMiddleware = _ => { };
            });

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*CustomMiddleware*");
    }

    [Theory]
    [InlineData("/")]
    [InlineData("")]
    public async Task should_reject_the_root_base_path(string basePath)
    {
        var act = () => _StartAsync(dashboard => dashboard.SetBasePath(basePath).WithNoAuth());

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*root path*");
    }

    [Fact]
    public async Task should_reject_mapping_without_the_dashboard_registration()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        await using var app = builder.Build();

        var act = () => app.MapHeadlessMessagingDashboard();

        act.Should().Throw<InvalidOperationException>().WithMessage("*UseDashboard*");
    }

    [Fact]
    public async Task should_reject_a_second_mapping()
    {
        await using var app = await _StartAsync(dashboard => dashboard.WithNoAuth());

        var act = () => app.MapHeadlessMessagingDashboard();

        act.Should().Throw<InvalidOperationException>().WithMessage("*already*");
    }

    private static async Task<WebApplication> _StartAsync(Action<MessagingDashboardOptionsBuilder> configureDashboard)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();

        builder
            .Services.AddAuthentication(TokenHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, TokenHandler>(TokenHandler.SchemeName, _ => { });
        builder
            .Services.AddAuthorizationBuilder()
            .AddPolicy(TokenHandler.Policy, policy => policy.RequireRole("operator"))
            .SetFallbackPolicy(
                new AuthorizationPolicyBuilder()
                    .RequireAuthenticatedUser()
                    .RequireClaim(TokenHandler.TenantClaim)
                    .Build()
            );

        builder.Services.AddHeadlessMessaging(setup =>
        {
            setup.UseInMemory();
            setup.UseInMemoryStorage();
            setup.UseDashboard(configureDashboard);
        });

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGet(_HostProbePath, () => "host");
        app.MapHeadlessMessagingDashboard();

        await app.StartAsync(AbortToken);

        return app;
    }

    private static async Task<HttpResponseMessage> _SendAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        string? authorization
    )
    {
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        if (authorization is not null)
        {
            request.Headers.TryAddWithoutValidation("Authorization", authorization);
        }

        return await client.SendAsync(request, AbortToken);
    }

    /// <summary>
    /// Signs in <c>Authorization: Token &lt;role&gt;[;&lt;tenant&gt;]</c> as a user in that role, with a tenant claim
    /// when one is given.
    /// </summary>
    private sealed class TokenHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder
    ) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "Token";
        public const string Policy = "Operators";
        public const string TenantClaim = "tenant";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var header = Request.Headers.Authorization.ToString();
            if (!header.StartsWith("Token ", StringComparison.Ordinal))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var parts = header["Token ".Length..].Split(';');
            List<Claim> claims = [new(ClaimTypes.Name, parts[0]), new(ClaimTypes.Role, parts[0])];
            if (parts.Length > 1)
            {
                claims.Add(new Claim(TenantClaim, parts[1]));
            }

            var identity = new ClaimsIdentity(claims, SchemeName);

            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new(identity), SchemeName)));
        }
    }
}
