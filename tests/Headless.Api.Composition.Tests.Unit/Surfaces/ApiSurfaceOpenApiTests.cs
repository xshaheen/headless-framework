// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Text.Json;
using Headless.Api;
using Headless.Api.ServiceDefaults;
using Headless.Api.Surfaces;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Tests.Surfaces;

public sealed class ApiSurfaceOpenApiTests : TestBase
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task should_isolate_documents_before_schema_generation_and_preserve_customization(bool customize)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddHeadlessApiSurfaces(options =>
        {
            options.Add(
                "portal",
                portal =>
                {
                    portal.OpenApi.DocumentName = "portal-doc";
                    portal.OpenApi.Title = "Final portal title";
                }
            );
            options.Add("console");
        });
        void Configure(OpenApiOptions options)
        {
            if (customize)
            {
                options.ShouldInclude = description => description.RelativePath != "portal/hidden";
                options.AddDocumentTransformer(
                    (document, _, _) =>
                    {
                        document.Info.Title = "Custom title";
                        return Task.CompletedTask;
                    }
                );
            }
        }
        if (customize)
        {
            _ConfigureEncryption(builder);
            builder.AddHeadless(configureServices: options =>
            {
                options.Validation.RequireUseHeadless = false;
                options.Validation.RequireStatusCodesRewriter = false;
                options.OpenTelemetry.Enabled = false;
                options.OpenApi.ConfigureOpenApi = Configure;
            });
            builder.Services.AddAuthentication();
        }
        else
        {
            builder.Services.AddHeadlessApiSurfaceDocuments(configure: Configure);
        }
        builder.Services.AddOpenApi(
            "extra",
            options => options.ShouldInclude = description => description.GroupName == "extra"
        );
        await using var app = builder.Build();
        app.MapGet("/portal/visible", () => new PortalPayload("public"))
            .WithGroupName("v2")
            .WithMetadata(new SurfaceMetadata("PORTAL"));
        app.MapGet("/portal/hidden", () => "hidden").WithMetadata(new SurfaceMetadata("portal"));
        app.MapGet("/console", () => new ConsolePayload("secret")).WithMetadata(new SurfaceMetadata("console"));
        app.MapGet("/plain", () => "plain");
        app.MapGet("/extra", () => "extra").WithGroupName("extra");
        if (customize)
        {
            app.MapHeadlessEndpoints();
        }
        else
        {
            app.MapOpenApi();
        }
        await app.StartAsync(AbortToken);
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
            using var response = await client.GetAsync("/openapi/PORTAL-DOC.json", AbortToken);
            var json = await response.Content.ReadAsStringAsync(AbortToken);
            response.StatusCode.Should().Be(HttpStatusCode.OK, json);
            using var document = JsonDocument.Parse(json);
            document
                .RootElement.GetProperty("info")
                .GetProperty("title")
                .GetString()
                .Should()
                .Be(customize ? "Custom title" : "Final portal title");
            var paths = document.RootElement.GetProperty("paths").EnumerateObject().Select(x => x.Name).ToArray();
            paths
                .Should()
                .BeEquivalentTo(customize ? ["/portal/visible"] : new[] { "/portal/visible", "/portal/hidden" });
            json.Should().NotContain("ConsolePayload");
            using var console = JsonDocument.Parse(await client.GetStringAsync("/openapi/console.json", AbortToken));
            console
                .RootElement.GetProperty("paths")
                .EnumerateObject()
                .Should()
                .ContainSingle()
                .Which.Name.Should()
                .Be("/console");
            using var extra = JsonDocument.Parse(await client.GetStringAsync("/openapi/extra.json", AbortToken));
            extra
                .RootElement.GetProperty("paths")
                .EnumerateObject()
                .Should()
                .ContainSingle()
                .Which.Name.Should()
                .Be("/extra");
            using var defaultDocument = await client.GetAsync("/openapi/v1.json", AbortToken);
            defaultDocument.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
        finally
        {
            await app.StopAsync(AbortToken);
        }
    }

    [Fact]
    public async Task should_reject_an_unconfigured_surface_document_at_startup()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddHeadlessApiSurface("portal");
        builder.Services.AddHeadlessApiSurfaceDocuments(["missing"]);
        await using var app = builder.Build();
        var start = () => app.StartAsync(AbortToken);
        await start.Should().ThrowAsync<InvalidOperationException>().WithMessage("*missing*configured API surface*");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void should_reject_surface_registration_after_document_inference(bool serviceDefaults)
    {
        var builder = WebApplication.CreateBuilder();
        if (serviceDefaults)
        {
            _ConfigureEncryption(builder);
            builder.AddHeadless();
        }
        else
        {
            builder.Services.AddHeadlessApiSurfaceDocuments();
        }
        var register = () => builder.Services.AddHeadlessApiSurface("portal");
        register.Should().Throw<InvalidOperationException>().WithMessage("*before OpenAPI document inference*");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task should_preserve_default_document_and_honor_explicit_selection(bool surfaces, bool selectPortal)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        _ConfigureEncryption(builder);
        if (surfaces)
        {
            builder.Services.AddHeadlessApiSurface("portal").AddHeadlessApiSurface("console");
        }
        builder.AddHeadless(configureServices: options =>
        {
            options.Validation.RequireUseHeadless = false;
            options.Validation.RequireStatusCodesRewriter = false;
            options.OpenTelemetry.Enabled = false;
            options.OpenApi.SurfaceDocumentNames = surfaces ? (selectPortal ? ["PORTAL"] : []) : null;
        });
        builder.Services.AddAuthentication();
        await using var app = builder.Build();
        app.MapGet("/portal", () => "portal").WithMetadata(new SurfaceMetadata("portal"));
        app.MapGet("/console", () => "console").WithMetadata(new SurfaceMetadata("console"));
        app.MapHeadlessEndpoints();
        await app.StartAsync(AbortToken);
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
            var name = selectPortal ? "portal" : "v1";
            using var document = JsonDocument.Parse(await client.GetStringAsync($"/openapi/{name}.json", AbortToken));
            var paths = document.RootElement.GetProperty("paths").EnumerateObject().Select(path => path.Name);
            paths.Should().BeEquivalentTo(selectPortal ? ["/portal"] : new[] { "/portal", "/console" });
            using var console = await client.GetAsync("/openapi/console.json", AbortToken);
            console.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
        finally
        {
            await app.StopAsync(AbortToken);
        }
    }

    private static void _ConfigureEncryption(WebApplicationBuilder builder) =>
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Headless:StringEncryption:DefaultPassPhrase"] = "TestPassPhrase123456",
                ["Headless:StringEncryption:InitVectorBytes"] = "VGVzdElWMDEyMzQ1Njc4OQ==",
                ["Headless:StringEncryption:DefaultSalt"] = "VGVzdFNhbHQ=",
                ["Headless:StringHash:DefaultSalt"] = "TestSalt",
            }
        );

    private sealed record SurfaceMetadata(string SurfaceName) : IApiSurfaceMetadata;

    public sealed record PortalPayload(string Value);

    public sealed record ConsolePayload(string Secret);
}
