// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Headless.Jobs;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tests.Dashboard;

/// <summary>
/// The Jobs dashboard registered through <c>AddDashboard(...)</c> and mounted with <c>MapHeadlessJobsDashboard()</c>
/// on a real host whose fallback policy requires an authenticated user with a tenant claim. The host's own
/// authorization evaluates the dashboard's endpoints by their own metadata, never by the fallback policy.
/// </summary>
public sealed class JobsDashboardHostMountTests : TestBase
{
    private const string _HostProbePath = "/host-probe";
    private const string _ApiPath = "/jobs/dashboard/api/options";
    private const string _NegotiatePath = "/jobs/dashboard/job-notification-hub/negotiate?negotiateVersion=1";

    private static readonly string _BasicCredential = "Basic " + "ops:secret".ToBase64();

    [Fact]
    public async Task should_fail_the_start_when_the_dashboard_is_registered_but_not_mapped()
    {
        var act = () => _StartAsync(dashboard => dashboard.WithNoAuth(), mapDashboard: false);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not mapped*MapHeadlessJobsDashboard*");
    }

    [Fact]
    public async Task should_refuse_an_unauthenticated_hub_negotiate_under_basic_auth()
    {
        await using var app = await _StartAsync(dashboard => dashboard.WithBasicAuth("ops", "secret"));
        using var client = app.GetTestClient();

        using var response = await _SendAsync(client, HttpMethod.Post, _NegotiatePath, authorization: null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.Content.ReadAsStringAsync(AbortToken)).Should().BeEmpty();
    }

