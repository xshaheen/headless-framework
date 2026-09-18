// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Net.Http.Json;
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
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tests.Helpers;

namespace Tests;

/// <summary>
/// Pins the route source end to end through the real catalog pipeline: the <c>{tenant}</c> route
/// segment resolving the ambient tenant, ignored identifiers ending resolution with no store call,
/// the fail-closed unknown rejection with <c>Cache-Control: no-store</c>, and the misorder
/// escalation — a route source on a middleware placed before <c>UseRouting()</c> finds
/// nothing, every request runs as host context, and the deferred misorder path escalates to the
/// Error-level <c>HEADLESS_TENANT_CATALOG_ROUTE_SOURCE_MISORDERED</c> event once per process with
/// no request data.
/// </summary>
[Collection(TenantCatalogOrderingWarningCollection.Name)]
public sealed class RouteTenantIdentifierSourceTests : TestBase
{
    private const string MisorderedEventName = "HEADLESS_TENANT_CATALOG_ROUTE_SOURCE_MISORDERED";
    private const string OrderingWarningEventName = "HEADLESS_TENANT_CATALOG_MIDDLEWARE_ORDERING";

    [Fact]
    public async Task should_resolve_the_tenant_from_the_route_segment()
    {
        await using var app = await _CreateAppAsync();
        using var client = HttpTenancyTestHarness.CreateClient(app);

        var tenant = await _GetTenantAsync(client, "/acme/orders");

        tenant.Id.Should().Be("ten_123");
        tenant.Name.Should().Be("Acme Inc");
        tenant.IsAvailable.Should().BeTrue();
    }

    [Fact]
    public async Task should_honor_a_custom_route_value_name()
    {
        await using var app = await _CreateAppAsync(routeValueName: "org");
        using var client = HttpTenancyTestHarness.CreateClient(app);

        var tenant = await _GetTenantAsync(client, "/acme/orders");

        tenant.Id.Should().Be("ten_123");
    }

    [Fact]
    public async Task should_end_resolution_as_host_context_without_a_store_call_for_an_ignored_identifier()
    {
        var store = new CountingTenantStore();
        await using var app = await _CreateAppAsync(store: store, ignoredIdentifiers: ["www"]);
        using var client = HttpTenancyTestHarness.CreateClient(app);

        // www is an ignored identifier on the CATALOG options (no source-level list), so
        // resolution ends as host context; the seeded store must never be consulted.
        var tenant = await _GetTenantAsync(client, "/www/orders");

        tenant.Id.Should().BeNull();
        tenant.IsAvailable.Should().BeFalse();
        store.IdentifierLookups.Should().Be(0);
    }

