// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless;
using Headless.Api;
using Headless.Context;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Tests.Extensions;

public sealed class EndpointsExtensionsWireShapeTests : TestBase
{
    [Fact]
    public async Task should_return_problem_details_with_400_when_redirect_uri_host_mismatches_main_host()
    {
        // given - a route that explicitly invokes BuildRedirectResultOrBadRequest with a
        // synthesized mismatched URI. This bypasses BuildRedirectUri's structural guarantee so the
        // 400 branch is reachable, then asserts the full IResult -> HTTP pipeline emits the
        // framework's canonical application/problem+json wire shape (defense-in-depth check).
        var mainHost = new Uri("https://main.example.com");
        var attackerRedirect = new Uri("https://attacker.example/login");

        await using var app = await _CreateAppAsync(mainHost, attackerRedirect);
        using var client = _CreateClient(app);

        // when
        using var response = await client.GetAsync("/redirect-mismatch", AbortToken);

        // then
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var contentType = response.Content.Headers.ContentType;
        contentType.Should().NotBeNull();
        contentType!.MediaType.Should().Be("application/problem+json");

        var json = await response.Content.ReadAsStringAsync(AbortToken);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("type").GetString().Should().Be(HeadlessProblemDetailsConstants.Types.BadRequest);
        root.GetProperty("title").GetString().Should().Be(HeadlessProblemDetailsConstants.Titles.BadRequest);
        root.GetProperty("status").GetInt32().Should().Be(StatusCodes.Status400BadRequest);
        root.GetProperty("detail").GetString().Should().Be(HeadlessProblemDetailsConstants.Details.BadRequest);
        root.GetProperty("traceId").GetString().Should().NotBeNullOrWhiteSpace();
        root.GetProperty("instance").GetString().Should().Be("/redirect-mismatch");
    }

    [Fact]
    public async Task should_redirect_anonymous_request_when_fallback_policy_requires_authentication()
    {
        // given
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = EnvironmentNames.Test }
        );
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddHttpContextAccessor();
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.TryAddSingleton<IBuildInformationAccessor, BuildInformationAccessor>();
        builder.Services.AddHeadlessProblemDetails();
        builder.Services.AddAuthentication();
        builder
            .Services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        await using var app = builder.Build();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapHostRedirects("https://main.example.com", ["old.example.com"]);
        await app.StartAsync(AbortToken);

        using var handler = new HttpClientHandler { AllowAutoRedirect = false, CheckCertificateRevocationList = true };
        using var client = new HttpClient(handler) { BaseAddress = new Uri(app.Urls.Single()) };
        using var request = new HttpRequestMessage(HttpMethod.Get, "/docs?page=2");
        request.Headers.Host = "old.example.com";

        // when
        using var response = await client.SendAsync(request, AbortToken);

        // then
        response.StatusCode.Should().Be(HttpStatusCode.MovedPermanently);
        response.Headers.Location.Should().Be(new Uri("https://main.example.com/docs?page=2"));
    }

    private async Task<WebApplication> _CreateAppAsync(Uri mainHost, Uri synthesizedRedirectUri)
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = EnvironmentNames.Test }
        );
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        builder.Services.AddRouting();
        builder.Services.AddHttpContextAccessor();
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.TryAddSingleton<IBuildInformationAccessor, BuildInformationAccessor>();
        builder.Services.AddHeadlessProblemDetails();

        var app = builder.Build();

        app.UseExceptionHandler();
        app.MapGet(
            "/redirect-mismatch",
            (IProblemDetailsCreator problemDetailsCreator) =>
                EndpointsExtensions.BuildRedirectResultOrBadRequest(
                    synthesizedRedirectUri,
                    mainHost,
                    problemDetailsCreator
                )
        );

        await app.StartAsync(AbortToken);
        return app;
    }

    private static HttpClient _CreateClient(WebApplication app)
    {
        return new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
    }
}
