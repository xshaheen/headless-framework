// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using FluentValidation.Results;
using Headless;
using Headless.Api;
using Headless.Api.Resources;
using Headless.Api.ServiceDefaults;
using Headless.Http;
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
            application.MapGet("/data", () => Results.Ok(new { Value = "test" }))
        );
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
        await using var app = await _CreateAppAsync(application => application.MapHeadlessEndpoints());
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
    public async Task should_noop_when_use_headless_is_called_more_than_once()
    {
        // given
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = EnvironmentNames.Test }
        );
        builder.WebHost.UseUrls("http://127.0.0.1:0");
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
        // Second call is expected to be a no-op (idempotent).
        app.UseHeadless(options =>
        {
            options.UseHttpsRedirection = false;
            options.UseHsts = false;
        });
        app.MapHeadlessEndpoints();

        await app.StartAsync(AbortToken);

        using var client = _CreateClient(app);

        // when
        using var response = await client.GetAsync("/health", AbortToken);

        // then
        response.StatusCode.Should().Be(HttpStatusCode.OK);
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
            application => application.MapGet("/origin", (HttpRequest request) => $"{request.Scheme}://{request.Host}"),
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
        var contentType = response.Content.Headers.ContentType;
        contentType.Should().NotBeNull();
        contentType!.MediaType.Should().Be(ContentTypes.Applications.ProblemJson);

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

        var act = () => app.StartAsync(AbortToken);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*MapHeadlessEndpoints*");
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

    [Theory]
    [InlineData(EnvironmentNames.Test, HttpStatusCode.OK)]
    [InlineData(EnvironmentNames.Development, HttpStatusCode.OK)]
    [InlineData(EnvironmentNames.Production, HttpStatusCode.TemporaryRedirect)]
    public async Task should_redirect_to_https_only_outside_development_and_test(
        string environmentName,
        HttpStatusCode expectedStatus
    )
    {
        // given - an explicit HTTPS port, so the redirection middleware would redirect whenever it runs
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environmentName });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.AddHeadless(configureServices: options =>
        {
            options.Validation.ValidateServiceProviderOnStartup = false;
            options.OpenTelemetry.Enabled = false;
        });
        builder.Services.AddAuthentication();
        builder.Services.AddHttpsRedirection(options => options.HttpsPort = 443);

        await using var app = builder.Build();
        app.UseHeadless();
        app.MapHeadlessEndpoints();
        app.MapGet("/data", () => Results.Ok());
        await app.StartAsync(AbortToken);

        using var handler = new HttpClientHandler { AllowAutoRedirect = false, CheckCertificateRevocationList = true };
        using var client = new HttpClient(handler) { BaseAddress = new Uri(app.Urls.Single()) };

        // when
        using var response = await client.GetAsync("/data", AbortToken);

        // then
        response.StatusCode.Should().Be(expectedStatus);
    }

    [Fact]
    public async Task should_handle_exception_thrown_by_application_middleware_added_after_use_headless()
    {
        // given - UseHeadless is the outer block, so middleware the app adds after it runs inside the exception handler
        await using var app = await _CreateAppAsync(application =>
        {
            application.Use((HttpContext _, RequestDelegate _) => throw new InvalidOperationException("app"));
            application.MapGet("/data", () => Results.Ok());
        });
        using var client = _CreateClient(app);

        // when
        using var response = await client.GetAsync("/data", AbortToken);

        // then
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        response.Content.Headers.ContentType!.MediaType.Should().Be(ContentTypes.Applications.ProblemJson);
    }

    [Theory]
    [InlineData("/validation", HttpStatusCode.UnprocessableEntity, "problem:unprocessable_entity")]
    [InlineData("/unknown-error", HttpStatusCode.InternalServerError, "problem:internal_error")]
    [InlineData("/unauthorized", HttpStatusCode.Unauthorized, "problem:unauthorized")]
    public async Task should_write_problem_details_in_the_request_culture_after_localization_has_unwound(
        string path,
        HttpStatusCode expectedStatus,
        string detailKey
    )
    {
        // given - localization is added after UseHeadless, so the exception or bare status reaches the
        // exception handler and the status-code rewriter after the request culture has been restored
        await using var app = await _CreateAppAsync(application =>
        {
            application.UseRequestLocalization(options =>
                options.SetDefaultCulture("en").AddSupportedCultures("en", "ar").AddSupportedUICultures("en", "ar")
            );
            application.MapGet(
                "/validation",
                IResult () =>
                    throw new FluentValidation.ValidationException([new ValidationFailure("Name", "Name is required.")])
            );
            application.MapGet("/unknown-error", IResult () => throw new InvalidOperationException("app"));
            application.MapGet("/unauthorized", () => Results.Unauthorized());
        });
        using var client = _CreateClient(app);
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.AcceptLanguage.ParseAdd("ar");

        // when
        using var response = await client.SendAsync(request, AbortToken);

        // then
        response.StatusCode.Should().Be(expectedStatus);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(AbortToken));
        body.RootElement.GetProperty("detail")
            .GetString()
            .Should()
            .Be(Messages.ResourceManager.GetString(detailKey, CultureInfo.GetCultureInfo("ar")));
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
}
