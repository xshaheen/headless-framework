// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Headless.Abstractions;
using Headless.Api;
using Headless.Api.Middlewares;
using Headless.Api.MultiTenancy;
using Headless.Api.ServiceDefaults;
using Headless.Caching;
using Headless.Constants;
using Headless.MultiTenancy;
using Headless.MultiTenancy.Resources;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tests.Helpers;

namespace Tests;

public sealed class TenantCatalogResolutionMiddlewareTests : TestBase
{
    private const string IdentifierHeader = "X-Test-Catalog-Identifier";
    private const string SecondaryIdentifierHeader = "X-Test-Catalog-Identifier-2";
    private const string SecondarySchemeTenantHeader = "X-Test-Secondary-Tenant";

    [Fact]
    public async Task should_resolve_ambient_tenant_and_expose_feature_when_identifier_maps_to_enabled_tenant()
    {
        await using var app = await _CreateAppAsync();
        using var client = HttpTenancyTestHarness.CreateClient(app);

        var tenant = await _GetTenantAsync(client, identifier: "acme");

        tenant.Id.Should().Be("ten_123");
        tenant.IsAvailable.Should().BeTrue();
    }

    [Fact]
    public async Task should_resolve_for_unauthenticated_request_proving_pre_auth_placement()
    {
        await using var app = await _CreateAppAsync();
        using var client = HttpTenancyTestHarness.CreateClient(app);

        // No X-Test-User header at all -> TestAuthenticationHandler returns NoResult(); the request is
        // unauthenticated end to end. Resolution must still succeed since catalog resolution runs before
        // UseAuthentication().
        var tenant = await _GetTenantAsync(client, identifier: "acme", user: null);

        tenant.Id.Should().Be("ten_123");
        tenant.IsAvailable.Should().BeTrue();
    }

    [Fact]
    public async Task should_resolve_case_insensitively()
    {
        await using var app = await _CreateAppAsync();
        using var client = HttpTenancyTestHarness.CreateClient(app);

        var tenant = await _GetTenantAsync(client, identifier: "ACME");

        tenant.Id.Should().Be("ten_123");
    }

