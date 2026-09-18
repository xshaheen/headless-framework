// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net.Http.Json;
using Headless.Abstractions;
using Headless.Api;
using Headless.Api.MultiTenancy;
using Headless.Api.ServiceDefaults;
using Headless.Caching;
using Headless.Constants;
using Headless.MultiTenancy;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Tests.Helpers;

namespace Tests;

/// <summary>
/// Records, per link-generation surface, whether the ambient
/// <c>{tenant}</c> route value survives when linking to a DIFFERENT endpoint without an explicit
/// tenant value. Every "preserve" assertion states the desired behavior; before the
/// <see cref="TenantAmbientRouteValueLinkGenerator"/> decorator was introduced, five of them were
/// red (<c>Url.Action</c>, <c>GetPathByAction</c>, and <c>GetPathByName</c> from both origins), which
/// is the evidence that ASP.NET Core drops the value on those surfaces and that the decorator is
/// needed. The "explicit" and
/// "no ambient tenant" scenarios document the boundaries the decorator keeps: an explicit different
/// tenant always wins, and a request with no tenant segment has nothing to promote, so the link stays
/// <c>null</c>. The "registration" scenarios pin the opt-out and the once-only wrap.
/// </summary>
public sealed class TenantRouteLinkGenerationTests : TestBase
{
    // --- ambient tenant preserved (desired behavior; red = surface drops the value) ---

    [Fact]
    public async Task should_preserve_ambient_tenant_for_mvc_url_action_to_same_controller()
    {
        await using var app = await _CreateAppAsync();
        using var client = HttpTenancyTestHarness.CreateClient(app);

        var links = await _GetLinksAsync(client, "/acme/orders");

        links.SameControllerAction.Should().Be("/acme/orders/7");
    }

    [Fact]
    public async Task should_preserve_ambient_tenant_for_mvc_get_path_by_action_to_other_controller()
    {
        await using var app = await _CreateAppAsync();
        using var client = HttpTenancyTestHarness.CreateClient(app);

        var links = await _GetLinksAsync(client, "/acme/orders");

        links.OtherControllerAction.Should().Be("/acme/invoices");
    }

    [Fact]
    public async Task should_preserve_ambient_tenant_for_get_path_by_name_from_mvc_request()
    {
        await using var app = await _CreateAppAsync();
        using var client = HttpTenancyTestHarness.CreateClient(app);

        var links = await _GetLinksAsync(client, "/acme/orders");

        links.NamedEndpoint.Should().Be("/acme/invoices/summary");
    }

    [Fact]
    public async Task should_preserve_ambient_tenant_for_minimal_api_get_path_by_name_to_group_sibling()
    {
        await using var app = await _CreateAppAsync();
        using var client = HttpTenancyTestHarness.CreateClient(app);

        var links = await _GetLinksAsync(client, "/acme/links");

        links.NamedEndpoint.Should().Be("/acme/invoices/summary");
    }

    [Fact]
    public async Task should_preserve_ambient_tenant_for_minimal_api_get_path_by_action_to_mvc_controller()
    {
        await using var app = await _CreateAppAsync();
        using var client = HttpTenancyTestHarness.CreateClient(app);

        var links = await _GetLinksAsync(client, "/acme/links");

        links.OtherControllerAction.Should().Be("/acme/invoices");
    }

    [Fact]
    public async Task should_preserve_ambient_tenant_for_minimal_api_get_path_by_route_values_to_group_sibling()
    {
        // Supplementary evidence: the route-name address scheme (GetPathByRouteValues) is the one
        // that consumes ambient values, so its outcome bounds what a decorator can recommend.
        await using var app = await _CreateAppAsync();
        using var client = HttpTenancyTestHarness.CreateClient(app);

        var links = await _GetLinksAsync(client, "/acme/links");

        links.RouteValuesNamed.Should().Be("/acme/invoices/summary");
    }

