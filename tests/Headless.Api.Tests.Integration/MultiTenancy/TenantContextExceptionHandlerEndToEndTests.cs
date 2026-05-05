// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Net.Mime;
using System.Text.Json;
using Headless.Abstractions;
using Headless.Api;
using Headless.Api.MultiTenancy;
using Headless.Constants;
using Headless.Primitives;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Tests.MultiTenancy;

public sealed class TenantContextExceptionHandlerEndToEndTests : TestBase
{
    [Fact]
    public async Task should_map_default_missing_tenant_context_exception_to_normalized_400()
    {
        // given
        await using var app = await _CreateAppAsync(
            handlerSetup: services => services.AddTenantContextProblemDetails(_ => { }),
            endpoint: () => throw new MissingTenantContextException()
        );
        using var client = _CreateClient(app);

        // when
        using var response = await client.GetAsync("/throw", AbortToken);

        // then
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var json = await response.Content.ReadAsStringAsync(AbortToken);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("status").GetInt32().Should().Be(400);
        root.GetProperty("title").GetString().Should().Be("tenant-context-required");
        root.GetProperty("type").GetString().Should().Be("https://errors.headless/tenancy/tenant-required");
        root.GetProperty("detail")
            .GetString()
            .Should()
            .Be(HeadlessProblemDetailsConstants.Details.TenantContextRequired);
        root.GetProperty("code").GetString().Should().Be("tenancy.tenant-required");
        root.GetProperty("traceId").GetString().Should().NotBeNullOrWhiteSpace();
        root.GetProperty("instance").GetString().Should().Be("/throw");
    }

    [Fact]
    public async Task should_use_custom_type_uri_prefix()
    {
        // given
        await using var app = await _CreateAppAsync(
            handlerSetup: services =>
                services.AddTenantContextProblemDetails(o => o.TypeUriPrefix = "https://zad.org/errors/tenancy"),
            endpoint: () => throw new MissingTenantContextException()
        );
        using var client = _CreateClient(app);

        // when
        using var response = await client.GetAsync("/throw", AbortToken);

        // then
        var json = await response.Content.ReadAsStringAsync(AbortToken);
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("type").GetString().Should().Be("https://zad.org/errors/tenancy/tenant-required");
    }

    [Fact]
    public async Task should_use_custom_error_code()
    {
        // given
        await using var app = await _CreateAppAsync(
            handlerSetup: services =>
                services.AddTenantContextProblemDetails(o => o.ErrorCode = "zad.tenancy.required"),
            endpoint: () => throw new MissingTenantContextException()
        );
        using var client = _CreateClient(app);

        // when
        using var response = await client.GetAsync("/throw", AbortToken);

        // then
        var json = await response.Content.ReadAsStringAsync(AbortToken);
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("code").GetString().Should().Be("zad.tenancy.required");
    }

    [Fact]
    public async Task should_not_leak_exception_message_data_or_inner_exception_in_response_body()
    {
        // given - exception with sensitive message, Data tag, and inner exception
        var sensitiveInner = new InvalidOperationException("INNER_SECRET_DETAIL_query=user-id-42");
        var exception = new MissingTenantContextException("CUSTOM_OUTER_MESSAGE_with_sensitive_data", sensitiveInner);
        exception.Data["Headless.Messaging.FailureCode"] = "SENSITIVE_LAYER_TAG";

        await using var app = await _CreateAppAsync(
            handlerSetup: services => services.AddTenantContextProblemDetails(_ => { }),
            endpoint: () => throw exception
        );
        using var client = _CreateClient(app);

        // when
        using var response = await client.GetAsync("/throw", AbortToken);

        // then
        var body = await response.Content.ReadAsStringAsync(AbortToken);
        body.Should().NotContain("INNER_SECRET_DETAIL");
        body.Should().NotContain("CUSTOM_OUTER_MESSAGE");
        body.Should().NotContain("SENSITIVE_LAYER_TAG");
        body.Should().NotContain("Headless.Messaging.FailureCode");
        body.Should().Contain("tenancy.tenant-required");
    }

    [Fact]
    public async Task should_pass_through_non_tenancy_exceptions()
    {
        // given
        await using var app = await _CreateAppAsync(
            handlerSetup: services => services.AddTenantContextProblemDetails(_ => { }),
            endpoint: () => throw new InvalidOperationException("not tenancy")
        );
        using var client = _CreateClient(app);

        // when
        using var response = await client.GetAsync("/throw", AbortToken);

        // then - the tenancy handler returned false; the default 500 page (or empty body) is sent
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);

        // Negative assertion: response is not the tenancy 400 shape
        var body = await response.Content.ReadAsStringAsync(AbortToken);
        body.Should().NotContain("tenancy.tenant-required");
        body.Should().NotContain("tenant-context-required");
    }

    [Fact]
    public async Task should_win_when_registered_before_catch_all_handler()
    {
        // given - tenancy handler first, then a catch-all
        await using var app = await _CreateAppAsync(
            handlerSetup: services =>
            {
                services.AddTenantContextProblemDetails(_ => { });
                services.AddSingleton<IExceptionHandler, _CatchAllHandler>();
            },
            endpoint: () => throw new MissingTenantContextException()
        );
        using var client = _CreateClient(app);

        // when
        using var response = await client.GetAsync("/throw", AbortToken);

        // then - tenancy handler ran (catch-all would have set 599 with marker text)
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync(AbortToken);
        body.Should().Contain("tenancy.tenant-required");
        body.Should().NotContain("CATCH_ALL_MARKER");
    }

    [Fact]
    public async Task should_lose_when_registered_after_catch_all_handler()
    {
        // given - catch-all first, then tenancy
        await using var app = await _CreateAppAsync(
            handlerSetup: services =>
            {
                services.AddSingleton<IExceptionHandler, _CatchAllHandler>();
                services.AddTenantContextProblemDetails(_ => { });
            },
            endpoint: () => throw new MissingTenantContextException()
        );
        using var client = _CreateClient(app);

        // when
        using var response = await client.GetAsync("/throw", AbortToken);

        // then - catch-all wins; ordering matters as documented
        response.StatusCode.Should().Be((HttpStatusCode)599);
        var body = await response.Content.ReadAsStringAsync(AbortToken);
        body.Should().Contain("CATCH_ALL_MARKER");
    }

    private static async Task<WebApplication> _CreateAppAsync(Action<IServiceCollection> handlerSetup, Action endpoint)
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
        handlerSetup(builder.Services);

        var app = builder.Build();

        app.UseExceptionHandler();
        app.MapGet(
            "/throw",
            () =>
            {
                endpoint();
                return Results.Ok();
            }
        );

        await app.StartAsync(TestContext.Current.CancellationToken);
        return app;
    }

    private static HttpClient _CreateClient(WebApplication app)
    {
        return new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
    }

    private sealed class _CatchAllHandler : IExceptionHandler
    {
        public async ValueTask<bool> TryHandleAsync(
            HttpContext httpContext,
            Exception exception,
            CancellationToken cancellationToken
        )
        {
            httpContext.Response.StatusCode = 599;
            httpContext.Response.ContentType = MediaTypeNames.Text.Plain;
            await httpContext.Response.WriteAsync("CATCH_ALL_MARKER", cancellationToken);
            return true;
        }
    }
}
