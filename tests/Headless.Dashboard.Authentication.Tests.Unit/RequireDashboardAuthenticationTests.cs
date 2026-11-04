// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Headless.Dashboard.Authentication;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tests;

/// <summary>
/// <c>RequireDashboardAuthentication</c> on endpoints of a real host's endpoint routing, where the host's own
/// <c>UseAuthentication</c> and <c>UseAuthorization</c> evaluate the dashboard's scheme and policy.
/// </summary>
public sealed class RequireDashboardAuthenticationTests : TestBase
{
    private const string _Name = "dashboard-a";
    private const string _OtherName = "dashboard-b";
    private const string _Path = "/dashboard-a/api/data";
    private const string _OtherPath = "/dashboard-b/api/data";
    private const string _HostProbePath = "/host-probe";
    private const string _HandlerCategory = "Headless.Dashboard.Authentication.DashboardAuthenticationHandler";

    private readonly CapturingLoggerProvider _logs = new();

    [Fact]
    public async Task signs_in_the_basic_user_when_the_credential_matches()
    {
        await using var app = await _StartSingleAsync(_Basic("admin", "pass"));
        using var client = app.GetTestClient();

        using var response = await _SendAsync(client, _Path, "Basic " + "admin:pass".ToBase64());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(AbortToken)).Should().Be("admin");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Basic YWRtaW46d3Jvbmc=")]
    [InlineData("Basic not-base64")]
    public async Task challenges_with_an_empty_401_when_the_basic_credential_is_missing_or_wrong(string? authorization)
    {
        await using var app = await _StartSingleAsync(_Basic("admin", "pass"));
        using var client = app.GetTestClient();

        using var response = await _SendAsync(client, _Path, authorization);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.Content.ReadAsStringAsync(AbortToken)).Should().BeEmpty();
    }

    [Fact]
    public async Task authenticates_an_api_key_sent_as_a_bearer_token()
    {
        await using var app = await _StartSingleAsync(_ApiKey("secret"));
        using var client = app.GetTestClient();

        using var accepted = await _SendAsync(client, _Path, "Bearer secret");
        using var refused = await _SendAsync(client, _Path, "Bearer other");

        accepted.StatusCode.Should().Be(HttpStatusCode.OK);
        (await accepted.Content.ReadAsStringAsync(AbortToken)).Should().Be("api-user");
        refused.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task leaves_the_endpoint_anonymous_when_the_mode_is_none()
    {
        await using var app = await _StartSingleAsync(auth => auth.Mode = AuthMode.None);
        using var client = app.GetTestClient();

        using var response = await _SendAsync(client, _Path, authorization: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(AbortToken)).Should().Be("<anonymous>");
    }

    [Fact]
    public async Task applies_the_named_host_policy_under_host_mode()
    {
        await using var app = await _StartHostModeAsync(TokenHandler.Policy);
        using var client = app.GetTestClient();

        (await _SendAsync(client, _Path, authorization: null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await _SendAsync(client, _Path, "Token viewer")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        using var operatorResponse = await _SendAsync(client, _Path, "Token operator");
        operatorResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        (await operatorResponse.Content.ReadAsStringAsync(AbortToken)).Should().Be("operator");
    }

    [Fact]
    public async Task applies_the_host_default_policy_under_host_mode_without_a_named_policy()
    {
        await using var app = await _StartHostModeAsync(policy: null);
        using var client = app.GetTestClient();

        (await _SendAsync(client, _Path, authorization: null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await _SendAsync(client, _Path, "Token viewer")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task neither_challenges_nor_logs_a_host_endpoint_when_the_dashboard_scheme_is_the_only_scheme()
    {
        // The only registered scheme becomes the host's default, so the host's UseAuthentication runs it on every
        // request, including requests that never reach the dashboard.
        await using var app = await _StartSingleAsync(_Basic("admin", "pass"));
        using var client = app.GetTestClient();

        using var probe = await _SendAsync(client, _HostProbePath, "Basic " + "admin:wrong".ToBase64());

        probe.StatusCode.Should().Be(HttpStatusCode.OK);
        (await probe.Content.ReadAsStringAsync(AbortToken)).Should().Be("host:<anonymous>");
        _logs.Entries.Should().NotContain(entry => entry.Category == _HandlerCategory);

        // The same credential on the dashboard's endpoint is checked and its failure logged once.
        (await _SendAsync(client, _Path, "Basic " + "admin:wrong".ToBase64()))
            .StatusCode.Should()
            .Be(HttpStatusCode.Unauthorized);
        _FailureWarnings().Should().ContainSingle().Which.Should().Contain("/dashboard-a/api/data");
    }

    [Fact]
    public async Task checks_the_credential_when_the_host_authenticates_before_its_own_routing()
    {
        // A host that calls UseRouting itself gets the WebApplication's implicit UseAuthentication ahead of routing,
        // where the dashboard scheme, the host's only and so default scheme, sees no endpoint yet. The dashboard policy
        // asks again after routing and must get a fresh answer, not that first NoResult.
        await using var app = await _StartAsync(
            services => services.AddDashboardAuthentication(_Name, _Basic("admin", "pass")),
            endpoints => endpoints.MapGet(_Path, _UserName).RequireDashboardAuthentication(_Name),
            pipeline =>
            {
                pipeline.UseRouting();
                pipeline.UseAuthorization();
            }
        );
        using var client = app.GetTestClient();

        using var accepted = await _SendAsync(client, _Path, "Basic " + "admin:pass".ToBase64());
        using var refused = await _SendAsync(client, _Path, "Basic " + "admin:wrong".ToBase64());

        accepted.StatusCode.Should().Be(HttpStatusCode.OK);
        (await accepted.Content.ReadAsStringAsync(AbortToken)).Should().Be("admin");
        refused.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _FailureWarnings().Should().ContainSingle();
    }

    [Fact]
    public async Task strips_line_breaks_from_the_logged_path_of_a_failed_request()
    {
        await using var app = await _StartAsync(
            services => services.AddDashboardAuthentication(_Name, _ApiKey("secret")),
            endpoints =>
                endpoints
                    .MapGet("/dashboard-a/api/{**rest}", (string rest) => rest)
                    .RequireDashboardAuthentication(_Name)
        );
        using var client = app.GetTestClient();

        (await _SendAsync(client, "/dashboard-a/api/x%0D%0Aforged", "Bearer wrong"))
            .StatusCode.Should()
            .Be(HttpStatusCode.Unauthorized);

        var message = _FailureWarnings().Should().ContainSingle().Subject;
        message.Should().Contain("/dashboard-a/api/xforged");
        message.Should().NotContain("\r").And.NotContain("\n");
    }

    [Fact]
    public async Task keeps_each_dashboards_credential_independent_in_one_host()
    {
        await using var app = await _StartAsync(
            services =>
            {
                services.AddDashboardAuthentication(_Name, _ApiKey("key-a"));
                services.AddDashboardAuthentication(_OtherName, _Basic("ops", "key-b"));
            },
            endpoints =>
            {
                endpoints.MapGet(_Path, _UserName).RequireDashboardAuthentication(_Name);
                endpoints.MapGet(_OtherPath, _UserName).RequireDashboardAuthentication(_OtherName);
            }
        );
        using var client = app.GetTestClient();
        var basicB = "Basic " + "ops:key-b".ToBase64();

        (await _SendAsync(client, _Path, "Bearer key-a")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await _SendAsync(client, _Path, basicB)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await _SendAsync(client, _OtherPath, basicB)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await _SendAsync(client, _OtherPath, "Bearer key-a")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task registers_the_scheme_and_policy_once_when_one_name_is_registered_twice()
    {
        await using var app = await _StartAsync(
            services =>
            {
                services.AddDashboardAuthentication(_Name, _ApiKey("secret"));
                services.AddDashboardAuthentication(_Name, _ApiKey("secret"));
            },
            endpoints => endpoints.MapGet(_Path, _UserName).RequireDashboardAuthentication(_Name)
        );
        using var client = app.GetTestClient();

        (await _SendAsync(client, _Path, "Bearer secret")).StatusCode.Should().Be(HttpStatusCode.OK);
        var schemes = await app.Services.GetRequiredService<IAuthenticationSchemeProvider>().GetAllSchemesAsync();
        schemes.Should().ContainSingle(scheme => scheme.Name == _Name);
    }

    [Fact]
    public async Task refuses_an_unauthenticated_hub_negotiate_before_signalr_and_accepts_the_access_token()
    {
        // SignalR adds a connection to Clients.All before the hub's OnConnectedAsync can reject it, so the hub endpoint
        // itself must refuse a client without the credential.
        await using var app = await _StartAsync(
            services =>
            {
                services.AddSignalR();
                services.AddDashboardAuthentication(_Name, _ApiKey("secret"));
            },
            endpoints => endpoints.MapHub<Hub>("/dashboard-a/hub").RequireDashboardAuthentication(_Name)
        );
        using var client = app.GetTestClient();

        using var refused = await _SendAsync(
            client,
            "/dashboard-a/hub/negotiate?negotiateVersion=1",
            authorization: null,
            HttpMethod.Post
        );
        using var accepted = await _SendAsync(
            client,
            "/dashboard-a/hub/negotiate?negotiateVersion=1&access_token=secret",
            authorization: null,
            HttpMethod.Post
        );

        refused.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        accepted.StatusCode.Should().Be(HttpStatusCode.OK);
        (await accepted.Content.ReadAsStringAsync(AbortToken)).Should().Contain("connectionToken");
    }

    [Fact]
    public async Task reads_the_mode_bound_from_configuration_when_the_host_builds_its_endpoints()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>(StringComparer.Ordinal) { ["Mode"] = "ApiKey", ["ApiKey"] = "secret" }
            )
            .Build();
        await using var app = await _StartAsync(
            services => services.AddDashboardAuthentication(_Name, configuration),
            endpoints => endpoints.MapGet(_Path, _UserName).RequireDashboardAuthentication(_Name)
        );
        using var client = app.GetTestClient();

        (await _SendAsync(client, _Path, authorization: null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await _SendAsync(client, _Path, "Bearer secret")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task refuses_to_build_an_endpoint_for_an_unregistered_name()
    {
        // An unconfigured name reads as a default AuthConfig whose mode is None, which would serve the endpoint openly.
        // The host's authorization builds its endpoints while the pipeline is built, so the host never starts.
        var act = () =>
            _StartAsync(
                services => services.AddDashboardAuthentication("registered", _ApiKey("secret")),
                endpoints => endpoints.MapGet(_Path, _UserName).RequireDashboardAuthentication("typo")
            );

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*'typo'*AddDashboardAuthentication*");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void rejects_a_blank_authentication_name(string name)
    {
        var builder = Substitute.For<IEndpointConventionBuilder>();

        var act = () => builder.RequireDashboardAuthentication(name);

        act.Should().Throw<ArgumentException>();
    }

    private Task<WebApplication> _StartSingleAsync(Action<AuthConfig> configure)
    {
        return _StartAsync(
            services => services.AddDashboardAuthentication(_Name, configure),
            endpoints => endpoints.MapGet(_Path, _UserName).RequireDashboardAuthentication(_Name)
        );
    }

    private Task<WebApplication> _StartHostModeAsync(string? policy)
    {
        return _StartAsync(
            services =>
            {
                services
                    .AddAuthentication(TokenHandler.SchemeName)
                    .AddScheme<AuthenticationSchemeOptions, TokenHandler>(TokenHandler.SchemeName, _ => { });
                services
                    .AddAuthorizationBuilder()
                    .AddPolicy(TokenHandler.Policy, builder => builder.RequireRole("operator"));
                services.AddDashboardAuthentication(
                    _Name,
                    auth =>
                    {
                        auth.Mode = AuthMode.Host;
                        auth.HostAuthorizationPolicy = policy;
                    }
                );
            },
            endpoints => endpoints.MapGet(_Path, _UserName).RequireDashboardAuthentication(_Name)
        );
    }

    private async Task<WebApplication> _StartAsync(
        Action<IServiceCollection> configureServices,
        Action<WebApplication> mapEndpoints,
        Action<WebApplication>? configurePipeline = null
    )
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(_logs);
        configureServices(builder.Services);

        var app = builder.Build();
        (
            configurePipeline
            ?? (
                pipeline =>
                {
                    pipeline.UseAuthentication();
                    pipeline.UseAuthorization();
                }
            )
        )(app);
        app.MapGet(_HostProbePath, (HttpContext context) => "host:" + _NameOf(context));
        mapEndpoints(app);

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

    // The base AuthenticationHandler writes its own Information entries under the same category.
    private string[] _FailureWarnings()
    {
        return
        [
            .. _logs
                .Entries.Where(entry =>
                    string.Equals(entry.Category, _HandlerCategory, StringComparison.Ordinal)
                    && entry.Level == LogLevel.Warning
                )
                .Select(entry => entry.Message),
        ];
    }

    private static string _UserName(HttpContext context) => _NameOf(context);

    private static string _NameOf(HttpContext context)
    {
        return context.User.Identity is { IsAuthenticated: true, Name: { } name } ? name : "<anonymous>";
    }

    private static Action<AuthConfig> _Basic(string username, string password)
    {
        return auth =>
        {
            auth.Mode = AuthMode.Basic;
            auth.BasicCredentials = $"{username}:{password}".ToBase64();
        };
    }

    private static Action<AuthConfig> _ApiKey(string key)
    {
        return auth =>
        {
            auth.Mode = AuthMode.ApiKey;
            auth.ApiKey = key;
        };
    }

    private async Task<HttpResponseMessage> _SendAsync(
        HttpClient client,
        string path,
        string? authorization,
        HttpMethod? method = null
    )
    {
        using var request = new HttpRequestMessage(method ?? HttpMethod.Get, new Uri(path, UriKind.Relative));
        if (authorization is not null)
        {
            request.Headers.TryAddWithoutValidation("Authorization", authorization);
        }

        return await client.SendAsync(request, AbortToken);
    }

    /// <summary>
    /// Signs in <c>Authorization: Token &lt;role&gt;</c> as a user named for, and in, that role.
    /// </summary>
    private sealed class TokenHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder
    ) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "Token";
        public const string Policy = "Operators";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var header = Request.Headers.Authorization.ToString();
            if (!header.StartsWith("Token ", StringComparison.Ordinal))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var role = header["Token ".Length..];
            var identity = new ClaimsIdentity([new(ClaimTypes.Name, role), new(ClaimTypes.Role, role)], SchemeName);

            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new(identity), SchemeName)));
        }
    }

    private sealed record LogEntry(string Category, LogLevel Level, string Message);

    /// <summary>Records every log entry the host writes; the requests of one test run on its own host.</summary>
    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<LogEntry> _entries = new();

        public IReadOnlyCollection<LogEntry> Entries => _entries;

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, _entries);

        public void Dispose() { }

        private sealed class CapturingLogger(string category, ConcurrentQueue<LogEntry> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter
            )
            {
                entries.Enqueue(new LogEntry(category, logLevel, formatter(state, exception)));
            }
        }
    }
}