    [Fact]
    public async Task should_preserve_ambient_tenant_for_mvc_get_path_by_route_values_to_named_endpoint()
    {
        // Supplementary evidence from the MVC origin for the same route-name address scheme.
        await using var app = await _CreateAppAsync();
        using var client = HttpTenancyTestHarness.CreateClient(app);

        var links = await _GetLinksAsync(client, "/acme/orders");

        links.RouteValuesNamed.Should().Be("/acme/invoices/summary");
    }

    // --- explicit different tenant wins on every surface ---

    [Fact]
    public async Task should_use_explicit_tenant_for_mvc_url_action()
    {
        await using var app = await _CreateAppAsync();
        using var client = HttpTenancyTestHarness.CreateClient(app);

        var links = await _GetLinksAsync(client, "/acme/orders");

        links.SameControllerActionExplicit.Should().Be("/globex/orders/7");
    }

    [Fact]
    public async Task should_use_explicit_tenant_for_mvc_get_path_by_action()
    {
        await using var app = await _CreateAppAsync();
        using var client = HttpTenancyTestHarness.CreateClient(app);

        var links = await _GetLinksAsync(client, "/acme/orders");

        links.OtherControllerActionExplicit.Should().Be("/globex/invoices");
    }

    [Fact]
    public async Task should_use_explicit_tenant_for_get_path_by_name_from_mvc_request()
    {
        await using var app = await _CreateAppAsync();
        using var client = HttpTenancyTestHarness.CreateClient(app);

        var links = await _GetLinksAsync(client, "/acme/orders");

        links.NamedEndpointExplicit.Should().Be("/globex/invoices/summary");
    }

    [Fact]
    public async Task should_use_explicit_tenant_for_minimal_api_get_path_by_name()
    {
        await using var app = await _CreateAppAsync();
        using var client = HttpTenancyTestHarness.CreateClient(app);

        var links = await _GetLinksAsync(client, "/acme/links");

        links.NamedEndpointExplicit.Should().Be("/globex/invoices/summary");
    }

    // --- no ambient tenant: nothing to promote, link stays null (documents the limit) ---

    [Fact]
    public async Task should_return_null_from_plain_mvc_request_without_explicit_tenant()
    {
        await using var app = await _CreateAppAsync();
        using var client = HttpTenancyTestHarness.CreateClient(app);

        var links = await _GetLinksAsync(client, "/plain-mvc");

        links.SameControllerAction.Should().BeNull();
        links.OtherControllerAction.Should().BeNull();
        links.NamedEndpoint.Should().BeNull();
    }

    [Fact]
    public async Task should_return_null_from_plain_minimal_api_request_without_explicit_tenant()
    {
        await using var app = await _CreateAppAsync();
        using var client = HttpTenancyTestHarness.CreateClient(app);

        var links = await _GetLinksAsync(client, "/plain");

        links.OtherControllerAction.Should().BeNull();
        links.NamedEndpoint.Should().BeNull();
        links.RouteValuesNamed.Should().BeNull();
    }

    [Fact]
    public async Task should_append_promoted_tenant_as_query_when_target_has_no_tenant_segment()
    {
        // Documented side effect of promotion: the promoted value is an explicit route value, and
        // ASP.NET Core appends explicit values the target template cannot bind as a query string.
        await using var app = await _CreateAppAsync();
        using var client = HttpTenancyTestHarness.CreateClient(app);

        var links = await _GetLinksAsync(client, "/acme/orders");

        links.PlainTarget.Should().Be("/plain-mvc?tenant=acme");
    }

    // --- registration: opt-out, once-only wrap, registration order ---

    [Fact]
    public async Task should_not_promote_ambient_tenant_when_promotion_is_disabled()
    {
        // With the opt-out the decorator passes everything through, so the surfaces the spike proved
        // lossy revert to the ASP.NET Core default (null), while the explicit and route-values
        // surfaces are unaffected.
        await using var app = await _CreateAppAsync(sources =>
            sources.AddRouteSource(options => options.PromoteAmbientRouteValue = false)
        );
        using var client = HttpTenancyTestHarness.CreateClient(app);

        var links = await _GetLinksAsync(client, "/acme/orders");

        links.SameControllerAction.Should().BeNull();
        links.OtherControllerAction.Should().BeNull();
        links.NamedEndpoint.Should().BeNull();
        links.RouteValuesNamed.Should().Be("/acme/invoices/summary");
        links.SameControllerActionExplicit.Should().Be("/globex/orders/7");
    }

