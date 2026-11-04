// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
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
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tests.Endpoints;

/// <summary>
/// The dashboard mounted on the host's endpoint routing. Most hosts here have a fallback policy that requires an
/// authenticated user with a tenant claim: the host's own authorization must evaluate the dashboard's endpoints by
/// their own metadata, not by the fallback policy.
/// </summary>
public sealed class HostEndpointMountTests : TestBase
{
    private const string _HostProbePath = "/host-probe";
    private const string _AuthenticationFailedEvent = "AuthenticationFailed";

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

        // A wrong credential is the scheme's challenge: a bare 401 with no body.
        using var wrong = await _SendAsync(
            client,
            HttpMethod.Get,
            "/messaging/api/stats",
            "Basic " + "ops:wrong".ToBase64()
        );
        wrong.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await wrong.Content.ReadAsStringAsync(AbortToken)).Should().BeEmpty();

        // The right credential carries no tenant claim, so the host fallback would refuse it.
        (await _SendAsync(client, HttpMethod.Get, "/messaging/api/stats", "Basic " + credential.ToBase64()))
            .StatusCode.Should()
            .Be(HttpStatusCode.OK);
        (await _SendAsync(client, HttpMethod.Get, "/messaging", authorization: null))
            .StatusCode.Should()
            .Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task should_leave_host_endpoints_unchallenged_when_the_host_has_no_scheme_of_its_own()
    {
        // given - the dashboard's scheme is the only one, so it is the host's default and UseAuthentication runs it on
        // every request.
        using var logs = new RecordingLoggerProvider();
        await using var app = await MessagingDashboardHost.StartAsync(
            dashboard => dashboard.WithBasicAuth("ops", "secret"),
            configureHost: builder => builder.Logging.AddProvider(logs),
            configurePipeline: host =>
            {
                host.UseAuthentication();
                host.UseAuthorization();
                host.MapGet(_HostProbePath, () => "host");
            },
            cancellationToken: AbortToken
        );
        using var client = app.GetTestClient();

        // when - host requests with no credential and with a wrong dashboard credential
        foreach (var authorization in new[] { null, "Basic " + "ops:wrong".ToBase64() })
        {
            using var response = await _SendAsync(client, HttpMethod.Get, _HostProbePath, authorization);

            // then
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            response.Headers.WwwAuthenticate.Should().BeEmpty();
            (await response.Content.ReadAsStringAsync(AbortToken)).Should().Be("host");
        }

        logs.Entries.Should().NotContain(entry => entry.EventName == _AuthenticationFailedEvent);

        // The same wrong credential on the dashboard API is checked and logged once.
        (await _SendAsync(client, HttpMethod.Get, "/messaging/api/stats", "Basic " + "ops:wrong".ToBase64()))
            .StatusCode.Should()
            .Be(HttpStatusCode.Unauthorized);
        logs.Entries.Should()
            .ContainSingle(entry => entry.EventName == _AuthenticationFailedEvent)
            .Which.Level.Should()
            .Be(LogLevel.Warning);
    }

    [Fact]
    public async Task should_accept_the_dashboard_credential_when_a_host_without_its_own_scheme_calls_use_routing()
    {
        // given - a host with no scheme of its own that calls UseRouting itself: the WebApplication then adds its
        // implicit UseAuthentication ahead of routing, where no endpoint is known yet.
        await using var app = await MessagingDashboardHost.StartAsync(
            dashboard => dashboard.WithBasicAuth("ops", "secret"),
            configurePipeline: host =>
            {
                host.UseRouting();
                host.UseAuthorization();
            },
            cancellationToken: AbortToken
        );
        using var client = app.GetTestClient();

        // when
        using var response = await _SendAsync(
            client,
            HttpMethod.Get,
            "/messaging/api/stats",
            "Basic " + "ops:secret".ToBase64()
        );

        // then
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task should_check_the_dashboard_credential_when_the_host_authenticates_before_routing()
    {
        // given - a host with no scheme of its own that calls UseAuthentication ahead of UseRouting, so the dashboard
        // scheme first runs where no endpoint is known yet.
        using var logs = new RecordingLoggerProvider();
        await using var app = await MessagingDashboardHost.StartAsync(
            dashboard => dashboard.WithBasicAuth("ops", "secret"),
            configureHost: builder => builder.Logging.AddProvider(logs),
            configurePipeline: host =>
            {
                host.UseAuthentication();
                host.UseRouting();
                host.UseAuthorization();
            },
            cancellationToken: AbortToken
        );
        using var client = app.GetTestClient();

        // when
        using var right = await _SendAsync(
            client,
            HttpMethod.Get,
            "/messaging/api/stats",
            "Basic " + "ops:secret".ToBase64()
        );
        using var wrong = await _SendAsync(
            client,
            HttpMethod.Get,
            "/messaging/api/stats",
            "Basic " + "ops:wrong".ToBase64()
        );

        // then - the credential is checked once routing knows the endpoint, and a failure is logged once
        right.StatusCode.Should().Be(HttpStatusCode.OK);
        wrong.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        logs.Entries.Should().ContainSingle(entry => entry.EventName == _AuthenticationFailedEvent);
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
    public async Task should_serve_a_startup_host_that_maps_the_dashboard_inside_use_endpoints()
    {
        // A Startup-style host maps its endpoints while the pipeline is built, inside the startup filter's next(app).
        using var host = await new HostBuilder()
            .ConfigureWebHost(web =>
                web.UseTestServer()
                    .ConfigureServices(services =>
                        services.AddHeadlessMessaging(setup =>
                        {
                            setup.UseInMemory();
                            setup.UseInMemoryStorage();
                            setup.UseDashboard(dashboard => dashboard.WithBasicAuth("ops", "secret"));
                        })
                    )
                    .Configure(app =>
                    {
                        app.UseRouting();
                        app.UseAuthentication();
                        app.UseAuthorization();
                        app.UseEndpoints(endpoints => endpoints.MapHeadlessMessagingDashboard());
                    })
            )
            .StartAsync(AbortToken);
        using var client = host.GetTestClient();

        (await _SendAsync(client, HttpMethod.Get, "/messaging", authorization: null))
            .StatusCode.Should()
            .Be(HttpStatusCode.OK);
        (await _SendAsync(client, HttpMethod.Get, "/messaging/api/stats", authorization: null))
            .StatusCode.Should()
            .Be(HttpStatusCode.Unauthorized);
        (await _SendAsync(client, HttpMethod.Get, "/messaging/api/stats", "Basic " + "ops:secret".ToBase64()))
            .StatusCode.Should()
            .Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task should_fail_the_start_when_the_dashboard_is_registered_but_never_mapped()
    {
        var act = () =>
            MessagingDashboardHost.StartAsync(
                dashboard => dashboard.WithNoAuth(),
                mapDashboard: false,
                cancellationToken: AbortToken
            );

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*not mapped*MapHeadlessMessagingDashboard()*");
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

    /// <summary>
    /// Starts a host with its own <see cref="TokenHandler"/> scheme, an operator policy, and a fallback policy that
    /// requires a tenant claim, plus one host endpoint at <see cref="_HostProbePath"/>.
    /// </summary>
    private static Task<WebApplication> _StartAsync(Action<MessagingDashboardOptionsBuilder> configureDashboard)
    {
        return MessagingDashboardHost.StartAsync(
            configureDashboard,
            configureHost: builder =>
            {
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
            },
            configurePipeline: app =>
            {
                app.UseAuthentication();
                app.UseAuthorization();
                app.MapGet(_HostProbePath, () => "host");
            },
            cancellationToken: AbortToken
        );
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

    private sealed record LogEntry(string Category, LogLevel Level, string? EventName);

    /// <summary>Records every log entry the host writes, so a test can assert what was and was not logged.</summary>
    private sealed class RecordingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<LogEntry> _entries = new();

        public IReadOnlyCollection<LogEntry> Entries => _entries;

        public ILogger CreateLogger(string categoryName)
        {
            return new RecordingLogger(categoryName, _entries);
        }

        public void Dispose() { }

        private sealed class RecordingLogger(string category, ConcurrentQueue<LogEntry> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull
            {
                return null;
            }

            public bool IsEnabled(LogLevel logLevel)
            {
                return true;
            }

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter
            )
            {
                entries.Enqueue(new LogEntry(category, logLevel, eventId.Name));
            }
        }
    }
}
