// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Abstractions;
using Headless.Api;
using Headless.Api.Abstractions;
using Headless.Api.ServiceDefaults;
using Headless.Constants;
using Headless.Testing.Tests;
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
        response.Content.Headers.ContentType?.MediaType.Should()?.Be("application/problem+json");

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
