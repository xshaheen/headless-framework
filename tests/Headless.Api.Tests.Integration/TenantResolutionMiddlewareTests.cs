// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Headless.Abstractions;
using Headless.Api;
using Headless.Api.Middlewares;
using Headless.Constants;
using Headless.MultiTenancy;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class TenantResolutionMiddlewareTests : TestBase
{
    private const string _Scheme = "Test";
    private const string _UserHeader = "X-Test-User";
    private const string _TenantHeader = "X-Test-Tenant";
    private const string _CustomTenantHeader = "X-Test-Custom-Tenant";
    private const string _UnauthenticatedHeader = "X-Test-Unauthenticated";

    [Fact]
    public async Task should_resolve_default_tenant_claim_and_not_bleed_between_requests()
    {
        await using var app = await _CreateAppAsync();
        using var client = _CreateClient(app);

        var authenticated = await _GetTenantAsync(client, user: "alice", tenantId: "TENANT-1");
        var unauthenticated = await _GetTenantAsync(client);
        var missingClaim = await _GetTenantAsync(client, user: "alice");

        authenticated.Id.Should().Be("TENANT-1");
        authenticated.IsAvailable.Should().BeTrue();
        unauthenticated.Id.Should().BeNull();
        unauthenticated.IsAvailable.Should().BeFalse();
        missingClaim.Id.Should().BeNull();
        missingClaim.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task should_resolve_custom_tenant_claim_type_when_configured()
    {
        await using var app = await _CreateAppAsync(options => options.ClaimType = "custom_tenant_id");
        using var client = _CreateClient(app);

        var tenant = await _GetTenantAsync(client, user: "alice", customTenantId: "TENANT-42");

        tenant.Id.Should().Be("TENANT-42");
        tenant.IsAvailable.Should().BeTrue();
    }

    [Fact]
    public async Task should_skip_resolution_for_unauthenticated_principal_even_when_tenant_claim_exists()
    {
        await using var app = await _CreateAppAsync();
        using var client = _CreateClient(app);

        var tenant = await _GetTenantAsync(client, user: "alice", tenantId: "TENANT-1", unauthenticated: true);

        tenant.Id.Should().BeNull();
        tenant.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task should_skip_resolution_for_whitespace_tenant_claim()
    {
        await using var app = await _CreateAppAsync();
        using var client = _CreateClient(app);

        var tenant = await _GetTenantAsync(client, user: "alice", tenantId: "   ");

        tenant.Id.Should().BeNull();
        tenant.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task should_fall_back_to_default_claim_type_when_custom_claim_type_is_blank()
    {
        await using var app = await _CreateAppAsync(options => options.ClaimType = " ");
        using var client = _CreateClient(app);

        var tenant = await _GetTenantAsync(client, user: "alice", tenantId: "TENANT-1");

        tenant.Id.Should().Be("TENANT-1");
        tenant.IsAvailable.Should().BeTrue();
    }

    [Fact]
    public async Task should_resolve_tenant_claim_through_use_headless_tenancy_when_http_tenancy_configured()
    {
        await using var app = await _CreateAppAsync(setup: TenancySetup.RootHttp);
        using var client = _CreateClient(app);

        var tenant = await _GetTenantAsync(client, user: "alice", tenantId: "TENANT-1");

        tenant.Id.Should().Be("TENANT-1");
        tenant.IsAvailable.Should().BeTrue();
    }

    [Fact]
    public async Task should_not_run_authentication_from_use_headless_tenancy()
    {
        await using var app = await _CreateAppAsync(
            setup: TenancySetup.RootHttp,
            addInfrastructure: false,
            registerAuthentication: false,
            useAuthentication: false,
            useAuthorization: false
        );
        using var client = _CreateClient(app);

        var tenant = await _GetTenantAsync(client, user: "alice", tenantId: "TENANT-1");

        tenant.Id.Should().BeNull();
        tenant.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task should_noop_use_headless_tenancy_when_http_tenancy_is_not_configured()
    {
        await using var app = await _CreateAppAsync(setup: TenancySetup.RootNoHttp);
        using var client = _CreateClient(app);

        var tenant = await _GetTenantAsync(client, user: "alice", tenantId: "TENANT-1");

        tenant.Id.Should().BeNull();
        tenant.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public void should_throw_when_use_headless_tenancy_is_called_without_root_tenancy()
    {
        // given
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = EnvironmentNames.Test }
        );
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.AddHeadlessInfrastructure();
        using var app = builder.Build();

        // when
        var act = () => app.UseHeadlessTenancy();

        // then
        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*UseHeadlessTenancy()*")
            .WithMessage("*AddHeadlessTenancy*");
    }

    [Fact]
    public async Task should_throw_startup_diagnostic_when_http_tenancy_is_configured_without_use_headless_tenancy()
    {
        await using var app = await _CreateAppAsync(
            setup: TenancySetup.RootHttp,
            applyTenantMiddleware: false,
            start: false
        );

        Func<Task> act = () => app.StartAsync(AbortToken);

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*HEADLESS_TENANCY_HTTP_MIDDLEWARE_MISSING*")
            .WithMessage("*UseHeadlessTenancy*");
    }

    [Fact]
    public async Task should_not_emit_ordering_warning_for_correctly_ordered_anonymous_request()
    {
        _ResetOrderingWarning();
        var loggerProvider = new CapturingLoggerProvider();
        await using var app = await _CreateAppAsync(loggerProvider: loggerProvider);
        using var client = _CreateClient(app);

        await _GetTenantAsync(client);

        loggerProvider
            .Entries.Should()
            .NotContain(entry => entry.EventId.Name == "HEADLESS_TENANCY_MIDDLEWARE_ORDERING");
    }

    [Fact]
    public async Task should_emit_ordering_warning_when_tenant_resolution_runs_before_authentication()
    {
        _ResetOrderingWarning();
        var loggerProvider = new CapturingLoggerProvider();
        await using var app = await _CreateAppAsync(
            applyTenantMiddlewareBeforeAuthentication: true,
            loggerProvider: loggerProvider
        );
        using var client = _CreateClient(app);

        var tenant = await _GetTenantAsync(client, user: "alice", tenantId: "TENANT-1");

        tenant.Id.Should().BeNull();
        tenant.IsAvailable.Should().BeFalse();
        loggerProvider
            .Entries.Should()
            .ContainSingle(entry =>
                entry.EventId.Name == "HEADLESS_TENANCY_MIDDLEWARE_ORDERING" && entry.Level == LogLevel.Warning
            );
    }

    private async Task<WebApplication> _CreateAppAsync(
        Action<MultiTenancyOptions>? configure = null,
        TenancySetup setup = TenancySetup.Direct,
        bool addInfrastructure = true,
        bool registerAuthentication = true,
        bool useAuthentication = true,
        bool useAuthorization = true,
        bool applyTenantMiddleware = true,
        bool applyTenantMiddlewareBeforeAuthentication = false,
        ILoggerProvider? loggerProvider = null,
        bool start = true
    )
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = EnvironmentNames.Test }
        );
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        _AddDefaultHeadlessSecurityConfiguration(builder.Configuration);

        if (loggerProvider is not null)
        {
            builder.Logging.ClearProviders();
            builder.Logging.AddProvider(loggerProvider);
        }

        if (addInfrastructure)
        {
            builder.AddHeadlessInfrastructure();
        }

        if (setup == TenancySetup.RootHttp)
        {
            builder.AddHeadlessTenancy(tenancy => tenancy.Http(http => http.ResolveFromClaims(configure)));
        }
        else if (setup == TenancySetup.RootNoHttp)
        {
            builder.AddHeadlessTenancy(_ => { });
        }
        else if (configure is not null)
        {
            builder.AddHeadlessMultiTenancy(configure);
        }

        if (registerAuthentication)
        {
            builder
                .Services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = _Scheme;
                    options.DefaultChallengeScheme = _Scheme;
                })
                .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(_Scheme, _ => { });
        }

        if (useAuthorization)
        {
            builder.Services.AddAuthorization();
        }

        var app = builder.Build();

        if (applyTenantMiddleware && applyTenantMiddlewareBeforeAuthentication)
        {
            _UseTenantMiddleware(app, setup);
        }

        if (useAuthentication)
        {
            app.UseAuthentication();
        }

        if (applyTenantMiddleware && !applyTenantMiddlewareBeforeAuthentication)
        {
            _UseTenantMiddleware(app, setup);
        }

        if (useAuthorization)
        {
            app.UseAuthorization();
        }

        app.MapGet(
            "/tenant",
            (ICurrentTenant currentTenant) =>
                Results.Json(new TenantResponse(currentTenant.Id, currentTenant.IsAvailable))
        );

        if (start)
        {
            await app.StartAsync(AbortToken);
        }

        return app;
    }

    private HttpClient _CreateClient(WebApplication app)
    {
        return new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
    }

    private async Task<TenantResponse> _GetTenantAsync(
        HttpClient client,
        string? user = null,
        string? tenantId = null,
        string? customTenantId = null,
        bool unauthenticated = false
    )
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/tenant");

        if (user is not null)
        {
            request.Headers.Add(_UserHeader, user);
        }

        if (tenantId is not null)
        {
            request.Headers.Add(_TenantHeader, tenantId);
        }

        if (customTenantId is not null)
        {
            request.Headers.Add(_CustomTenantHeader, customTenantId);
        }

        if (unauthenticated)
        {
            request.Headers.Add(_UnauthenticatedHeader, "true");
        }

        using var response = await client.SendAsync(request, AbortToken);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<TenantResponse>(cancellationToken: AbortToken))!;
    }

    private static void _UseTenantMiddleware(WebApplication app, TenancySetup setup)
    {
        if (setup is TenancySetup.RootHttp or TenancySetup.RootNoHttp)
        {
            app.UseHeadlessTenancy();
        }
        else
        {
            app.UseTenantResolution();
        }
    }

    private static void _ResetOrderingWarning()
    {
        var field = typeof(TenantResolutionMiddleware).GetField(
            "_orderingWarningEmitted",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        field.Should().NotBeNull();
        field!.SetValue(null, 0);
    }

    private static void _AddDefaultHeadlessSecurityConfiguration(IConfigurationBuilder configuration)
    {
        configuration.AddInMemoryCollection([
            new KeyValuePair<string, string?>("Headless:StringEncryption:DefaultPassPhrase", "TestPassPhrase123456"),
            new KeyValuePair<string, string?>("Headless:StringEncryption:InitVectorBytes", "VGVzdElWMDEyMzQ1Njc4OQ=="),
            new KeyValuePair<string, string?>("Headless:StringEncryption:DefaultSalt", "VGVzdFNhbHQ="),
            new KeyValuePair<string, string?>("Headless:StringHash:DefaultSalt", "TestSalt"),
        ]);
    }

    private sealed record TenantResponse(string? Id, bool IsAvailable);

    private sealed record LogEntry(string Category, LogLevel Level, EventId EventId, string Message);

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<LogEntry> _entries = new();

        public IReadOnlyCollection<LogEntry> Entries => _entries.ToArray();

        public ILogger CreateLogger(string categoryName)
        {
            return new CapturingLogger(categoryName, _entries);
        }

        public void Dispose() { }
    }

    private sealed class CapturingLogger(string categoryName, ConcurrentQueue<LogEntry> entries) : ILogger
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
            entries.Enqueue(new LogEntry(categoryName, logLevel, eventId, formatter(state, exception)));
        }
    }

    private enum TenancySetup
    {
        Direct,
        RootHttp,
        RootNoHttp,
    }

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder
    ) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(_UserHeader, out var userValues))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var isUnauthenticated = Request.Headers.ContainsKey(_UnauthenticatedHeader);
            var claims = new List<Claim> { new(UserClaimTypes.Name, userValues.ToString()) };

            if (Request.Headers.TryGetValue(_TenantHeader, out var tenantValues))
            {
                claims.Add(new Claim(UserClaimTypes.TenantId, tenantValues.ToString()));
            }

            if (Request.Headers.TryGetValue(_CustomTenantHeader, out var customTenantValues))
            {
                claims.Add(new Claim("custom_tenant_id", customTenantValues.ToString()));
            }

            var identity = new ClaimsIdentity(claims, authenticationType: isUnauthenticated ? null : Scheme.Name);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, Scheme.Name);

            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
