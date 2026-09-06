// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Headless.Abstractions;
using Headless.Api;
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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tests.Helpers;

namespace Tests;

/// <summary>
/// Covers the posture contract between catalog identifier resolution and
/// <c>UseStatusCodesRewriter()</c>: the rewriter is what collapses the R19 authorization-tier mismatch
/// into the generic tenant rejection, so a host that omits it — or registers it downstream of
/// <c>UseAuthorization()</c>, where the short-circuited evaluation never reaches it — keeps the tenant
/// enumeration oracle the byte-identical rejection exists to close.
/// </summary>
public sealed class TenantCatalogRewriterPostureTests : TestBase
{
    private const string _IdentifierHeader = "X-Rewriter-Posture-Identifier";

    [Fact]
    public async Task should_fail_startup_when_a_catalog_resolution_host_never_calls_use_status_codes_rewriter()
    {
        // The marker UseStatusCodesRewriter() records travels from Headless.Api.Core to
        // TenantCatalogPostureValidator through the shared TenantPostureManifest; omitting the call leaves
        // it absent and the resolution posture unvalidated.
        await using var app = _CreateApp(RewriterPlacement.Omitted);

        var act = () => app.StartAsync(AbortToken);

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*CATALOG_RESOLUTION_WITHOUT_REWRITER*")
            .WithMessage("*UseStatusCodesRewriter()*");
    }

