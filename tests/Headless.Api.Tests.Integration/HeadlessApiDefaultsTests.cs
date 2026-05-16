// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Api;
using Headless.Constants;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class HeadlessApiDefaultsTests : TestBase
{
    [Fact]
    public async Task should_map_default_health_endpoints_and_add_no_cache_header()
    {
        // given
        await using var app = await _CreateAppAsync(application =>
        {
            application.MapGet("/data", () => Results.Ok(new { Value = "test" }));
        });
        using var client = _CreateClient(app);

        // when
        var health = await client.GetStringAsync("/health", AbortToken);
        var alive = await client.GetStringAsync("/alive", AbortToken);
        using var response = await client.GetAsync("/data", AbortToken);

        // then
        using var healthDocument = JsonDocument.Parse(health);
        healthDocument.RootElement.GetProperty("status").GetString().Should().Be("Healthy");
        healthDocument.RootElement.GetProperty("results").TryGetProperty("self", out _).Should().BeTrue();
        alive.Should().Be("Healthy");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl.Should().NotBeNull();
        response.Headers.CacheControl!.NoCache.Should().BeTrue();
        response.Headers.CacheControl.NoStore.Should().BeTrue();
        response.Headers.CacheControl.MustRevalidate.Should().BeTrue();
    }

    [Fact]
    public async Task should_noop_when_default_endpoints_are_mapped_more_than_once()
    {
        // given
        await using var app = await _CreateAppAsync(application =>
        {
            application.MapHeadlessEndpoints();
        });
        using var client = _CreateClient(app);

        // when
        var health = await client.GetStringAsync("/health", AbortToken);
        var alive = await client.GetStringAsync("/alive", AbortToken);

        // then
        using var healthDocument = JsonDocument.Parse(health);
        healthDocument.RootElement.GetProperty("status").GetString().Should().Be("Healthy");
        alive.Should().Be("Healthy");
    }

    [Fact]
    public async Task should_not_override_explicit_cache_control_header()
    {
        // given
        await using var app = await _CreateAppAsync(application =>
        {
            application.MapGet(
                "/cached",
                (HttpContext context) =>
                {
                    context.Response.Headers.CacheControl = "public,max-age=60";
                    return Results.Ok(new { Value = "cached" });
                }
            );
        });
        using var client = _CreateClient(app);

        // when
        using var response = await client.GetAsync("/cached", AbortToken);

        // then
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl.Should().NotBeNull();
        response.Headers.CacheControl!.Public.Should().BeTrue();
        response.Headers.CacheControl.MaxAge.Should().Be(TimeSpan.FromSeconds(60));
        response.Headers.CacheControl.NoCache.Should().BeFalse();
    }

    [Fact]
    public async Task should_apply_forwarded_headers_when_explicitly_trusting_any_proxy()
    {
        // given
        await using var app = await _CreateAppAsync(
            application =>
            {
                application.MapGet("/origin", (HttpRequest request) => $"{request.Scheme}://{request.Host}");
            },
            options => options.TrustForwardedHeadersFromAnyProxy = true
        );
        using var client = _CreateClient(app);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/origin");
        request.Headers.Add("X-Forwarded-Proto", "https");
        request.Headers.Add("X-Forwarded-Host", "api.example.test");

        // when
        using var response = await client.SendAsync(request, AbortToken);
        var body = await response.Content.ReadAsStringAsync(AbortToken);

        // then
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().Be("https://api.example.test");
    }

    [Fact]
    public async Task should_rewrite_bare_404_to_headless_problem_details()
    {
        // given
        await using var app = await _CreateAppAsync(_ => { });
        using var client = _CreateClient(app);

        // when
        using var response = await client.GetAsync("/missing", AbortToken);
        var body = await response.Content.ReadAsStringAsync(AbortToken);

        // then
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.Should().Be(ContentTypes.Applications.ProblemJson);

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        root.GetProperty("status").GetInt32().Should().Be(StatusCodes.Status404NotFound);
        root.GetProperty("title").GetString().Should().Be(HeadlessProblemDetailsConstants.Titles.EndpointNotFound);
        root.GetProperty("detail").GetString().Should().Contain("/missing");
    }

    [Fact]
    public async Task should_map_headless_service_defaults_and_convention_endpoints()
    {
        // given
        await using var app = await _CreateAppAsync(
            application =>
            {
                application.MapGet("/data", () => Results.Ok(new { Value = "test" }));
                application.MapGet("/origin", (HttpRequest request) => $"{request.Scheme}://{request.Host}");
            },
            options => options.TrustForwardedHeadersFromAnyProxy = true
        );
        using var client = _CreateClient(app);
        using var originRequest = new HttpRequestMessage(HttpMethod.Get, "/origin");
        originRequest.Headers.Add("X-Forwarded-Proto", "https");
        originRequest.Headers.Add("X-Forwarded-Host", "api.example.test");

        // when
        var health = await client.GetStringAsync("/health", AbortToken);
        var alive = await client.GetStringAsync("/alive", AbortToken);
        using var data = await client.GetAsync("/data", AbortToken);
        using var origin = await client.SendAsync(originRequest, AbortToken);
        var originBody = await origin.Content.ReadAsStringAsync(AbortToken);
        using var openApi = await client.GetAsync("/openapi/v1.json", AbortToken);

        // then
        using var healthDocument = JsonDocument.Parse(health);
        healthDocument.RootElement.GetProperty("status").GetString().Should().Be("Healthy");
        healthDocument.RootElement.GetProperty("results").TryGetProperty("self", out _).Should().BeTrue();
        alive.Should().Be("Healthy");
        data.StatusCode.Should().Be(HttpStatusCode.OK);
        data.Headers.CacheControl.Should().NotBeNull();
        data.Headers.CacheControl!.NoCache.Should().BeTrue();
        data.Headers.CacheControl.NoStore.Should().BeTrue();
        data.Headers.CacheControl.MustRevalidate.Should().BeTrue();
        origin.StatusCode.Should().Be(HttpStatusCode.OK);
        originBody.Should().Be("https://api.example.test");
        openApi.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task should_fail_start_when_use_headless_is_not_applied()
    {
        // given
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = EnvironmentNames.Test }
        );
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        _AddDefaultHeadlessSecurityConfiguration(builder.Configuration);
        builder.AddHeadless(configureServices: options =>
        {
            options.Validation.ValidateServiceProviderOnStartup = false;
            options.OpenTelemetry.Enabled = false;
        });
        await using var app = builder.Build();

        // when
        var act = () => app.StartAsync(AbortToken);

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*UseHeadless*");
    }

    [Fact]
    public async Task should_fail_start_when_map_headless_endpoints_is_not_applied()
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = EnvironmentNames.Test }
        );
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        _AddDefaultHeadlessSecurityConfiguration(builder.Configuration);
        builder.AddHeadless(configureServices: options =>
        {
            options.Validation.ValidateServiceProviderOnStartup = false;
            options.OpenTelemetry.Enabled = false;
        });
        builder.Services.AddAuthentication();

        await using var app = builder.Build();
        app.UseHeadless(options =>
        {
            options.UseHttpsRedirection = false;
            options.UseHsts = false;
        });

        Func<Task> act = () => app.StartAsync(AbortToken);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*MapHeadlessEndpoints*");
    }

    [Fact]
    public async Task should_skip_antiforgery_middleware_when_disabled_in_pipeline_options()
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = EnvironmentNames.Test }
        );
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        _AddDefaultHeadlessSecurityConfiguration(builder.Configuration);
        builder.AddHeadless(configureServices: options =>
        {
            options.Validation.ValidateServiceProviderOnStartup = false;
            options.OpenTelemetry.Enabled = false;
        });
        builder.Services.AddAuthentication();

        await using var app = builder.Build();
        app.UseHeadless(options =>
        {
            options.UseHttpsRedirection = false;
            options.UseHsts = false;
            options.UseAntiforgery = false;
        });
        app.MapHeadlessEndpoints();

        await app.StartAsync(AbortToken);

        using var client = _CreateClient(app);
        using var response = await client.GetAsync("/health", AbortToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task should_not_map_openapi_document_when_openapi_is_disabled()
    {
        await using var app = await _CreateAppAsync(
            application =>
            {
                application.MapGet("/data", () => Results.Ok());
            },
            configureServices: options => options.OpenApi.Enabled = false
        );
        using var client = _CreateClient(app);

        using var openApi = await client.GetAsync("/openapi/v1.json", AbortToken);
        using var data = await client.GetAsync("/data", AbortToken);

        openApi.StatusCode.Should().Be(HttpStatusCode.NotFound);
        data.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<WebApplication> _CreateAppAsync(
        Action<WebApplication> map,
        Action<HeadlessApiDefaultsOptions>? configure = null,
        Action<HeadlessServiceDefaultsOptions>? configureServices = null
    )
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = EnvironmentNames.Test }
        );
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        _AddDefaultHeadlessSecurityConfiguration(builder.Configuration);
        builder.AddHeadless(configureServices: options =>
        {
            options.Validation.ValidateServiceProviderOnStartup = false;
            options.OpenTelemetry.Enabled = false;
            configureServices?.Invoke(options);
        });
        builder.Services.AddAuthentication();

        var app = builder.Build();
        app.UseHeadless(options =>
        {
            options.UseHttpsRedirection = false;
            options.UseHsts = false;
            configure?.Invoke(options);
        });
        app.MapHeadlessEndpoints();
        map(app);

        await app.StartAsync(AbortToken);
        return app;
    }

    private static HttpClient _CreateClient(WebApplication app)
    {
        return new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
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
}