    [Fact]
    public async Task should_wrap_link_generator_once_when_route_source_is_registered_twice()
    {
        await using var app = await _CreateAppAsync(sources => sources.AddRouteSource().AddRouteSource());
        using var client = HttpTenancyTestHarness.CreateClient(app);

        var generator = app.Services.GetRequiredService<LinkGenerator>();
        var decorator = generator.Should().BeOfType<TenantAmbientRouteValueLinkGenerator>().Subject;
        decorator.Inner.Should().NotBeOfType<TenantAmbientRouteValueLinkGenerator>();

        var links = await _GetLinksAsync(client, "/acme/orders");
        links.SameControllerAction.Should().Be("/acme/orders/7");
    }

    [Fact]
    public async Task should_preserve_ambient_tenant_when_route_source_is_registered_after_add_controllers()
    {
        // The default factory registers the route source BEFORE AddControllers() (the order the five
        // preserve scenarios run under); the reverse order must build and behave identically, because
        // the later AddRouting() inside AddControllers() is TryAdd-based and cannot undo the wrap.
        await using var app = await _CreateAppAsync(registerControllersFirst: true);
        using var client = HttpTenancyTestHarness.CreateClient(app);

        app.Services.GetRequiredService<LinkGenerator>().Should().BeOfType<TenantAmbientRouteValueLinkGenerator>();

        var links = await _GetLinksAsync(client, "/acme/orders");
        links.SameControllerAction.Should().Be("/acme/orders/7");
        links.OtherControllerAction.Should().Be("/acme/invoices");
        links.NamedEndpoint.Should().Be("/acme/invoices/summary");
    }

    // --- app factory and helpers ---

    /// <summary>Builds the MVC + tenancy host every scenario sends through.</summary>
    /// <param name="configureSources">
    /// Source registration; defaults to a single <c>AddRouteSource()</c>. Must register the route
    /// source, or nothing wraps the <see cref="LinkGenerator"/>.
    /// </param>
    /// <param name="registerControllersFirst">
    /// Registers MVC before tenancy so the route source runs after <c>AddControllers()</c>; the
    /// default registers tenancy first.
    /// </param>
    private async Task<WebApplication> _CreateAppAsync(
        Action<HeadlessTenantCatalogResolutionBuilder>? configureSources = null,
        bool registerControllersFirst = false
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

        if (registerControllersFirst)
        {
            _AddControllers(builder);
        }

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
                    if (configureSources is null)
                    {
                        sources.AddRouteSource();
                    }
                    else
                    {
                        configureSources(sources);
                    }
                })
            );
        });

        builder.Services.AddTestAuthentication();
        builder.Services.AddAuthorization();

        if (!registerControllersFirst)
        {
            _AddControllers(builder);
        }

        var app = builder.Build();

        app.UseStatusCodesRewriter();
        app.UseRouting();
        app.UseHeadlessTenantCatalogResolution();
        app.UseAuthentication();
        app.UseAuthorization();

        var tenantGroup = app.MapGroup("/{tenant}");

        tenantGroup.MapGet("/invoices/summary", () => Results.Ok()).WithName(TenantLinkNames.InvoiceSummary);

        // Minimal API origin inside the tenant group: generates links WITHOUT a tenant value
        // (ambient must carry it) and WITH an explicit different tenant.
        tenantGroup.MapGet(
            "/links",
            (HttpContext context, LinkGenerator links) => Results.Json(_Report(context, links))
        );

        // Minimal API origin with no tenant segment at all: nothing ambient to promote.
        app.MapGet("/plain", (HttpContext context, LinkGenerator links) => Results.Json(_Report(context, links)));

        app.MapControllers();

        await app.StartAsync(AbortToken);
        return app;
    }

    private static void _AddControllers(WebApplicationBuilder builder)
    {
        builder.Services.AddControllers().AddApplicationPart(typeof(TenantLinkOrdersController).Assembly);
    }

    private static TenantLinkReport _Report(HttpContext context, LinkGenerator links)
    {
        return new TenantLinkReport(
            SameControllerAction: null,
            OtherControllerAction: links.GetPathByAction(context, "Index", "TenantLinkInvoices"),
            NamedEndpoint: links.GetPathByName(context, TenantLinkNames.InvoiceSummary),
            RouteValuesNamed: links.GetPathByRouteValues(context, TenantLinkNames.InvoiceSummary),
            SameControllerActionExplicit: null,
            OtherControllerActionExplicit: links.GetPathByAction(
                context,
                "Index",
                "TenantLinkInvoices",
                new { tenant = "globex" }
            ),
            NamedEndpointExplicit: links.GetPathByName(
                context,
                TenantLinkNames.InvoiceSummary,
                new { tenant = "globex" }
            )
        );
    }

    private async Task<TenantLinkReport> _GetLinksAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path, AbortToken);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<TenantLinkReport>(cancellationToken: AbortToken))!;
    }
}

