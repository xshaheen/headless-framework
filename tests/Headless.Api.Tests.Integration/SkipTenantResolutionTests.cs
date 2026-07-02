// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Net.Http.Json;
using Headless.Abstractions;
using Headless.Api;
using Headless.Api.Middlewares;
using Headless.Api.MultiTenancy;
using Headless.Constants;
using Headless.MultiTenancy;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tests.Helpers;

namespace Tests;

public sealed class SkipTenantResolutionTests : TestBase
{
    [Fact]
    public async Task should_not_mutate_current_tenant_when_endpoint_marked_skip_resolution_and_claim_present()
    {
        await using var app = await _CreateAppAsync();
        using var client = HttpTenancyTestHarness.CreateClient(app);

        var result = await _GetTenantAsync(client, "/skip-minimal", user: "alice", tenantId: "TENANT-1");

        result.Id.Should().BeNull();
        result.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task should_not_mutate_current_tenant_when_route_group_marked_skip_resolution_and_claim_present()
    {
        await using var app = await _CreateAppAsync();
        using var client = HttpTenancyTestHarness.CreateClient(app);

        var result = await _GetTenantAsync(client, "/skip-group/child", user: "alice", tenantId: "TENANT-1");

        result.Id.Should().BeNull();
        result.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task should_not_mutate_current_tenant_when_mvc_controller_marked_skip_resolution_and_claim_present()
    {
        await using var app = await _CreateAppAsync();
        using var client = HttpTenancyTestHarness.CreateClient(app);

        var result = await _GetTenantAsync(client, "/skip-mvc-class/action", user: "alice", tenantId: "TENANT-1");

        result.Id.Should().BeNull();
        result.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task should_not_mutate_current_tenant_when_mvc_action_marked_skip_resolution_and_claim_present()
    {
        await using var app = await _CreateAppAsync();
        using var client = HttpTenancyTestHarness.CreateClient(app);

        var result = await _GetTenantAsync(client, "/skip-mvc-action/skip", user: "alice", tenantId: "TENANT-1");

        result.Id.Should().BeNull();
        result.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task should_not_mutate_current_tenant_when_endpoint_marked_skip_resolution_and_unauthenticated()
    {
        await using var app = await _CreateAppAsync();
        using var client = HttpTenancyTestHarness.CreateClient(app);

        var result = await _GetTenantAsync(client, "/skip-minimal");

        result.Id.Should().BeNull();
        result.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task should_not_emit_middleware_ordering_warning_when_endpoint_marked_skip_resolution_and_unauthenticated()
    {
        TenantResolutionMiddleware.ResetOrderingWarningForTesting();
        var loggerProvider = new CapturingLoggerProvider();
        await using var app = await _CreateAppAsync(options =>
        {
            options.ApplyTenantMiddlewareBeforeAuthentication = true;
            options.LoggerProvider = loggerProvider;
        });
        using var client = HttpTenancyTestHarness.CreateClient(app);

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
        using var client = HttpTenancyTestHarness.CreateClient(app);

        var result = await _GetTenantAsync(client, "/unmarked", user: "alice", tenantId: "TENANT-1");

        result.Id.Should().Be("TENANT-1");
        result.IsAvailable.Should().BeTrue();
    }

    [Fact]
    public async Task should_mutate_current_tenant_for_unmarked_endpoint_in_route_group_that_does_not_skip()
    {
        await using var app = await _CreateAppAsync();
        using var client = HttpTenancyTestHarness.CreateClient(app);

        var result = await _GetTenantAsync(client, "/unmarked-group/child", user: "alice", tenantId: "TENANT-1");

        result.Id.Should().Be("TENANT-1");
        result.IsAvailable.Should().BeTrue();
    }

    [Fact]
    public async Task should_compose_skip_resolution_with_allow_missing_tenant_independently()
    {
        await using var app = await _CreateAppAsync(options => options.RequireTenancyAuthorization = true);
        using var client = HttpTenancyTestHarness.CreateClient(app);

        // SkipTenantResolution bypasses claim extraction; AllowMissingTenant satisfies TenantRequirement.
        // Send a tenant claim so the skip marker's claim-blocking is observable — the endpoint must reach the handler
        // and observe a null tenant despite the principal carrying TENANT-1.
        using var response = await _SendAsync(client, "/skip-and-allow-missing", user: "alice", tenantId: "TENANT-1");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<TenantResponse>(cancellationToken: AbortToken);
        result!.Id.Should().BeNull();
        result.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task should_set_tenancy_resolution_applied_feature_when_endpoint_marked_skip_resolution()
    {
        await using var app = await _CreateAppAsync();
        using var client = HttpTenancyTestHarness.CreateClient(app);

        var result = await _GetFeatureCheckAsync(client, "/skip-feature-check", user: "alice", tenantId: "TENANT-1");

        result.ResolutionApplied.Should().BeTrue();
        result.Id.Should().BeNull();
        result.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task should_return_403_when_skip_resolution_without_allow_missing_tenant_under_tenant_required_policy()
    {
        // SkipTenantResolution alone bypasses claim extraction but does NOT satisfy TenantRequirement.
        // Under a FallbackPolicy that requires a tenant, the request must fail authorization with 403.
        await using var app = await _CreateAppAsync(options => options.RequireTenancyAuthorization = true);
        using var client = HttpTenancyTestHarness.CreateClient(app);

        using var response = await _SendAsync(client, "/skip-only", user: "alice", tenantId: "TENANT-1");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var json = await response.Content.ReadAsStringAsync(AbortToken);
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("g:tenant_required");
    }

    [Fact]
    public async Task should_mutate_current_tenant_for_derived_mvc_controller_when_base_has_skip_marker_and_claim_present()
    {
        // [AttributeUsage(Inherited = false)] — derived controllers must NOT inherit SkipTenantResolution.
        await using var app = await _CreateAppAsync();
        using var client = HttpTenancyTestHarness.CreateClient(app);

        var result = await _GetTenantAsync(client, "/derived-skip/action", user: "alice", tenantId: "TENANT-1");

        result.Id.Should().Be("TENANT-1");
        result.IsAvailable.Should().BeTrue();
    }

    [Fact]
    public async Task should_mutate_current_tenant_for_unmarked_action_on_same_controller_when_claim_present()
    {
        // Method-level SkipTenantResolution must NOT bleed across sibling actions on the same controller.
        await using var app = await _CreateAppAsync();
        using var client = HttpTenancyTestHarness.CreateClient(app);

        var result = await _GetTenantAsync(client, "/skip-mvc-action/no-skip", user: "alice", tenantId: "TENANT-1");

        result.Id.Should().Be("TENANT-1");
        result.IsAvailable.Should().BeTrue();
    }

    // --- app factory ---

    private sealed class TestAppOptions
    {
        /// <summary>
        /// When true, registers HeadlessTenancy with claim-based HTTP resolution and a FallbackPolicy requiring a tenant.
        /// When false, the app wires authentication, raw <c>UseTenantResolution()</c>, and plain <c>AddAuthorization()</c>.
        /// </summary>
        public bool RequireTenancyAuthorization { get; set; }

        /// <summary>
        /// When true (and authorization is not enforced), the tenant middleware runs before authentication —
        /// used to exercise the ordering-warning branch.
        /// </summary>
        public bool ApplyTenantMiddlewareBeforeAuthentication { get; set; }

        public ILoggerProvider? LoggerProvider { get; set; }
    }

    private async Task<WebApplication> _CreateAppAsync(Action<TestAppOptions>? configure = null)
    {
        var options = new TestAppOptions();
        configure?.Invoke(options);

        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = EnvironmentNames.Test }
        );
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        HttpTenancyTestHarness.AddDefaultHeadlessSecurityConfiguration(builder.Configuration);

        if (options.LoggerProvider is not null)
        {
            builder.Logging.ClearProviders();
            builder.Logging.AddProvider(options.LoggerProvider);
        }

        builder.AddHeadless(configureServices: hl =>
        {
            hl.Validation.ValidateServiceProviderOnStartup = false;
            hl.Validation.RequireUseHeadless = false;
            hl.Validation.RequireMapHeadlessEndpoints = false;
            hl.Validation.RequireStatusCodesRewriter = false;
            hl.OpenTelemetry.Enabled = false;
            hl.OpenApi.Enabled = false;
        });

        if (options.RequireTenancyAuthorization)
        {
            builder.AddHeadlessTenancy(tenancy =>
                tenancy.Http(http => http.ResolveFromClaims()).Authorization(auth => auth.RequireTenant())
            );
        }

        builder.Services.AddTestAuthentication(registerForbidScheme: options.RequireTenancyAuthorization);

        if (options.RequireTenancyAuthorization)
        {
            builder
                .Services.AddAuthorizationBuilder()
                .SetFallbackPolicy(
                    new AuthorizationPolicyBuilder(HttpTenancyTestHarness.Scheme)
                        .RequireAuthenticatedUser()
                        .AddRequirements(new TenantRequirement())
                        .Build()
                );
        }
        else
        {
            builder.Services.AddAuthorization();
        }

        builder.Services.AddControllers().AddApplicationPart(typeof(SkipResolutionClassController).Assembly);

        var app = builder.Build();

        if (options.RequireTenancyAuthorization)
        {
            // Headless.Api.ServiceDefaults wires UseStatusCodesRewriter() automatically; tests
            // opt out of UseHeadless() via RequireUseHeadless = false, so wire it explicitly so
            // bare 403s produced by the default IAuthorizationMiddlewareResultHandler get
            // rewritten into the structured g:tenant_required ProblemDetails.
            app.UseStatusCodesRewriter();
            app.UseAuthentication();
            app.UseHeadlessTenancy();
            app.UseAuthorization();

            app.MapGet(
                    "/skip-and-allow-missing",
                    (ICurrentTenant t) => Results.Json(new TenantResponse(t.Id, t.IsAvailable))
                )
                .SkipTenantResolution()
                .AllowMissingTenant();

            // Same shape as /skip-and-allow-missing but missing AllowMissingTenant — must hit TenantRequirement
            // and produce 403 g:tenant_required when no tenant context is established.
            app.MapGet("/skip-only", (ICurrentTenant t) => Results.Json(new TenantResponse(t.Id, t.IsAvailable)))
                .SkipTenantResolution();
        }
        else
        {
            if (options.ApplyTenantMiddlewareBeforeAuthentication)
            {
                app.UseTenantResolution();
            }

            app.UseAuthentication();

            if (!options.ApplyTenantMiddlewareBeforeAuthentication)
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
        }

        await app.StartAsync(AbortToken);
        return app;
    }

    // --- helpers ---

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
        using var request = HttpTenancyTestHarness.CreateRequest(HttpMethod.Get, path, user, tenantId);
        return await client.SendAsync(request, AbortToken);
    }

    private sealed record FeatureCheckResponse(string? Id, bool IsAvailable, bool ResolutionApplied);
}

internal sealed record TenantResponse(string? Id, bool IsAvailable);

[ApiController]
[SkipTenantResolution]
[Route("skip-mvc-class")]
public sealed class SkipResolutionClassController : ControllerBase
{
    [HttpGet("action")]
    public IActionResult GetTenant([FromServices] ICurrentTenant currentTenant)
    {
        return Ok(new TenantResponse(currentTenant.Id, currentTenant.IsAvailable));
    }
}

[ApiController]
[SkipTenantResolution]
public abstract class SkipResolutionBaseController : ControllerBase;

// Derived controller — [AttributeUsage(Inherited = false)] means the SkipTenantResolution attribute on
// SkipResolutionBaseController must NOT propagate to this derived type.
[Route("derived-skip")]
public sealed class SkipResolutionDerivedController : SkipResolutionBaseController
{
    [HttpGet("action")]
    public IActionResult GetTenant([FromServices] ICurrentTenant currentTenant)
    {
        return Ok(new TenantResponse(currentTenant.Id, currentTenant.IsAvailable));
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
        return Ok(new TenantResponse(currentTenant.Id, currentTenant.IsAvailable));
    }

    // Sibling action (no SkipTenantResolution marker) — regression check that the [Method-level] marker
    // does not bleed across actions on the same controller.
    [HttpGet("no-skip")]
    public IActionResult NoSkip([FromServices] ICurrentTenant currentTenant)
    {
        return Ok(new TenantResponse(currentTenant.Id, currentTenant.IsAvailable));
    }
}
