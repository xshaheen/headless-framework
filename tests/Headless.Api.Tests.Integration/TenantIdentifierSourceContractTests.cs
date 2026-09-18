// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Net.Http.Json;
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
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tests.Helpers;

namespace Tests;

/// <summary>
/// Pins the source-contract semantics end to end over a real host: the three-state result loop
/// (None falls through, Found wins, Invalid rejects before any catalog call), and the
/// registration-order semantics across repeated <c>ResolveFromCatalog</c> passes.
/// </summary>
public sealed class TenantIdentifierSourceContractTests : TestBase
{
    private const string IdentifierHeader = "X-Test-Contract-Identifier";

    [Fact]
    public async Task should_reject_before_any_catalog_call_when_a_source_reports_invalid()
    {
        HeaderStubSource.Consultations = 0;
        var store = new CountingTenantStore();
        await using var app = await _CreateAppAsync(
            http =>
                http.ResolveFromCatalog(sources =>
                    sources.AddSource(new InvalidStubSource()).AddSource(new HeaderStubSource(IdentifierHeader))
                ),
            store
        );
        using var client = HttpTenancyTestHarness.CreateClient(app);

        using var response = await _SendAsync(client, "acme");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Headers.CacheControl.Should().NotBeNull();
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        var body = await response.Content.ReadAsStringAsync(AbortToken);
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("error")
            .GetProperty("code")
            .GetString()
            .Should()
            .Be(TenancyErrorCodes.IdentifierInvalid);

        // The rejection happens before any catalog call, and later sources never run.
        store.IdentifierLookups.Should().Be(0);
        HeaderStubSource.Consultations.Should().Be(0);
    }

    [Fact]
    public async Task should_fall_through_to_a_later_source_when_the_first_returns_none()
    {
        await using var app = await _CreateAppAsync(http =>
            http.ResolveFromCatalog(sources =>
                sources.AddSource(new NoneStubSource()).AddSource(new HeaderStubSource(IdentifierHeader))
            )
        );
        using var client = HttpTenancyTestHarness.CreateClient(app);

        var tenant = await _GetTenantAsync(client, "acme");

        tenant.Id.Should().Be("ten_123");
        tenant.IsAvailable.Should().BeTrue();
    }

    [Fact]
    public async Task should_keep_the_first_type_registration_position_across_two_resolve_from_catalog_passes()
    {
        // A shared library registers StubB first; the app then registers StubA and StubB
        // again. The effective order must stay B, A (first position kept, no duplicate B descriptor),
        // so B is consulted exactly once and StubA never runs.
        OrderFirstStubSource.Consultations = 0;
        OrderSecondStubSource.Consultations = 0;
        await using var app = await _CreateAppAsync(http =>
        {
            // Two ResolveFromCatalog passes share only the service collection: the first models a
            // shared library's registration, the second the app's own. Descriptor insertion order
            // across both passes is the resolution order.
            http.ResolveFromCatalog(sources => sources.AddSource<OrderSecondStubSource>());
            http.ResolveFromCatalog(sources =>
                sources.AddSource<OrderFirstStubSource>().AddSource<OrderSecondStubSource>()
            );
        });
        using var client = HttpTenancyTestHarness.CreateClient(app);

        var tenant = await _GetTenantAsync(client, identifier: null);

        // OrderFirstStubSource yields "disabled-co" — if it ever ran first, the response would be the
        // fail-closed disabled rejection instead of a resolved tenant.
        tenant.Id.Should().Be("ten_123");
        OrderFirstStubSource.Consultations.Should().Be(0);
        OrderSecondStubSource.Consultations.Should().Be(1);
    }

    // --- app factory and helpers ---

    private async Task<WebApplication> _CreateAppAsync(
        Action<HeadlessHttpTenancyBuilder> configureHttp,
        CountingTenantStore? store = null
    )
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

