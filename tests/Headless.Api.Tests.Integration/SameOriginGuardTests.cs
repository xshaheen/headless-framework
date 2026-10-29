// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Text.Json;
using Headless;
using Headless.Api;
using Headless.Api.Resources;
using Headless.Checks;
using Headless.Context;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Tests;

/// <summary>
/// Pins the same-origin guard on a Kestrel host: a guarded Minimal API endpoint, a controller carrying
/// <see cref="RequireSameOriginAttribute"/>, and controllers guarded by a <c>MapControllers()</c> convention refuse a
/// cross-site request with 403 <c>g:cross_site_request</c> and admit a same-origin one.
/// </summary>
public sealed class SameOriginGuardTests : TestBase
{
    private const string _TrustedSpa = "https://app.example.com";

    [Theory]
    [InlineData("/session/refresh")]
    [InlineData("/mvc/same-origin/attribute")]
    public async Task should_refuse_a_cross_site_request_with_the_error_code(string path)
    {
        await using var app = await _CreateAppAsync(guardControllersByConvention: false);
        using var client = _CreateClient(app);
        using var request = _Request(path, origin: "https://evil.example.net", fetchSite: "cross-site");

        using var response = await client.SendAsync(request, AbortToken);

        await _AssertCrossSiteRejectionAsync(response);
    }

    [Theory]
    [InlineData("/session/refresh")]
    [InlineData("/mvc/same-origin/attribute")]
    public async Task should_admit_a_same_origin_request(string path)
    {
        await using var app = await _CreateAppAsync(guardControllersByConvention: false);
        using var client = _CreateClient(app);
        using var request = _Request(path, origin: app.Urls.Single(), fetchSite: "same-origin");

        using var response = await client.SendAsync(request, AbortToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(AbortToken)).Should().Contain("ok");
    }

    [Fact]
    public async Task should_admit_a_listed_origin_on_a_same_site_request()
    {
        await using var app = await _CreateAppAsync(guardControllersByConvention: false);
        using var client = _CreateClient(app);
        using var request = _Request("/session/refresh", origin: _TrustedSpa, fetchSite: "same-site");

        using var response = await client.SendAsync(request, AbortToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task should_refuse_a_request_without_origin_evidence()
    {
        await using var app = await _CreateAppAsync(guardControllersByConvention: false);
        using var client = _CreateClient(app);
        using var request = _Request("/session/refresh", origin: null, fetchSite: null);

        using var response = await client.SendAsync(request, AbortToken);

        await _AssertCrossSiteRejectionAsync(response);
    }

    [Fact]
    public async Task should_leave_an_unguarded_endpoint_alone()
    {
        await using var app = await _CreateAppAsync(guardControllersByConvention: false);
        using var client = _CreateClient(app);
        using var request = _Request("/public", origin: "https://evil.example.net", fetchSite: "cross-site");

        using var response = await client.SendAsync(request, AbortToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task should_guard_controllers_through_the_map_controllers_convention()
    {
        await using var app = await _CreateAppAsync(guardControllersByConvention: true);
        using var client = _CreateClient(app);
        using var crossSite = _Request("/mvc/same-origin/convention", "https://evil.example.net", "cross-site");
        using var sameOrigin = _Request("/mvc/same-origin/convention", app.Urls.Single(), "same-origin");

        using var refused = await client.SendAsync(crossSite, AbortToken);
        using var admitted = await client.SendAsync(sameOrigin, AbortToken);

        await _AssertCrossSiteRejectionAsync(refused);
        admitted.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static async Task _AssertCrossSiteRejectionAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(AbortToken));
        body.RootElement.GetProperty("status").GetInt32().Should().Be(StatusCodes.Status403Forbidden);
        body.RootElement.GetProperty("error")
            .GetProperty("code")
            .GetString()
            .Should()
            .Be(GeneralErrorCodes.CrossSiteRequest);
    }

    private static HttpRequestMessage _Request(string path, string? origin, string? fetchSite)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path);

        if (origin is not null)
        {
            request.Headers.Add("Origin", origin);
        }

        if (fetchSite is not null)
        {
            request.Headers.Add("Sec-Fetch-Site", fetchSite);
        }

        return request;
    }

    private static HttpClient _CreateClient(WebApplication app)
    {
        return new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
    }

    private async Task<WebApplication> _CreateAppAsync(bool guardControllersByConvention)
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = EnvironmentNames.Test }
        );
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddHttpContextAccessor();
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.TryAddSingleton<IBuildInformationAccessor, BuildInformationAccessor>();
        builder.Services.AddHeadlessProblemDetails();
        builder.Services.AddHeadlessSameOriginGuard(options => options.AllowedOrigins = [_TrustedSpa]);
        builder.Services.AddControllers().AddApplicationPart(typeof(SameOriginGuardController).Assembly);

        var app = builder.Build();
        app.MapPost("/session/refresh", () => "ok").RequireSameOrigin();
        app.MapPost("/public", () => "ok");

        var controllers = app.MapControllers();

        if (guardControllersByConvention)
        {
            controllers.RequireSameOrigin();
        }

        await app.StartAsync(AbortToken);

        return app;
    }
}

[ApiController]
[Route("mvc/same-origin")]
public sealed class SameOriginGuardController : ControllerBase
{
    [HttpPost("attribute")]
    [RequireSameOrigin]
    public string Attribute() => "ok";

    [HttpPost("convention")]
    public string Convention() => "ok";
}