    [Fact]
    public async Task should_continue_as_host_context_for_ignored_identifier()
    {
        await using var app = await _CreateAppAsync();
        using var client = HttpTenancyTestHarness.CreateClient(app);

        var tenant = await _GetTenantAsync(client, identifier: "www");

        tenant.Id.Should().BeNull();
        tenant.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task should_noop_when_no_source_produces_an_identifier()
    {
        await using var app = await _CreateAppAsync();
        using var client = HttpTenancyTestHarness.CreateClient(app);

        var tenant = await _GetTenantAsync(client, identifier: null);

        tenant.Id.Should().BeNull();
        tenant.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task should_leave_claim_only_request_untouched_when_catalog_configured_but_no_identifier_resolves()
    {
        // R5/R8: a configured catalog with no identifier resolved this request must not disturb the
        // existing claim-only flow.
        await using var app = await _CreateAppAsync(alsoResolveFromClaims: true);
        using var client = HttpTenancyTestHarness.CreateClient(app);

        var tenant = await _GetTenantAsync(client, identifier: null, user: "alice", tenantId: "ten_123");

        tenant.Id.Should().Be("ten_123");
        tenant.IsAvailable.Should().BeTrue();
    }

    [Fact]
    public async Task should_return_generic_rejection_for_unknown_identifier_by_default()
    {
        await using var app = await _CreateAppAsync();
        using var client = HttpTenancyTestHarness.CreateClient(app);

        using var response = await _SendAsync(client, identifier: "ghost");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await response.Content.ReadAsStringAsync(AbortToken);
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("error")
            .GetProperty("code")
            .GetString()
            .Should()
            .Be(TenancyErrorCodes.ResolutionFailed);
    }

    [Fact]
    public async Task should_return_byte_identical_generic_rejection_for_unknown_and_disabled_by_default()
    {
        await using var app = await _CreateAppAsync();
        using var client = HttpTenancyTestHarness.CreateClient(app);

        using var unknownResponse = await _SendAsync(client, identifier: "ghost");
        using var disabledResponse = await _SendAsync(client, identifier: "disabled-co");

        unknownResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        disabledResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var unknownBody = _StripVolatile(await unknownResponse.Content.ReadAsStringAsync(AbortToken));
        var disabledBody = _StripVolatile(await disabledResponse.Content.ReadAsStringAsync(AbortToken));

        unknownBody.Should().Be(disabledBody);
    }

    [Fact]
    public async Task should_return_granular_codes_when_diagnostics_enabled()
    {
        await using var app = await _CreateAppAsync(detailedResolutionErrors: true);
        using var client = HttpTenancyTestHarness.CreateClient(app);

        using var unknownResponse = await _SendAsync(client, identifier: "ghost");
        using var disabledResponse = await _SendAsync(client, identifier: "disabled-co");

        unknownResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var unknownBody = await unknownResponse.Content.ReadAsStringAsync(AbortToken);
        using var unknownDoc = JsonDocument.Parse(unknownBody);
        unknownDoc
            .RootElement.GetProperty("error")
            .GetProperty("code")
            .GetString()
            .Should()
            .Be(TenancyErrorCodes.Unknown);

        disabledResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var disabledBody = await disabledResponse.Content.ReadAsStringAsync(AbortToken);
        using var disabledDoc = JsonDocument.Parse(disabledBody);
        disabledDoc
            .RootElement.GetProperty("error")
            .GetProperty("code")
            .GetString()
            .Should()
            .Be(TenancyErrorCodes.Disabled);
    }

    [Fact]
    public async Task should_reject_invalid_identifier_with_bad_request()
    {
        await using var app = await _CreateAppAsync();
        using var client = HttpTenancyTestHarness.CreateClient(app);

        using var response = await _SendAsync(client, identifier: new string('a', 200));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync(AbortToken);
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("error")
            .GetProperty("code")
            .GetString()
            .Should()
            .Be(TenancyErrorCodes.IdentifierInvalid);
    }

    [Fact]
    public async Task should_surface_500_when_store_faults_instead_of_a_tenant_code()
    {
        await using var app = await _CreateAppAsync(useThrowingStore: true);
        using var client = HttpTenancyTestHarness.CreateClient(app);

        using var response = await _SendAsync(client, identifier: "acme");

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task should_not_run_resolution_for_skip_tenant_resolution_endpoint_and_emit_no_warning()
    {
        TenantCatalogResolutionMiddleware.ResetOrderingWarningForTesting();
        using var loggerProvider = new CapturingLoggerProvider();
        await using var app = await _CreateAppAsync(loggerProvider: loggerProvider);
        using var client = HttpTenancyTestHarness.CreateClient(app);

        var tenant = await _GetTenantAsync(client, identifier: "acme", path: "/skip-catalog");

        tenant.Id.Should().BeNull();
        tenant.IsAvailable.Should().BeFalse();
        loggerProvider
            .Entries.Should()
            .NotContain(entry => entry.EventId.Name == "HEADLESS_TENANT_CATALOG_MIDDLEWARE_ORDERING");
    }

    [Fact]
    public async Task should_emit_ordering_warning_once_when_endpoint_is_null_before_use_routing()
    {
        TenantCatalogResolutionMiddleware.ResetOrderingWarningForTesting();
        using var loggerProvider = new CapturingLoggerProvider();
        await using var app = await _CreateAppAsync(loggerProvider: loggerProvider, applyBeforeUseRouting: true);
        using var client = HttpTenancyTestHarness.CreateClient(app);

        var tenant = await _GetTenantAsync(client, identifier: "acme");

        // Ordering misconfigured: routing has not run yet, so no endpoint metadata is resolvable and
        // resolution never runs — the request continues as host context, and identity resolution never
        // engaged for this call.
        tenant.Id.Should().BeNull();
        loggerProvider
            .Entries.Should()
            .ContainSingle(entry => entry.EventId.Name == "HEADLESS_TENANT_CATALOG_MIDDLEWARE_ORDERING");
    }

    [Fact]
    public async Task should_pass_authenticated_request_when_catalog_only_host_claim_matches_resolved_tenant()
    {
        await using var app = await _CreateAppAsync(requireAuthenticatedUser: true);
        using var client = HttpTenancyTestHarness.CreateClient(app);

        using var response = await _SendAsync(client, identifier: "acme", user: "alice", tenantId: "ten_123");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task should_reject_authenticated_request_when_catalog_only_host_claim_mismatches_resolved_tenant()
    {
        await using var app = await _CreateAppAsync(requireAuthenticatedUser: true);
        using var client = HttpTenancyTestHarness.CreateClient(app);

        using var response = await _SendAsync(client, identifier: "acme", user: "alice", tenantId: "ten_999");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await response.Content.ReadAsStringAsync(AbortToken);
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("error")
            .GetProperty("code")
            .GetString()
            .Should()
            .Be(TenancyErrorCodes.ResolutionFailed);
    }

    [Fact]
    public async Task should_reject_mismatch_at_granular_status_when_diagnostics_enabled()
    {
        await using var app = await _CreateAppAsync(requireAuthenticatedUser: true, detailedResolutionErrors: true);
        using var client = HttpTenancyTestHarness.CreateClient(app);

        using var response = await _SendAsync(client, identifier: "acme", user: "alice", tenantId: "ten_999");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadAsStringAsync(AbortToken);
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("error")
            .GetProperty("code")
            .GetString()
            .Should()
            .Be(TenancyErrorCodes.IdentifierMismatch);
    }

    [Fact]
    public async Task should_reject_mismatch_in_combined_pipeline_with_resolve_from_claims_also_active()
    {
        await using var app = await _CreateAppAsync(alsoResolveFromClaims: true, requireAuthenticatedUser: true);
        using var client = HttpTenancyTestHarness.CreateClient(app);

        using var response = await _SendAsync(client, identifier: "acme", user: "alice", tenantId: "ten_999");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await response.Content.ReadAsStringAsync(AbortToken);
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("error")
            .GetProperty("code")
            .GetString()
            .Should()
            .Be(TenancyErrorCodes.ResolutionFailed);
    }

    [Fact]
    public async Task should_pass_combined_pipeline_when_claim_matches_resolved_identifier()
    {
        await using var app = await _CreateAppAsync(alsoResolveFromClaims: true, requireAuthenticatedUser: true);
        using var client = HttpTenancyTestHarness.CreateClient(app);

        using var response = await _SendAsync(client, identifier: "acme", user: "alice", tenantId: "ten_123");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<TenantCatalogResponse>(cancellationToken: AbortToken);
        body!.Id.Should().Be("ten_123");
        body!.Name.Should().Be("Acme Inc");
    }

    [Fact]
    public async Task should_reject_mismatch_for_non_default_authentication_scheme()
    {
        await using var app = await _CreateAppAsync(requireAuthenticatedUser: true, useSecondaryScheme: true);
        using var client = HttpTenancyTestHarness.CreateClient(app);

        using var response = await _SendAsync(client, identifier: "acme", user: "alice", tenantId: "ten_999");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task should_not_emit_ordering_warning_for_an_unmatched_route_on_a_correctly_ordered_pipeline()
    {
        // An ordinary 404 probe also reaches this middleware with a null endpoint. That must not be
        // mistaken for a misordered pipeline, nor consume the one-shot warning slot a genuinely
        // misordered host depends on.
        TenantCatalogResolutionMiddleware.ResetOrderingWarningForTesting();
        using var loggerProvider = new CapturingLoggerProvider();
        await using var app = await _CreateAppAsync(loggerProvider: loggerProvider);
        using var client = HttpTenancyTestHarness.CreateClient(app);

        using var response = await _SendAsync(client, identifier: "acme", path: "/no-such-route");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        loggerProvider
            .Entries.Should()
            .NotContain(entry => entry.EventId.Name == "HEADLESS_TENANT_CATALOG_MIDDLEWARE_ORDERING");
    }

    [Fact]
    public async Task should_use_the_first_registered_source_when_several_sources_produce_an_identifier()
    {
        await using var app = await _CreateAppAsync(registerSecondIdentifierSource: true);
        using var client = HttpTenancyTestHarness.CreateClient(app);

        // Both sources yield an identifier, and they disagree: the first registration must win.
        var tenant = await _GetTenantAsync(client, identifier: "acme", secondaryIdentifier: "disabled-co");

        tenant.Id.Should().Be("ten_123");
        tenant.IsAvailable.Should().BeTrue();
    }

    [Fact]
    public async Task should_fall_through_to_the_next_source_when_the_first_returns_null()
    {
        await using var app = await _CreateAppAsync(registerSecondIdentifierSource: true);
        using var client = HttpTenancyTestHarness.CreateClient(app);

        var tenant = await _GetTenantAsync(client, identifier: null, secondaryIdentifier: "acme");

        tenant.Id.Should().Be("ten_123");
        tenant.IsAvailable.Should().BeTrue();
    }

    [Fact]
    public async Task should_reject_mismatching_claim_on_an_allow_anonymous_endpoint()
    {
        // AuthorizationMiddleware never evaluates handlers for [AllowAnonymous] endpoints, so this only
        // holds because R19 is enforced independently of authorization evaluation.
        await using var app = await _CreateAppAsync(requireAuthenticatedUser: true);
        using var client = HttpTenancyTestHarness.CreateClient(app);

        using var response = await _SendAsync(
            client,
            identifier: "acme",
            user: "alice",
            tenantId: "ten_999",
            path: "/anonymous-tenant"
        );

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await response.Content.ReadAsStringAsync(AbortToken);
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("error")
            .GetProperty("code")
            .GetString()
            .Should()
            .Be(TenancyErrorCodes.ResolutionFailed);
    }

    [Fact]
    public async Task should_reject_mismatching_claim_when_endpoint_has_no_authorize_metadata_and_no_fallback_policy()
    {
        // No authorize metadata and no FallbackPolicy means AuthorizationMiddleware combines a null
        // policy and calls next() without evaluating a single handler.
        await using var app = await _CreateAppAsync(requireAuthenticatedUser: false);
        using var client = HttpTenancyTestHarness.CreateClient(app);

        using var response = await _SendAsync(client, identifier: "acme", user: "alice", tenantId: "ten_999");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await response.Content.ReadAsStringAsync(AbortToken);
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("error")
            .GetProperty("code")
            .GetString()
            .Should()
            .Be(TenancyErrorCodes.ResolutionFailed);
    }

    [Fact]
    public async Task should_reject_mismatch_visible_only_to_an_endpoint_scoped_scheme()
    {
        // The default scheme authenticates alice without a tenant claim, so the mismatch surfaces only
        // when PolicyEvaluator authenticates "secondary" — the authorization-handler path.
        await using var app = await _CreateAppAsync(requireAuthenticatedUser: true, useIsolatedSecondaryScheme: true);
        using var client = HttpTenancyTestHarness.CreateClient(app);

        using var response = await _SendAsync(
            client,
            identifier: "acme",
            user: "alice",
            secondarySchemeTenantId: "ten_999"
        );

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await response.Content.ReadAsStringAsync(AbortToken);
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("error")
            .GetProperty("code")
            .GetString()
            .Should()
            .Be(TenancyErrorCodes.ResolutionFailed);
    }

    [Fact]
    public async Task should_keep_mismatch_rejection_byte_identical_to_unknown_when_forbid_is_not_a_bare_403()
    {
        // A cookie-style scheme forbids with a 302, not a 403. If the rewrite were gated on a bare 403,
        // the mismatch would stay distinguishable from the unknown/disabled rejection — exactly the
        // enumeration signal the secure-by-default collapse exists to remove.
        await using var app = await _CreateAppAsync(
            requireAuthenticatedUser: true,
            useIsolatedSecondaryScheme: true,
            redirectOnForbid: true
        );
        using var client = HttpTenancyTestHarness.CreateClient(app, allowAutoRedirect: false);

        using var mismatchResponse = await _SendAsync(
            client,
            identifier: "acme",
            user: "alice",
            secondarySchemeTenantId: "ten_999"
        );
        using var unknownResponse = await _SendAsync(client, identifier: "ghost", user: "alice");

        mismatchResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        unknownResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        mismatchResponse.Headers.Location.Should().BeNull();

        var mismatchBody = _StripVolatile(await mismatchResponse.Content.ReadAsStringAsync(AbortToken));
        var unknownBody = _StripVolatile(await unknownResponse.Content.ReadAsStringAsync(AbortToken));

        mismatchBody.Should().Be(unknownBody);
    }

    [Fact]
    public async Task should_reject_mismatch_when_authorization_resource_is_not_the_http_context()
    {
        // The SuppressUseHttpContextAsAuthorizationResource AppContext switch makes AuthorizationMiddleware
        // pass the Endpoint as the resource. Exercised directly rather than through the process-global
        // switch, which would leak into every other test running in parallel.
        var httpContext = new DefaultHttpContext();
        httpContext.Features.Set(new TenantIdentifierResolvedFeature("ten_123"));

        var handler = new TenantIdentifierIntegrityHandler(
            Options.Create(new MultiTenancyOptions()),
            new HttpContextAccessor { HttpContext = httpContext }
        );

        var authorizationContext = new AuthorizationHandlerContext(
            [new TenantRequirement()],
            _CreatePrincipal(tenantId: "ten_999"),
            resource: new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(), "test")
        );

        await handler.HandleAsync(authorizationContext);

        authorizationContext.HasFailed.Should().BeTrue();
        httpContext.Features.Get<TenantIdentifierMismatchFeature>().Should().NotBeNull();
    }

    [Fact]
    public async Task should_not_fail_authorization_when_the_claim_matches_and_the_resource_is_not_the_http_context()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Features.Set(new TenantIdentifierResolvedFeature("ten_123"));

        var handler = new TenantIdentifierIntegrityHandler(
            Options.Create(new MultiTenancyOptions()),
            new HttpContextAccessor { HttpContext = httpContext }
        );

        var authorizationContext = new AuthorizationHandlerContext(
            [new TenantRequirement()],
            _CreatePrincipal(tenantId: "ten_123"),
            resource: new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(), "test")
        );

        await handler.HandleAsync(authorizationContext);

        authorizationContext.HasFailed.Should().BeFalse();
        httpContext.Features.Get<TenantIdentifierMismatchFeature>().Should().BeNull();
    }

    private static ClaimsPrincipal _CreatePrincipal(string tenantId)
    {
        var identity = new ClaimsIdentity(
            [new Claim(UserClaimTypes.Name, "alice"), new Claim(UserClaimTypes.TenantId, tenantId)],
            authenticationType: HttpTenancyTestHarness.Scheme
        );

        return new ClaimsPrincipal(identity);
    }

    // --- app factory ---

    private async Task<WebApplication> _CreateAppAsync(
        bool detailedResolutionErrors = false,
        bool useThrowingStore = false,
        ILoggerProvider? loggerProvider = null,
        bool applyBeforeUseRouting = false,
        bool requireAuthenticatedUser = false,
        bool alsoResolveFromClaims = false,
        bool useSecondaryScheme = false,
        bool useIsolatedSecondaryScheme = false,
        bool redirectOnForbid = false,
        bool registerSecondIdentifierSource = false
    )
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = EnvironmentNames.Test }
        );
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        HttpTenancyTestHarness.AddDefaultHeadlessSecurityConfiguration(builder.Configuration);

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
            options.Validation.RequireStatusCodesRewriter = false;
            options.OpenTelemetry.Enabled = false;
            options.OpenApi.Enabled = false;
        });

        builder.Services.AddHeadlessCaching(caching => caching.UseInMemory());

        builder.AddHeadlessTenancy(tenancy =>
        {
            tenancy.Catalog(catalog =>
                catalog.UseInMemory(o =>
                {
                    o.Tenants.Add(new TenantInfo("ten_123", "acme", "Acme Inc", isEnabled: true));
                    o.Tenants.Add(new TenantInfo("ten_456", "disabled-co", "Disabled Co", isEnabled: false));
                })
            );

            tenancy.Http(http =>
            {
                if (alsoResolveFromClaims)
                {
                    http.ResolveFromClaims();
                }

                http.ResolveFromCatalog(catalogHttp =>
                {
                    catalogHttp.AddSource(new HeaderTenantIdentifierSource(IdentifierHeader));

                    if (registerSecondIdentifierSource)
                    {
                        catalogHttp.AddSource(new HeaderTenantIdentifierSource(SecondaryIdentifierHeader));
                    }
                });
            });
        });

        builder.Services.Configure<TenantCatalogOptions>(o =>
        {
            o.DetailedResolutionErrors = detailedResolutionErrors;
            o.IgnoredIdentifiers = ["www"];
        });

        if (useThrowingStore)
        {
            builder.Services.Replace(ServiceDescriptor.Singleton<ITenantStore>(new ThrowingTenantStore()));
        }

        builder.Services.AddTestAuthentication(registerForbidScheme: requireAuthenticatedUser);

        if (useSecondaryScheme)
        {
            builder
                .Services.AddAuthentication()
                .AddScheme<AuthenticationSchemeOptions, SecondarySchemeHandler>("secondary", _ => { });
        }

        if (useIsolatedSecondaryScheme)
        {
            builder
                .Services.AddAuthentication()
                .AddScheme<IsolatedSchemeOptions, IsolatedSecondarySchemeHandler>(
                    "secondary",
                    o => o.RedirectOnForbid = redirectOnForbid
                );
        }

        if (requireAuthenticatedUser)
        {
            builder
                .Services.AddAuthorizationBuilder()
                .SetFallbackPolicy(
                    useSecondaryScheme || useIsolatedSecondaryScheme
                        ? new AuthorizationPolicyBuilder("secondary").RequireAuthenticatedUser().Build()
                        : new AuthorizationPolicyBuilder(HttpTenancyTestHarness.Scheme)
                            .RequireAuthenticatedUser()
                            .Build()
                );
        }
        else
        {
            builder.Services.AddAuthorization();
        }

        var app = builder.Build();

        // Must wrap UseAuthentication()/UseAuthorization() (registered before them) so it observes the
        // bare 401/403 they produce on the way back out — mirrors TenantRequirementTests' working order.
        app.UseStatusCodesRewriter();

        if (applyBeforeUseRouting)
        {
            // Deliberately misordered: the resolution middleware runs before UseRouting(), so no
            // endpoint has been resolved yet — exercises the null-endpoint misorder-warning path.
            app.UseHeadlessTenantCatalogResolution();
            app.UseRouting();
        }
        else
        {
            app.UseRouting();
            app.UseHeadlessTenantCatalogResolution();
        }

        app.UseAuthentication();

        if (alsoResolveFromClaims)
        {
            app.UseHeadlessTenancy();
        }

        app.UseAuthorization();

        app.MapGet(
            "/tenant",
            (HttpContext ctx, ICurrentTenant currentTenant) =>
                Results.Json(new TenantCatalogResponse(currentTenant.Id, currentTenant.IsAvailable, currentTenant.Name))
        );

        app.MapGet(
                "/skip-catalog",
                (ICurrentTenant currentTenant) =>
                    Results.Json(new TenantCatalogResponse(currentTenant.Id, currentTenant.IsAvailable))
            )
            .SkipTenantResolution();

        // AuthorizationMiddleware short-circuits before evaluating any handler for [AllowAnonymous]
        // endpoints, so R19 on this route can only hold if enforcement does not depend on authorization
        // running at all.
        app.MapGet(
                "/anonymous-tenant",
                (ICurrentTenant currentTenant) =>
                    Results.Json(new TenantCatalogResponse(currentTenant.Id, currentTenant.IsAvailable))
            )
            .AllowAnonymous();

        await app.StartAsync(AbortToken);
        return app;
    }

    private async Task<TenantCatalogResponse> _GetTenantAsync(
        HttpClient client,
        string? identifier,
        string? user = "alice",
        string? tenantId = null,
        string path = "/tenant",
        string? secondaryIdentifier = null
    )
    {
        using var response = await _SendAsync(client, identifier, user, tenantId, path, secondaryIdentifier);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TenantCatalogResponse>(cancellationToken: AbortToken))!;
    }

    private async Task<HttpResponseMessage> _SendAsync(
        HttpClient client,
        string? identifier,
        string? user = "alice",
        string? tenantId = null,
        string path = "/tenant",
        string? secondaryIdentifier = null,
        string? secondarySchemeTenantId = null
    )
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);

        if (identifier is not null)
        {
            request.Headers.Add(IdentifierHeader, identifier);
        }

        if (secondaryIdentifier is not null)
        {
            request.Headers.Add(SecondaryIdentifierHeader, secondaryIdentifier);
        }

        if (user is not null)
        {
            request.Headers.Add(HttpTenancyTestHarness.UserHeader, user);
        }

        if (tenantId is not null)
        {
            request.Headers.Add(HttpTenancyTestHarness.TenantHeader, tenantId);
        }

        if (secondarySchemeTenantId is not null)
        {
            request.Headers.Add(SecondarySchemeTenantHeader, secondarySchemeTenantId);
        }

        return await client.SendAsync(request, AbortToken);
    }

    private static string _StripVolatile(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        using var stream = new MemoryStream();

        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();

            foreach (var property in root.EnumerateObject())
            {
                if (
                    property.NameEquals("traceId")
                    || property.NameEquals("timestamp")
                    || property.NameEquals("instance")
                    || property.NameEquals("buildNumber")
                    || property.NameEquals("commitNumber")
                )
                {
                    continue;
                }

                property.WriteTo(writer);
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }
}