    [Fact]
    public async Task should_start_when_the_rewriter_is_registered_after_use_authorization_because_order_is_unobservable()
    {
        // The manifest records presence, not position — this host satisfies the startup diagnostic while
        // still being misordered, which is exactly why the exposure below needs a test of its own.
        await using var app = _CreateApp(RewriterPlacement.AfterAuthorization);

        var act = () => app.StartAsync(AbortToken);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task should_leave_the_r19_mismatch_distinguishable_from_an_unknown_tenant_when_the_rewriter_runs_after_authorization()
    {
        // A rewriter registered downstream of UseAuthorization() never observes the failed evaluation:
        // authorization short-circuits before calling next. The R19 mismatch therefore surfaces as the bare
        // forbid the scheme produced, while an unknown identifier still gets the generic 404 rejection the
        // resolution middleware writes itself — the two rejections stay distinguishable by status code
        // alone, which is the enumeration oracle. This asserts the exposure as it exists today.
        await using var app = _CreateApp(RewriterPlacement.AfterAuthorization);
        await app.StartAsync(AbortToken);
        using var client = HttpTenancyTestHarness.CreateClient(app);

        using var mismatchResponse = await _SendAsync(client, identifier: "acme", secondarySchemeTenantId: "ten_999");
        using var unknownResponse = await _SendAsync(client, identifier: "ghost", secondarySchemeTenantId: "ten_123");

        mismatchResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        unknownResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var mismatchBody = await mismatchResponse.Content.ReadAsStringAsync(AbortToken);
        mismatchBody.Should().NotContain(TenancyErrorCodes.ResolutionFailed);

        var unknownBody = await unknownResponse.Content.ReadAsStringAsync(AbortToken);
        using var unknownDocument = JsonDocument.Parse(unknownBody);
        unknownDocument
            .RootElement.GetProperty("error")
            .GetProperty("code")
            .GetString()
            .Should()
            .Be(TenancyErrorCodes.ResolutionFailed);
    }

    [Fact]
    public async Task should_keep_the_r19_mismatch_byte_identical_to_an_unknown_tenant_when_the_rewriter_wraps_authorization()
    {
        // The correctly ordered counterpart, so the assertion above reads as a defect of placement rather
        // than of the R19 design.
        await using var app = _CreateApp(RewriterPlacement.WrappingAuthorization);
        await app.StartAsync(AbortToken);
        using var client = HttpTenancyTestHarness.CreateClient(app);

        using var mismatchResponse = await _SendAsync(client, identifier: "acme", secondarySchemeTenantId: "ten_999");
        using var unknownResponse = await _SendAsync(client, identifier: "ghost", secondarySchemeTenantId: "ten_123");

        mismatchResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        unknownResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var mismatchBody = _StripVolatile(await mismatchResponse.Content.ReadAsStringAsync(AbortToken));
        var unknownBody = _StripVolatile(await unknownResponse.Content.ReadAsStringAsync(AbortToken));

        mismatchBody.Should().Be(unknownBody);
    }

    // --- app factory ---

    private enum RewriterPlacement
    {
        /// <summary>UseStatusCodesRewriter() is never called — the startup diagnostic's target.</summary>
        Omitted = 0,

        /// <summary>Registered before UseAuthentication()/UseAuthorization(), so it observes their rejections.</summary>
        WrappingAuthorization = 1,

        /// <summary>Registered downstream of UseAuthorization(), which short-circuits before reaching it.</summary>
        AfterAuthorization = 2,
    }

    private static WebApplication _CreateApp(RewriterPlacement placement)
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = EnvironmentNames.Test }
        );
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        HttpTenancyTestHarness.AddDefaultHeadlessSecurityConfiguration(builder.Configuration);

        builder.AddHeadless(configureServices: options =>
        {
            options.Validation.ValidateServiceProviderOnStartup = false;
            options.Validation.RequireUseHeadless = false;
            options.Validation.RequireMapHeadlessEndpoints = false;
            // Kept off so the ServiceDefaults presence check cannot stand in for the tenancy diagnostic
            // under test — a plain Headless.Api.Core catalog host has no ServiceDefaults check at all.
            options.Validation.RequireStatusCodesRewriter = false;
            options.OpenTelemetry.Enabled = false;
            options.OpenApi.Enabled = false;
        });

        builder.Services.AddHeadlessCaching(caching => caching.UseInMemory());

        builder.AddHeadlessTenancy(tenancy =>
        {
            tenancy.Catalog(catalog =>
                catalog.UseInMemory(o => o.Tenants.Add(new TenantInfo("ten_123", "acme", "Acme Inc", isEnabled: true)))
            );

            tenancy.Http(http =>
                http.ResolveFromCatalog(catalogHttp =>
                    catalogHttp.AddSource(new PostureHeaderTenantIdentifierSource(_IdentifierHeader))
                )
            );
        });

        // The default scheme deliberately never sees a tenant claim (it reads a header these tests do not
        // send), so the pre-auth R19 tier finds nothing to compare and the mismatch can only surface
        // through TenantIdentifierIntegrityHandler during authorization — the tier the rewriter serves.
        builder.Services.AddTestAuthentication(registerForbidScheme: true);
        builder
            .Services.AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, PostureSecondarySchemeHandler>("secondary", _ => { });

        builder
            .Services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder("secondary").RequireAuthenticatedUser().Build());

        var app = builder.Build();

        if (placement is RewriterPlacement.WrappingAuthorization)
        {
            app.UseStatusCodesRewriter();
        }

        app.UseRouting();
        app.UseHeadlessTenantCatalogResolution();
        app.UseAuthentication();
        app.UseAuthorization();

        if (placement is RewriterPlacement.AfterAuthorization)
        {
            app.UseStatusCodesRewriter();
        }

        // The endpoint body is irrelevant here: every assertion is about a rejection that short-circuits
        // before it runs.
        app.MapGet("/tenant", () => Results.NoContent());

        return app;
    }

    private async Task<HttpResponseMessage> _SendAsync(
        HttpClient client,
        string identifier,
        string secondarySchemeTenantId
    )
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/tenant");
        request.Headers.Add(_IdentifierHeader, identifier);
        request.Headers.Add(HttpTenancyTestHarness.UserHeader, "alice");
        request.Headers.Add(PostureSecondarySchemeHandler.TenantHeader, secondarySchemeTenantId);

        return await client.SendAsync(request, AbortToken);
    }

    private static string _StripVolatile(string json)
    {
        using var document = JsonDocument.Parse(json);
        using var stream = new MemoryStream();

        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();

            foreach (var property in document.RootElement.EnumerateObject())
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

internal sealed class PostureHeaderTenantIdentifierSource(string headerName) : ITenantIdentifierSource
{
    public string? GetIdentifier(HttpContext context)
    {
        return context.Request.Headers.TryGetValue(headerName, out var values) ? values.ToString() : null;
    }
}

/// <summary>
/// An endpoint-scoped scheme whose tenant claim comes from a header the default scheme ignores, so the
/// mismatching claim only becomes visible once <c>PolicyEvaluator</c> authenticates it inside
/// authorization — the one path whose rejection depends on <c>StatusCodesRewriterMiddleware</c>.
/// </summary>
internal sealed class PostureSecondarySchemeHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder
) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string TenantHeader = "X-Rewriter-Posture-Tenant";

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
}
