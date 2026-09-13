// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Headless.Api;
using Headless.Api.MultiTenancy;
using Headless.Api.Mvc.Surfaces;
using Headless.Api.Surfaces;
using Headless.MultiTenancy;
using Headless.OpenApi.Nswag;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tests.Surfaces;

public sealed class ApiSurfaceHttpTests : TestBase
{
    [Fact]
    public async Task should_enforce_defaults_and_overrides_and_isolate_served_documents()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddControllers().AddApplicationPart(typeof(SurfacePortalController).Assembly);
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddHeadlessMvcApiSurfaces();
        builder.Services.AddHeadlessMvcApiSurfaces();
        builder.Services.AddHeadlessApiSurfaces(options =>
        {
            options.Add(
                "portal",
                surface =>
                {
                    surface.RoutePrefix = "/api/portal/";
                    surface.DefaultAuthorizationPolicy = "tenant";
                    surface.DefaultTenancyMode = ApiSurfaceTenancyMode.RequireTenant;
                }
            );
            options.Add("console", surface => surface.RoutePrefix = "api/console");
        });
        builder.Services.AddNswagApiSurfaceDocuments();
        builder
            .Services.AddAuthentication("test")
            .AddScheme<AuthenticationSchemeOptions, SurfaceAuthenticationHandler>("test", _ => { });
        builder.Services.AddAuthorization(options =>
        {
            var policy = new AuthorizationPolicyBuilder("test")
                .RequireAuthenticatedUser()
                .AddRequirements(new TenantRequirement())
                .Build();
            options.DefaultPolicy = policy;
            options.AddPolicy("tenant", policy);
        });
        builder.AddHeadlessTenancy(tenancy =>
            tenancy.Http(http => http.ResolveFromClaims()).Authorization(auth => auth.RequireTenant())
        );
        await using var app = builder.Build();
        app.UseDeveloperExceptionPage();
        app.UseRouting();
        app.Use(
            async (context, next) =>
            {
                context.Response.Headers["X-Surface"] = context.GetApiSurface()?.SurfaceName ?? "none";
                await next(context);
            }
        );
        app.UseAuthentication();
        app.UseHeadlessTenancy();
        app.UseAuthorization();
        app.MapControllers();
        var portal = app.MapApiSurface("portal");
        portal.MapGet("minimal/required", () => "required");
        portal.MapGet("minimal/optional", () => "optional").AllowMissingTenant();
        portal.MapGet("minimal/anonymous", () => "anonymous").AllowAnonymous();
        app.MapApiSurface("console").MapGet("minimal", () => new ConsoleSurfacePayload("private"));
        app.MapNswagApiSurfaceDocuments();
        await app.StartAsync(AbortToken);
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
            foreach (var style in new[] { "mvc", "minimal" })
            {
                using var unauthorized = await client.GetAsync($"/api/portal/{style}/required", AbortToken);
                unauthorized
                    .StatusCode.Should()
                    .Be(HttpStatusCode.Unauthorized, await unauthorized.Content.ReadAsStringAsync(AbortToken));
                unauthorized.Headers.GetValues("X-Surface").Should().ContainSingle("portal");
                client.DefaultRequestHeaders.Add("X-User", "alice");
                using var forbidden = await client.GetAsync($"/api/portal/{style}/required", AbortToken);
                forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);
                forbidden.Headers.GetValues("X-Surface").Should().ContainSingle("portal");
                using var optional = await client.GetAsync($"/api/portal/{style}/optional", AbortToken);
                optional.StatusCode.Should().Be(HttpStatusCode.OK);
                client.DefaultRequestHeaders.Remove("X-User");
                using var anonymous = await client.GetAsync($"/api/portal/{style}/anonymous", AbortToken);
                anonymous.StatusCode.Should().Be(HttpStatusCode.OK);
            }

            using var portalResponse = await client.GetAsync("/openapi/portal.json", AbortToken);
            var portalJson = await portalResponse.Content.ReadAsStringAsync(AbortToken);
            portalResponse.StatusCode.Should().Be(HttpStatusCode.OK, portalJson);
            using var portalDocument = JsonDocument.Parse(portalJson);
            var portalPaths = portalDocument.RootElement.GetProperty("paths");
            portalPaths.EnumerateObject().Should().HaveCount(6);
            portalPaths
                .EnumerateObject()
                .Should()
                .OnlyContain(x => x.Name.StartsWith("/api/portal/", StringComparison.Ordinal));
            foreach (var style in new[] { "mvc", "minimal" })
            {
                var required = portalPaths
                    .GetProperty($"/api/portal/{style}/required")
                    .GetProperty("get")
                    .GetProperty("responses")
                    .GetProperty("403");
                required
                    .GetProperty("content")
                    .GetProperty("application/problem+json")
                    .GetProperty("examples")
                    .TryGetProperty("tenantRequired", out _)
                    .Should()
                    .BeTrue();
                var optional = portalPaths
                    .GetProperty($"/api/portal/{style}/optional")
                    .GetProperty("get")
                    .GetProperty("responses")
                    .GetProperty("403");
                optional.GetRawText().Should().NotContain("tenantRequired");
            }

            portalDocument.RootElement.GetRawText().Should().NotContain(nameof(ConsoleSurfacePayload));
            using var consoleDocument = JsonDocument.Parse(
                await client.GetStringAsync("/openapi/console.json", AbortToken)
            );
            consoleDocument
                .RootElement.GetProperty("paths")
                .EnumerateObject()
                .Should()
                .ContainSingle()
                .Which.Name.Should()
                .Be("/api/console/minimal");
            var swagger = await client.GetStringAsync("/swagger/index.html", AbortToken);
            swagger.Should().Contain("/openapi/portal.json").And.Contain("/openapi/console.json");
        }
        finally
        {
            await app.StopAsync(AbortToken);
        }
    }
}

[ApiController]
[ApiSurface("portal")]
[ApiExplorerSettings(GroupName = "v1")]
[Route("mvc")]
public sealed class SurfacePortalController : ControllerBase
{
    [HttpGet("required")]
    public string Required() => "required";

    [HttpGet("optional")]
    [AllowMissingTenant]
    public string Optional() => "optional";

    [HttpGet("anonymous")]
    [AllowAnonymous]
    public string Anonymous() => "anonymous";
}

public sealed record ConsoleSurfacePayload(string Secret);

internal sealed class SurfaceAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder
) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync() =>
        Task.FromResult(
            Request.Headers.ContainsKey("X-User")
                ? AuthenticateResult.Success(
                    new AuthenticationTicket(
                        new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "alice")], "test")),
                        "test"
                    )
                )
                : AuthenticateResult.NoResult()
        );
}