internal sealed record TenantCatalogResponse(string? Id, bool IsAvailable, string? Name = null);

internal sealed class HeaderTenantIdentifierSource(string headerName) : ITenantIdentifierSource
{
    public string? GetIdentifier(HttpContext context)
    {
        return context.Request.Headers.TryGetValue(headerName, out var values) ? values.ToString() : null;
    }
}

internal sealed class ThrowingTenantStore : ITenantStore
{
    public Task<TenantInfo?> FindByIdentifierAsync(
        string normalizedIdentifier,
        CancellationToken cancellationToken = default
    )
    {
        throw new InvalidOperationException("Simulated store fault.");
    }

    public Task<TenantInfo?> FindByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException("Simulated store fault.");
    }
}

internal sealed class IsolatedSchemeOptions : AuthenticationSchemeOptions
{
    /// <summary>Forbids with a 302 redirect rather than a bare 403, the way a cookie scheme does.</summary>
    public bool RedirectOnForbid { get; set; }
}

/// <summary>
/// An endpoint-scoped scheme that reads its tenant claim from a header the default scheme ignores, so
/// the mismatching claim only becomes visible once <c>PolicyEvaluator</c> authenticates it inside
/// authorization — the one path where <c>TenantIdentifierIntegrityHandler</c> is the sole R19 enforcement.
/// </summary>
internal sealed class IsolatedSecondarySchemeHandler(
    IOptionsMonitor<IsolatedSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder
) : AuthenticationHandler<IsolatedSchemeOptions>(options, logger, encoder)
{
    public const string TenantHeader = "X-Test-Secondary-Tenant";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(HttpTenancyTestHarness.UserHeader, out var userValues))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new List<Claim> { new(UserClaimTypes.Name, userValues.ToString()) };

        if (Request.Headers.TryGetValue(TenantHeader, out var tenantValues))
        {
            claims.Add(new Claim(UserClaimTypes.TenantId, tenantValues.ToString()));
        }

        var identity = new ClaimsIdentity(claims, authenticationType: Scheme.Name);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        if (!Options.RedirectOnForbid)
        {
            return base.HandleForbiddenAsync(properties);
        }

        Response.Redirect("/forbidden");

        return Task.CompletedTask;
    }
}

internal sealed class SecondarySchemeHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder
) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(HttpTenancyTestHarness.UserHeader, out var userValues))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new List<Claim> { new(UserClaimTypes.Name, userValues.ToString()) };

        if (Request.Headers.TryGetValue(HttpTenancyTestHarness.TenantHeader, out var tenantValues))
        {
            claims.Add(new Claim(UserClaimTypes.TenantId, tenantValues.ToString()));
        }

        var identity = new ClaimsIdentity(claims, authenticationType: Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
