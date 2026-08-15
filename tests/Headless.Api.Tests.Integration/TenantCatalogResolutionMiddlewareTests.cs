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
    }

    [Fact]
    public async Task should_reject_mismatch_for_non_default_authentication_scheme()
    {
        await using var app = await _CreateAppAsync(requireAuthenticatedUser: true, useSecondaryScheme: true);
        using var client = HttpTenancyTestHarness.CreateClient(app);

        using var response = await _SendAsync(client, identifier: "acme", user: "alice", tenantId: "ten_999");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // --- app factory ---

    private async Task<WebApplication> _CreateAppAsync(
        bool detailedResolutionErrors = false,
        bool useThrowingStore = false,
        ILoggerProvider? loggerProvider = null,
        bool applyBeforeUseRouting = false,
        bool requireAuthenticatedUser = false,
        bool alsoResolveFromClaims = false,
        bool useSecondaryScheme = false
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
                    catalogHttp.AddSource(new HeaderTenantIdentifierSource(IdentifierHeader))
                );
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

        if (requireAuthenticatedUser)
        {
            builder
                .Services.AddAuthorizationBuilder()
                .SetFallbackPolicy(
                    useSecondaryScheme
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
                Results.Json(new TenantCatalogResponse(currentTenant.Id, currentTenant.IsAvailable))
        );

        app.MapGet(
                "/skip-catalog",
                (ICurrentTenant currentTenant) =>
                    Results.Json(new TenantCatalogResponse(currentTenant.Id, currentTenant.IsAvailable))
            )
            .SkipTenantResolution();

        await app.StartAsync(AbortToken);
        return app;
    }

    private async Task<TenantCatalogResponse> _GetTenantAsync(
        HttpClient client,
        string? identifier,
        string? user = "alice",
        string? tenantId = null,
        string path = "/tenant"
    )
    {
        using var response = await _SendAsync(client, identifier, user, tenantId, path);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TenantCatalogResponse>(cancellationToken: AbortToken))!;
    }

    private async Task<HttpResponseMessage> _SendAsync(
        HttpClient client,
        string? identifier,
        string? user = "alice",
        string? tenantId = null,
        string path = "/tenant"
    )
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);

        if (identifier is not null)
        {
            request.Headers.Add(IdentifierHeader, identifier);
        }

        if (user is not null)
        {
            request.Headers.Add(HttpTenancyTestHarness.UserHeader, user);
        }

        if (tenantId is not null)
        {
            request.Headers.Add(HttpTenancyTestHarness.TenantHeader, tenantId);
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

internal sealed record TenantCatalogResponse(string? Id, bool IsAvailable);

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