            tenancy.Http(configureHttp);
        });

        if (store is not null)
        {
            builder.Services.Replace(ServiceDescriptor.Singleton<ITenantStore>(store));
        }

        builder.Services.AddTestAuthentication();
        builder.Services.AddAuthorization();

        var app = builder.Build();

        // Mirrors the working order in TenantCatalogResolutionMiddlewareTests: the rewriter wraps the
        // rest of the pipeline, and the catalog resolution middleware sits between routing and auth.
        app.UseStatusCodesRewriter();

        app.UseRouting();
        app.UseHeadlessTenantCatalogResolution();
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapGet(
            "/tenant",
            (ICurrentTenant currentTenant) =>
                Results.Json(new TenantCatalogResponse(currentTenant.Id, currentTenant.IsAvailable, currentTenant.Name))
        );

        await app.StartAsync(AbortToken);
        return app;
    }

    private async Task<TenantCatalogResponse> _GetTenantAsync(HttpClient client, string? identifier)
    {
        using var response = await _SendAsync(client, identifier);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<TenantCatalogResponse>(cancellationToken: AbortToken))!;
    }

    private async Task<HttpResponseMessage> _SendAsync(HttpClient client, string? identifier)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/tenant");

        if (identifier is not null)
        {
            request.Headers.Add(IdentifierHeader, identifier);
        }

        return await client.SendAsync(request, AbortToken);
    }

    // --- stubs and doubles ---

    private sealed class NoneStubSource : ITenantIdentifierSource
    {
        public TenantIdentifierSourceResult GetIdentifier(HttpContext context) => TenantIdentifierSourceResult.None;
    }

    private sealed class InvalidStubSource : ITenantIdentifierSource
    {
        public TenantIdentifierSourceResult GetIdentifier(HttpContext context) => TenantIdentifierSourceResult.Invalid;
    }

    /// <summary>
    /// Yields "disabled-co" — a Found result whose catalog outcome is the fail-closed disabled
    /// rejection, so an ordering violation (this source running first) is observable in the response
    /// status, not only in a consultation counter.
    /// </summary>
    private sealed class OrderFirstStubSource : ITenantIdentifierSource
    {
        public static int Consultations;

        public TenantIdentifierSourceResult GetIdentifier(HttpContext context)
        {
            Consultations++;
            return TenantIdentifierSourceResult.Found("disabled-co");
        }
    }

    private sealed class OrderSecondStubSource : ITenantIdentifierSource
    {
        public static int Consultations;

        public TenantIdentifierSourceResult GetIdentifier(HttpContext context)
        {
            Consultations++;
            return TenantIdentifierSourceResult.Found("acme");
        }
    }

    private sealed class HeaderStubSource(string headerName) : ITenantIdentifierSource
    {
        public static int Consultations;

        public TenantIdentifierSourceResult GetIdentifier(HttpContext context)
        {
            Consultations++;

            return context.Request.Headers.TryGetValue(headerName, out var values)
                ? TenantIdentifierSourceResult.Found(values.ToString())
                : TenantIdentifierSourceResult.None;
        }
    }

    /// <summary>
    /// Replaces the in-memory store so a test can prove resolution never reached the catalog: every
    /// identifier lookup is counted, and the seeded acme tenant is returned so that an incorrectly
    /// continued pipeline would resolve rather than 404.
    /// </summary>
    private sealed class CountingTenantStore : ITenantStore
    {
        private readonly TenantInfo _acme = new("ten_123", "acme", "Acme Inc", isEnabled: true);

        public int IdentifierLookups { get; private set; }

        public Task<TenantInfo?> FindByIdentifierAsync(
            string normalizedIdentifier,
            CancellationToken cancellationToken = default
        )
        {
            IdentifierLookups++;
            return Task.FromResult(normalizedIdentifier == "acme" ? _acme : null);
        }

        public Task<TenantInfo?> FindByIdAsync(string id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(string.Equals(id, _acme.Id, StringComparison.Ordinal) ? _acme : null);
        }
    }
}