internal static class TenantLinkNames
{
    public const string InvoiceSummary = "tenant-invoices";
}

/// <summary>Links generated by one request; <c>null</c> means the surface could not build the link.</summary>
internal sealed record TenantLinkReport(
    string? SameControllerAction,
    string? OtherControllerAction,
    string? NamedEndpoint,
    string? RouteValuesNamed,
    string? SameControllerActionExplicit,
    string? OtherControllerActionExplicit,
    string? NamedEndpointExplicit,
    string? PlainTarget = null
);

/// <summary>MVC origin under a leading <c>{tenant}</c> segment; <c>Index</c> generates the links.</summary>
[ApiController]
[Route("{tenant}/orders")]
public sealed class TenantLinkOrdersController(LinkGenerator links) : ControllerBase
{
    [HttpGet]
    public IActionResult Index()
    {
        var report = new TenantLinkReport(
            SameControllerAction: Url.Action("Detail", new { id = 7 }),
            OtherControllerAction: links.GetPathByAction(HttpContext, "Index", "TenantLinkInvoices"),
            NamedEndpoint: links.GetPathByName(HttpContext, TenantLinkNames.InvoiceSummary),
            RouteValuesNamed: links.GetPathByRouteValues(HttpContext, TenantLinkNames.InvoiceSummary),
            SameControllerActionExplicit: Url.Action("Detail", new { id = 7, tenant = "globex" }),
            OtherControllerActionExplicit: links.GetPathByAction(
                HttpContext,
                "Index",
                "TenantLinkInvoices",
                new { tenant = "globex" }
            ),
            NamedEndpointExplicit: links.GetPathByName(
                HttpContext,
                TenantLinkNames.InvoiceSummary,
                new { tenant = "globex" }
            ),
            PlainTarget: links.GetPathByAction(HttpContext, "Index", "TenantLinkPlain")
        );

        return Ok(report);
    }

    [HttpGet("{id:int}")]
    public IActionResult Detail(int id)
    {
        return Ok(new { id });
    }
}

/// <summary>Second MVC controller so the cross-controller link targets a different route.</summary>
[ApiController]
[Route("{tenant}/invoices")]
public sealed class TenantLinkInvoicesController : ControllerBase
{
    [HttpGet]
    public IActionResult Index()
    {
        return Ok();
    }
}

/// <summary>MVC origin with no tenant segment: nothing ambient to promote.</summary>
[ApiController]
[Route("plain-mvc")]
public sealed class TenantLinkPlainController(LinkGenerator links) : ControllerBase
{
    [HttpGet]
    public IActionResult Index()
    {
        var report = new TenantLinkReport(
            SameControllerAction: Url.Action("Index", "TenantLinkOrders"),
            OtherControllerAction: links.GetPathByAction(HttpContext, "Index", "TenantLinkInvoices"),
            NamedEndpoint: links.GetPathByName(HttpContext, TenantLinkNames.InvoiceSummary),
            RouteValuesNamed: null,
            SameControllerActionExplicit: null,
            OtherControllerActionExplicit: null,
            NamedEndpointExplicit: null
        );

        return Ok(report);
    }
}