    [Fact]
    public async Task should_negotiate_the_hub_with_the_dashboard_credential_in_the_header_or_the_access_token()
    {
        await using var app = await _StartAsync(dashboard => dashboard.WithBasicAuth("ops", "secret"));
        using var client = app.GetTestClient();

        // A browser cannot set a header on a WebSocket, so the SPA sends the hub credential as access_token.
        using var header = await _SendAsync(client, HttpMethod.Post, _NegotiatePath, _BasicCredential);
        using var accessToken = await _SendAsync(
            client,
            HttpMethod.Post,
            _NegotiatePath + "&access_token=" + Uri.EscapeDataString("ops:secret".ToBase64()),
            authorization: null
        );

        header.StatusCode.Should().Be(HttpStatusCode.OK);
        (await header.Content.ReadAsStringAsync(AbortToken)).Should().Contain("connectionToken");
        accessToken.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task should_answer_404_for_an_unmatched_dashboard_api_path()
    {
        await using var app = await _StartAsync(dashboard => dashboard.WithBasicAuth("ops", "secret"));
        using var client = app.GetTestClient();

        using var anonymous = await _SendAsync(client, HttpMethod.Get, "/jobs/dashboard/api/x", authorization: null);
        using var authenticated = await _SendAsync(client, HttpMethod.Get, "/jobs/dashboard/api/x", _BasicCredential);

        anonymous.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await anonymous.Content.ReadAsStringAsync(AbortToken)).Should().NotContain("JobsConfig");
        authenticated.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("/jobs/dashboard")]
    [InlineData("/jobs/dashboard/")]
    [InlineData("/jobs/dashboard/time-jobs")]
    [InlineData("/jobs/dashboard/cron-jobs/occurrences/42")]
    public async Task should_serve_the_shell_anonymously_for_a_deep_link_under_basic_auth(string path)
    {
        await using var app = await _StartAsync(dashboard => dashboard.WithBasicAuth("ops", "secret"));
        using var client = app.GetTestClient();

        using var response = await _SendAsync(client, HttpMethod.Get, path, authorization: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/html");
        (await response.Content.ReadAsStringAsync(AbortToken))
            .Should()
            .Contain("window.JobsConfig = ")
            .And.Contain("\"mode\":\"basic\"");
    }

    [Fact]
    public async Task should_apply_the_dashboard_credential_instead_of_the_host_fallback_under_basic_auth()
    {
        await using var app = await _StartAsync(dashboard => dashboard.WithBasicAuth("ops", "secret"));
        using var client = app.GetTestClient();

        // The fallback policy is live for the host's own endpoints.
        (await _SendAsync(client, HttpMethod.Get, _HostProbePath, _BasicCredential))
            .StatusCode.Should()
            .Be(HttpStatusCode.Unauthorized);

        // The dashboard credential alone satisfies the dashboard, though it carries no tenant claim.
        (await _SendAsync(client, HttpMethod.Get, _ApiPath, _BasicCredential))
            .StatusCode.Should()
            .Be(HttpStatusCode.OK);

        // A host user that satisfies the fallback is still not the dashboard's credential.
        (await _SendAsync(client, HttpMethod.Get, _ApiPath, "Token operator;tenant-a"))
            .StatusCode.Should()
            .Be(HttpStatusCode.Unauthorized);
        (await _SendAsync(client, HttpMethod.Get, _ApiPath, "Basic " + "ops:wrong".ToBase64()))
            .StatusCode.Should()
            .Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task should_apply_the_named_host_policy_to_the_api_and_the_hub_under_host_auth()
    {
        await using var app = await _StartAsync(dashboard => dashboard.WithHostAuthentication(TokenHandler.Policy));
        using var client = app.GetTestClient();

        (await _SendAsync(client, HttpMethod.Get, _ApiPath, authorization: null))
            .StatusCode.Should()
            .Be(HttpStatusCode.Unauthorized);
        (await _SendAsync(client, HttpMethod.Get, _ApiPath, "Token viewer;tenant-a"))
            .StatusCode.Should()
            .Be(HttpStatusCode.Forbidden);

        // A tenantless operator fails the host fallback but satisfies the dashboard policy.
        (await _SendAsync(client, HttpMethod.Get, _ApiPath, "Token operator"))
            .StatusCode.Should()
            .Be(HttpStatusCode.OK);
        (await _SendAsync(client, HttpMethod.Post, _NegotiatePath, authorization: null))
            .StatusCode.Should()
            .Be(HttpStatusCode.Unauthorized);
        (await _SendAsync(client, HttpMethod.Post, _NegotiatePath + "&access_token=Token%20operator", null))
            .StatusCode.Should()
            .Be(HttpStatusCode.OK);
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
    public async Task should_reject_a_second_mapping()
    {
        await using var app = await _StartAsync(dashboard => dashboard.WithNoAuth());

        var act = () => app.MapHeadlessJobsDashboard();

        act.Should().Throw<InvalidOperationException>().WithMessage("*already*");
    }

    [Fact]
    public async Task should_reject_mapping_without_the_dashboard_registration()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        await using var app = builder.Build();

        var act = () => app.MapHeadlessJobsDashboard();

        act.Should().Throw<InvalidOperationException>().WithMessage("*not registered*AddDashboard*");
    }

    [Fact]
    public async Task should_honor_a_custom_base_path()
    {
        await using var app = await _StartAsync(dashboard => dashboard.SetBasePath("/ops/jobs").WithNoAuth());
        using var client = app.GetTestClient();

        using var shell = await _SendAsync(client, HttpMethod.Get, "/ops/jobs/time-jobs", authorization: null);
        shell.StatusCode.Should().Be(HttpStatusCode.OK);
        (await shell.Content.ReadAsStringAsync(AbortToken)).Should().Contain("\"basePath\":\"/ops/jobs\"");
        (await _SendAsync(client, HttpMethod.Get, "/ops/jobs/api/options", authorization: null))
            .StatusCode.Should()
            .Be(HttpStatusCode.OK);
        (await _SendAsync(client, HttpMethod.Get, _ApiPath, authorization: null))
            .StatusCode.Should()
            .Be(HttpStatusCode.Unauthorized, "the default base path is not mounted, so the host fallback answers");
    }

    private static async Task<WebApplication> _StartAsync(
        Action<DashboardOptionsBuilder> configureDashboard,
        bool mapDashboard = true
    )
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
        builder.Services.AddHeadlessJobs(options =>
            options.DisableBackgroundServices().AddDashboard(configureDashboard)
        );

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGet(_HostProbePath, () => "host");

        if (mapDashboard)
        {
            app.MapHeadlessJobsDashboard();
        }

        try
        {
            await app.StartAsync(AbortToken);
        }
        catch
        {
            await app.DisposeAsync();
            throw;
        }

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