    [Fact]
    public async Task should_return_the_fail_closed_404_with_no_store_for_an_unknown_route_identifier()
    {
        await using var app = await _CreateAppAsync();
        using var client = HttpTenancyTestHarness.CreateClient(app);

        using var response = await client.GetAsync("/ghost/orders", AbortToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Headers.CacheControl.Should().NotBeNull();
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        var body = await response.Content.ReadAsStringAsync(AbortToken);
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("error")
            .GetProperty("code")
            .GetString()
            .Should()
            .Be(TenancyErrorCodes.ResolutionFailed);
    }

    [Fact]
    public async Task should_emit_the_misorder_error_once_as_host_context_when_placed_before_use_routing()
    {
        // Routing has not run when the middleware consults its sources, so the route value
        // is absent, resolution finds nothing, and the endpoint executes as host context. The
        // deferred misorder path escalates to the Error-level dedicated event — once per process
        // even across two requests — and its message carries no path or route value.
        TenantCatalogResolutionMiddleware.ResetOrderingWarningForTesting();
        using var loggerProvider = new CapturingLoggerProvider();
        await using var app = await _CreateAppAsync(loggerProvider: loggerProvider, applyBeforeUseRouting: true);
        using var client = HttpTenancyTestHarness.CreateClient(app);

        var first = await _GetTenantAsync(client, "/acme/orders");
        var second = await _GetTenantAsync(client, "/acme/orders");

        first.Id.Should().BeNull();
        first.IsAvailable.Should().BeFalse();
        second.Id.Should().BeNull();

        var entry = loggerProvider.Entries.Should().ContainSingle(e => e.EventId.Name == MisorderedEventName).Subject;

        entry.Level.Should().Be(LogLevel.Error);
        entry.Message.Should().NotContain("acme"); // never the route value
        entry.Message.Should().NotContain("/acme"); // never the path
        entry.Message.Should().NotContain("/orders");

        loggerProvider.Entries.Should().NotContain(e => e.EventId.Name == OrderingWarningEventName);
    }

    [Fact]
    public async Task should_still_emit_the_ordering_warning_without_a_route_source_when_misordered()
    {
        // The Warning event stays exactly as it was for hosts with no route source; only a registered
        // route source escalates to the Error event.
        TenantCatalogResolutionMiddleware.ResetOrderingWarningForTesting();
        using var loggerProvider = new CapturingLoggerProvider();
        await using var app = await _CreateAppAsync(
            loggerProvider: loggerProvider,
            applyBeforeUseRouting: true,
            registerDelegateSourceInsteadOfRoute: true
        );
        using var client = HttpTenancyTestHarness.CreateClient(app);

        var tenant = await _GetTenantAsync(client, "/acme/orders");

        tenant.Id.Should().BeNull();
        loggerProvider.Entries.Should().ContainSingle(e => e.EventId.Name == OrderingWarningEventName);
        loggerProvider.Entries.Should().NotContain(e => e.EventId.Name == MisorderedEventName);
    }

    [Fact]
    public async Task should_not_let_an_unmatched_route_probe_consume_the_misorder_error_slot()
    {
        // A 404 probe on a misordered host leaves the endpoint null before AND after next(), so the
        // deferred detection cannot distinguish it from a correctly ordered pipeline and must not
        // burn the once-per-process slot — the subsequent matched request still gets the Error.
        TenantCatalogResolutionMiddleware.ResetOrderingWarningForTesting();
        using var loggerProvider = new CapturingLoggerProvider();
        await using var app = await _CreateAppAsync(loggerProvider: loggerProvider, applyBeforeUseRouting: true);
        using var client = HttpTenancyTestHarness.CreateClient(app);

        using var probe = await client.GetAsync("/acme/no-such-route", AbortToken);

        probe.StatusCode.Should().Be(HttpStatusCode.NotFound);
        loggerProvider.Entries.Should().NotContain(e => e.EventId.Name == MisorderedEventName);

        var tenant = await _GetTenantAsync(client, "/acme/orders");

        tenant.Id.Should().BeNull();
        loggerProvider.Entries.Should().ContainSingle(e => e.EventId.Name == MisorderedEventName);
    }

    // --- app factory and helpers ---

    private async Task<WebApplication> _CreateAppAsync(
        string routeValueName = RouteTenantIdentifierSourceOptions.DefaultRouteValueName,
        CountingTenantStore? store = null,
        IList<string>? ignoredIdentifiers = null,
        CapturingLoggerProvider? loggerProvider = null,
        bool applyBeforeUseRouting = false,
        bool registerDelegateSourceInsteadOfRoute = false
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
                })
            );

            tenancy.Http(http =>
                http.ResolveFromCatalog(sources =>
                {
                    if (registerDelegateSourceInsteadOfRoute)
                    {
                        // A non-route source so the Warning path (not the Error escalation) is the
                        // one under test; it deliberately yields nothing.
                        sources.AddSource(_ => null);
                    }
                    else if (
                        string.Equals(
                            routeValueName,
                            RouteTenantIdentifierSourceOptions.DefaultRouteValueName,
                            StringComparison.Ordinal
                        )
                    )
                    {
                        sources.AddRouteSource();
                    }
                    else
                    {
                        sources.AddRouteSource(routeValueName);
                    }
                })
            );
        });

        if (ignoredIdentifiers is not null)
        {
            builder.Services.PostConfigure<TenantCatalogOptions>(o => o.IgnoredIdentifiers = ignoredIdentifiers);
        }

        if (store is not null)
        {
            builder.Services.Replace(ServiceDescriptor.Singleton<ITenantStore>(store));
        }

        builder.Services.AddTestAuthentication();
        builder.Services.AddAuthorization();

        var app = builder.Build();

        app.UseStatusCodesRewriter();

        if (applyBeforeUseRouting)
        {
            // Deliberately misordered: the resolution middleware runs before UseRouting(), so no
            // endpoint (and therefore no route values) exist yet when sources are consulted.
            app.UseHeadlessTenantCatalogResolution();
            app.UseRouting();
        }
        else
        {
            app.UseRouting();
            app.UseHeadlessTenantCatalogResolution();
        }

        app.UseAuthentication();
        app.UseAuthorization();

        // The route pattern uses the configured route value name for the leading segment.
        var pattern = $"/{{{routeValueName}}}/orders";

        app.MapGet(
            pattern,
            (ICurrentTenant currentTenant) =>
                Results.Json(
                    new RouteTenantCatalogResponse(currentTenant.Id, currentTenant.IsAvailable, currentTenant.Name)
                )
        );

        await app.StartAsync(AbortToken);
        return app;
    }

    private async Task<RouteTenantCatalogResponse> _GetTenantAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path, AbortToken);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<RouteTenantCatalogResponse>(cancellationToken: AbortToken))!;
    }

    /// <summary>
    /// Replaces the in-memory store so a test can prove resolution never reached the catalog: every
    /// identifier lookup is counted.
    /// </summary>
    private sealed class CountingTenantStore : ITenantStore
    {
        public int IdentifierLookups { get; private set; }

        public Task<TenantInfo?> FindByIdentifierAsync(
            string normalizedIdentifier,
            CancellationToken cancellationToken = default
        )
        {
            IdentifierLookups++;
            return Task.FromResult<TenantInfo?>(null);
        }

        public Task<TenantInfo?> FindByIdAsync(string id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<TenantInfo?>(null);
        }
    }
}

internal sealed record RouteTenantCatalogResponse(string? Id, bool IsAvailable, string? Name = null);
