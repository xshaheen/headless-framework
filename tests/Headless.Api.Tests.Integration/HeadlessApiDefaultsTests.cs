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
            application.MapHeadlessDefaultEndpoints();
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

    private async Task<WebApplication> _CreateAppAsync(
        Action<WebApplication> map,
        Action<HeadlessApiDefaultsOptions>? configure = null
    )
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = EnvironmentNames.Test }
        );
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        _AddDefaultHeadlessSecurityConfiguration(builder.Configuration);
        builder.AddHeadlessInfrastructure();
        builder.Services.AddAuthentication();

        var app = builder.Build();
        app.UseHeadlessDefaults(options =>
        {
            options.UseHttpsRedirection = false;
            options.UseHsts = false;
            configure?.Invoke(options);
        });
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
