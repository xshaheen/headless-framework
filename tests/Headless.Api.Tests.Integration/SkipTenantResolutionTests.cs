// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Headless.Abstractions;
using Headless.Api;
using Headless.Api.Middlewares;
using Headless.Api.MultiTenancy;
using Headless.Constants;
using Headless.MultiTenancy;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class SkipTenantResolutionTests : TestBase
{
    private const string _Scheme = "Test";
    private const string _UserHeader = "X-Test-User";
    private const string _TenantHeader = "X-Test-Tenant";
    private const string _UnauthenticatedHeader = "X-Test-Unauthenticated";

    [Fact]
    public async Task should_not_mutate_current_tenant_when_endpoint_marked_skip_resolution_and_claim_present()
    {
        await using var app = await _CreateAppAsync();
        using var client = _CreateClient(app);

        var result = await _GetTenantAsync(client, "/skip-minimal", user: "alice", tenantId: "TENANT-1");

        result.Id.Should().BeNull();
        result.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task should_not_mutate_current_tenant_when_route_group_marked_skip_resolution_and_claim_present()
    {
        await using var app = await _CreateAppAsync();
        using var client = _CreateClient(app);

        var result = await _GetTenantAsync(client, "/skip-group/child", user: "alice", tenantId: "TENANT-1");

        result.Id.Should().BeNull();
        result.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task should_not_mutate_current_tenant_when_mvc_controller_marked_skip_resolution_and_claim_present()
    {
        await using var app = await _CreateAppAsync();
        using var client = _CreateClient(app);

        var result = await _GetTenantAsync(client, "/skip-mvc-class/action", user: "alice", tenantId: "TENANT-1");

        result.Id.Should().BeNull();
        result.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task should_not_mutate_current_tenant_when_mvc_action_marked_skip_resolution_and_claim_present()
    {
        await using var app = await _CreateAppAsync();
        using var client = _CreateClient(app);

        var result = await _GetTenantAsync(client, "/skip-mvc-action/skip", user: "alice", tenantId: "TENANT-1");

        result.Id.Should().BeNull();
        result.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task should_not_mutate_current_tenant_when_endpoint_marked_skip_resolution_and_unauthenticated()
    {
        await using var app = await _CreateAppAsync();
        using var client = _CreateClient(app);

        var result = await _GetTenantAsync(client, "/skip-minimal");

        result.Id.Should().BeNull();
        result.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task should_not_emit_middleware_ordering_warning_when_endpoint_marked_skip_resolution_and_unauthenticated()
    {
        _ResetOrderingWarning();
        var loggerProvider = new CapturingLoggerProvider();
        await using var app = await _CreateAppAsync(
            applyTenantMiddlewareBeforeAuthentication: true,
            loggerProvider: loggerProvider
        );
        using var client = _CreateClient(app);

        // Unauthenticated request to a skipped endpoint — middleware exits before the ordering-warning branch.
        await _GetTenantAsync(client, "/skip-minimal");

        loggerProvider
            .Entries.Should()
            .NotContain(entry => entry.EventId.Name == "HEADLESS_TENANCY_MIDDLEWARE_ORDERING");
    }

    [Fact]
    public async Task should_mutate_current_tenant_for_unmarked_endpoint_when_claim_present()
    {
        await using var app = await _CreateAppAsync();
        using var client = _CreateClient(app);

        var result = await _GetTenantAsync(client, "/unmarked", user: "alice", tenantId: "TENANT-1");

        result.Id.Should().Be("TENANT-1");
        result.IsAvailable.Should().BeTrue();
    }

    [Fact]
    public async Task should_mutate_current_tenant_for_unmarked_endpoint_in_route_group_that_does_not_skip()
    {
        await using var app = await _CreateAppAsync();
        using var client = _CreateClient(app);

        var result = await _GetTenantAsync(client, "/unmarked-group/child", user: "alice", tenantId: "TENANT-1");

        result.Id.Should().Be("TENANT-1");
        result.IsAvailable.Should().BeTrue();
    }

    [Fact]
    public async Task should_compose_skip_resolution_with_allow_missing_tenant_independently()
    {
        await using var app = await _CreateAppWithAuthorizationAsync();
        using var client = _CreateClient(app);

        // SkipTenantResolution bypasses claim extraction; AllowMissingTenant satisfies TenantRequirement.
        // Authenticated user with no tenant claim must reach the endpoint and observe a null tenant.
        using var response = await _SendAsync(client, "/skip-and-allow-missing", user: "alice");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<TenantResponse>(cancellationToken: AbortToken);
        result!.Id.Should().BeNull();
        result.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task should_set_tenancy_resolution_applied_feature_when_endpoint_marked_skip_resolution()
    {
        await using var app = await _CreateAppAsync();
        using var client = _CreateClient(app);

        var result = await _GetFeatureCheckAsync(client, "/skip-feature-check", user: "alice", tenantId: "TENANT-1");

        result.ResolutionApplied.Should().BeTrue();
        result.Id.Should().BeNull();
        result.IsAvailable.Should().BeFalse();
    }

    // --- app factories ---

    private async Task<WebApplication> _CreateAppAsync(
        bool applyTenantMiddlewareBeforeAuthentication = false,
        ILoggerProvider? loggerProvider = null
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

        builder.AddHeadless(configureServices: options =>
        {
            options.Validation.ValidateServiceProviderOnStartup = false;
            options.Validation.RequireUseHeadless = false;
            options.Validation.RequireMapHeadlessEndpoints = false;
            options.OpenTelemetry.Enabled = false;
            options.OpenApi.Enabled = false;
        });

        builder
            .Services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = _Scheme;
                options.DefaultChallengeScheme = _Scheme;
            })
            .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(_Scheme, _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddControllers().AddApplicationPart(typeof(SkipResolutionClassController).Assembly);

        var app = builder.Build();

        if (applyTenantMiddlewareBeforeAuthentication)
        {
            app.UseTenantResolution();
        }

        app.UseAuthentication();

        if (!applyTenantMiddlewareBeforeAuthentication)
        {
            app.UseTenantResolution();
        }

        app.UseAuthorization();

        app.MapGet("/skip-minimal", (ICurrentTenant t) => Results.Json(new TenantResponse(t.Id, t.IsAvailable)))
            .SkipTenantResolution();

        var skipGroup = app.MapGroup("/skip-group").SkipTenantResolution();
        skipGroup.MapGet("/child", (ICurrentTenant t) => Results.Json(new TenantResponse(t.Id, t.IsAvailable)));

        app.MapGet("/unmarked", (ICurrentTenant t) => Results.Json(new TenantResponse(t.Id, t.IsAvailable)));

        var unmarkedGroup = app.MapGroup("/unmarked-group");
        unmarkedGroup.MapGet("/child", (ICurrentTenant t) => Results.Json(new TenantResponse(t.Id, t.IsAvailable)));

        // Probes that HeadlessTenancyResolutionApplied is set even when the skip path is taken.
        app.MapGet(
                "/skip-feature-check",
                (HttpContext ctx, ICurrentTenant t) =>
                {
                    var applied = ctx.Features.Get<HeadlessTenancyResolutionApplied>() is not null;
                    return Results.Json(new FeatureCheckResponse(t.Id, t.IsAvailable, applied));
                }
            )
            .SkipTenantResolution();

        app.MapControllers();

        await app.StartAsync(AbortToken);
        return app;
    }

    private async Task<WebApplication> _CreateAppWithAuthorizationAsync()
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = EnvironmentNames.Test }
        );
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        _AddDefaultHeadlessSecurityConfiguration(builder.Configuration);

        builder.AddHeadless(configureServices: options =>
        {
            options.Validation.ValidateServiceProviderOnStartup = false;
            options.Validation.RequireUseHeadless = false;
            options.Validation.RequireMapHeadlessEndpoints = false;
            options.OpenTelemetry.Enabled = false;
            options.OpenApi.Enabled = false;
        });
        builder.AddHeadlessTenancy(tenancy =>
            tenancy.Http(http => http.ResolveFromClaims()).Authorization(auth => auth.RequireTenant())
        );

        builder
            .Services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = _Scheme;
                options.DefaultChallengeScheme = _Scheme;
                options.DefaultForbidScheme = _Scheme;
            })
            .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(_Scheme, _ => { });
        builder.Services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder(_Scheme)
                .RequireAuthenticatedUser()
                .AddRequirements(new TenantRequirement())
                .Build();
        });

        var app = builder.Build();
        app.UseAuthentication();
        app.UseHeadlessTenancy();
        app.UseAuthorization();

        app.MapGet(
                "/skip-and-allow-missing",
                (ICurrentTenant t) => Results.Json(new TenantResponse(t.Id, t.IsAvailable))
            )
            .SkipTenantResolution()
            .AllowMissingTenant();

        await app.StartAsync(AbortToken);
        return app;
    }

    // --- helpers ---

    private HttpClient _CreateClient(WebApplication app)
    {
        return new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
    }

    private async Task<TenantResponse> _GetTenantAsync(
        HttpClient client,
        string path,
        string? user = null,
        string? tenantId = null
    )
    {
        using var response = await _SendAsync(client, path, user, tenantId);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TenantResponse>(cancellationToken: AbortToken))!;
    }

    private async Task<FeatureCheckResponse> _GetFeatureCheckAsync(
        HttpClient client,
        string path,
        string? user = null,
        string? tenantId = null
    )
    {
        using var response = await _SendAsync(client, path, user, tenantId);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<FeatureCheckResponse>(cancellationToken: AbortToken))!;
    }

    private async Task<HttpResponseMessage> _SendAsync(
        HttpClient client,
        string path,
        string? user = null,
        string? tenantId = null
    )
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);

        if (user is not null)
        {
            request.Headers.Add(_UserHeader, user);
        }

        if (tenantId is not null)
        {
            request.Headers.Add(_TenantHeader, tenantId);
        }

        return await client.SendAsync(request, AbortToken);
    }

    private static void _ResetOrderingWarning()
    {
        var field = typeof(TenantResolutionMiddleware).GetField(
            "_orderingWarningEmitted",
            BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly
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

    private sealed record FeatureCheckResponse(string? Id, bool IsAvailable, bool ResolutionApplied);

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

            var identity = new ClaimsIdentity(claims, authenticationType: isUnauthenticated ? null : Scheme.Name);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, Scheme.Name);

            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}

[ApiController]
[SkipTenantResolution]
[Route("skip-mvc-class")]
public sealed class SkipResolutionClassController : ControllerBase
{
    [HttpGet("action")]
    public IActionResult GetTenant([FromServices] ICurrentTenant currentTenant)
    {
        return Ok(new { currentTenant.Id, currentTenant.IsAvailable });
    }
}

[ApiController]
[Route("skip-mvc-action")]
public sealed class SkipResolutionActionController : ControllerBase
{
    [HttpGet("skip")]
    [SkipTenantResolution]
    public IActionResult Skip([FromServices] ICurrentTenant currentTenant)
    {
        return Ok(new { currentTenant.Id, currentTenant.IsAvailable });
    }
}
