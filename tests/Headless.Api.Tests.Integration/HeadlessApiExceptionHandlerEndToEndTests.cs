// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Net.Mime;
using FluentValidation.Results;
using Headless.Abstractions;
using Headless.Api;
using Headless.Constants;
using Headless.Exceptions;
using Headless.Primitives;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Tests;

public sealed class HeadlessApiExceptionHandlerEndToEndTests : TestBase
{
    [Fact]
    public async Task should_map_missing_tenant_context_exception_to_normalized_400()
    {
        // given - AddHeadlessProblemDetails auto-registers the tenancy handler
        await using var app = await _CreateAppAsync(
            handlerSetup: _ => { },
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
        root.GetProperty("title").GetString().Should().Be(HeadlessProblemDetailsConstants.Titles.BadRequest);
        root.GetProperty("detail")
            .GetString()
            .Should()
            .Be(HeadlessProblemDetailsConstants.Details.TenantContextRequired);
        root.GetProperty("code").GetString().Should().Be(HeadlessProblemDetailsConstants.Codes.TenantContextRequired);
        root.GetProperty("traceId").GetString().Should().NotBeNullOrWhiteSpace();
        root.GetProperty("instance").GetString().Should().Be("/throw");
    }

    [Fact]
    public async Task should_not_leak_exception_message_data_or_inner_exception_in_response_body()
    {
        // given - exception with sensitive message, Data tag, and inner exception
        var sensitiveInner = new InvalidOperationException("INNER_SECRET_DETAIL_query=user-id-42");
        var exception = new MissingTenantContextException("CUSTOM_OUTER_MESSAGE_with_sensitive_data", sensitiveInner);
        exception.Data["Headless.Messaging.FailureCode"] = "SENSITIVE_LAYER_TAG";

        await using var app = await _CreateAppAsync(handlerSetup: _ => { }, endpoint: () => throw exception);
        using var client = _CreateClient(app);

        // when
        using var response = await client.GetAsync("/throw", AbortToken);

        // then
        var body = await response.Content.ReadAsStringAsync(AbortToken);
        body.Should().NotContain("INNER_SECRET_DETAIL");
        body.Should().NotContain("CUSTOM_OUTER_MESSAGE");
        body.Should().NotContain("SENSITIVE_LAYER_TAG");
        body.Should().NotContain("Headless.Messaging.FailureCode");
        body.Should().Contain(HeadlessProblemDetailsConstants.Codes.TenantContextRequired);
    }

    [Fact]
    public async Task should_map_conflict_exception_to_normalized_409()
    {
        // given
        var errors = new[] { new ErrorDescriptor("conflict_code", "Already exists") };
        await using var app = await _CreateAppAsync(
            handlerSetup: _ => { },
            endpoint: () => throw new ConflictException(errors)
        );
        using var client = _CreateClient(app);

        // when
        using var response = await client.GetAsync("/throw", AbortToken);

        // then
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var json = await response.Content.ReadAsStringAsync(AbortToken);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        root.GetProperty("status").GetInt32().Should().Be(409);
        root.GetProperty("title").GetString().Should().Be(HeadlessProblemDetailsConstants.Titles.Conflict);
        root.GetProperty("traceId").GetString().Should().NotBeNullOrWhiteSpace();
        // The errors extension is preserved through the pipeline.
        root.TryGetProperty("errors", out _).Should().BeTrue();
    }

    [Fact]
    public async Task should_map_validation_exception_to_normalized_422()
    {
        // given
        var failures = new[] { new ValidationFailure("Name", "Name is required") { ErrorCode = "NotEmpty" } };
        await using var app = await _CreateAppAsync(
            handlerSetup: _ => { },
            endpoint: () => throw new FluentValidation.ValidationException(failures)
        );
        using var client = _CreateClient(app);

        // when
        using var response = await client.GetAsync("/throw", AbortToken);

        // then
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var json = await response.Content.ReadAsStringAsync(AbortToken);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        root.GetProperty("status").GetInt32().Should().Be(422);
        root.GetProperty("title").GetString().Should().Be(HeadlessProblemDetailsConstants.Titles.UnprocessableEntity);
        root.GetProperty("traceId").GetString().Should().NotBeNullOrWhiteSpace();
        // The field-errors extension is preserved through the pipeline.
        root.TryGetProperty("errors", out _).Should().BeTrue();
    }

    [Fact]
    public async Task should_pass_through_non_tenancy_exceptions()
    {
        // given
        await using var app = await _CreateAppAsync(
            handlerSetup: _ => { },
            endpoint: () => throw new InvalidOperationException("not tenancy")
        );
        using var client = _CreateClient(app);

        // when
        using var response = await client.GetAsync("/throw", AbortToken);

        // then - the tenancy handler returned false; the default 500 page (or empty body) is sent
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);

        // Negative assertion: response is not the tenancy 400 shape — the unique tenancy code
        // is the identifier; title is shared `bad-request` so it can't be used to distinguish.
        var body = await response.Content.ReadAsStringAsync(AbortToken);
        body.Should().NotContain(HeadlessProblemDetailsConstants.Codes.TenantContextRequired);
    }

    [Theory]
    [InlineData("EntityNotFoundException")]
    [InlineData("DbUpdateConcurrencyException")]
    [InlineData("TimeoutException")]
    [InlineData("NotImplementedException")]
    public async Task should_not_leak_sentinel_message_in_response_body_for_mapped_exception(string kind)
    {
        // given - a sentinel string that must never appear in any response body for any mapped
        // exception type. Note: ConflictException and ValidationException intentionally expose the
        // caller-provided ErrorDescriptor / FailureMessage payloads via the `errors` extension —
        // covered by other tests; not asserted here as those payloads are part of the contract.
        const string sentinel = "LEAKED-SENTINEL-XYZ";
        Action endpoint = kind switch
        {
            "EntityNotFoundException" => () => throw new EntityNotFoundException(sentinel, sentinel),
            "DbUpdateConcurrencyException" => () => throw new DbUpdateConcurrencyException(sentinel),
            "TimeoutException" => () => throw new TimeoutException(sentinel),
            "NotImplementedException" => () => throw new NotImplementedException(sentinel),
            _ => throw new InvalidOperationException($"unknown kind {kind}"),
        };

        await using var app = await _CreateAppAsync(handlerSetup: _ => { }, endpoint: endpoint);
        using var client = _CreateClient(app);

        // when
        using var response = await client.GetAsync("/throw", AbortToken);

        // then - body must never echo the exception message
        var body = await response.Content.ReadAsStringAsync(AbortToken);
        body.Should().NotContain(sentinel);
    }

    [Fact]
    public async Task should_map_missing_tenant_context_exception_thrown_from_mvc_controller_to_normalized_400()
    {
        // given - same global handler reaches MVC controllers as well as Minimal-API endpoints
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = EnvironmentNames.Test }
        );
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        builder.Services.AddRouting();
        builder.Services.AddHttpContextAccessor();
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.TryAddSingleton<IBuildInformationAccessor, BuildInformationAccessor>();
        builder.Services.AddHeadlessProblemDetails();
        builder.Services.AddControllers().AddApplicationPart(typeof(TenancyThrowingController).Assembly);

        await using var app = builder.Build();
        app.UseExceptionHandler();
        app.MapControllers();
        await app.StartAsync(AbortToken);

        using var client = _CreateClient(app);

        // when
        using var response = await client.GetAsync("/mvc/throw-tenancy", AbortToken);

        // then
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var json = await response.Content.ReadAsStringAsync(AbortToken);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        root.GetProperty("status").GetInt32().Should().Be(400);
        root.GetProperty("code").GetString().Should().Be(HeadlessProblemDetailsConstants.Codes.TenantContextRequired);
    }

    [Fact]
    public async Task should_lose_when_catch_all_registered_before_problem_details_setup()
    {
        // given - a catch-all handler registered BEFORE AddHeadlessProblemDetails wins
        // because IExceptionHandler runs in registration order. Demonstrates that consumers
        // who need their own catch-all must order their setup appropriately.
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = EnvironmentNames.Test }
        );
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        builder.Services.AddRouting();
        builder.Services.AddHttpContextAccessor();
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.TryAddSingleton<IBuildInformationAccessor, BuildInformationAccessor>();

        // catch-all registered FIRST
        builder.Services.AddSingleton<IExceptionHandler, _CatchAllHandler>();
        // tenancy handler registered SECOND via AddHeadlessProblemDetails
        builder.Services.AddHeadlessProblemDetails();

        await using var app = builder.Build();
        app.UseExceptionHandler();
        app.MapGet("/throw", (Action)(() => throw new MissingTenantContextException()));
        await app.StartAsync(AbortToken);

        using var client = _CreateClient(app);

        // when
        using var response = await client.GetAsync("/throw", AbortToken);

        // then - catch-all wins; ordering matters as documented
        response.StatusCode.Should().Be((HttpStatusCode)599);
        var body = await response.Content.ReadAsStringAsync(AbortToken);
        body.Should().Contain("CATCH_ALL_MARKER");
    }

    private async Task<WebApplication> _CreateAppAsync(Action<IServiceCollection> handlerSetup, Action endpoint)
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

        await app.StartAsync(AbortToken);
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

[Microsoft.AspNetCore.Mvc.ApiController]
[Microsoft.AspNetCore.Mvc.Route("mvc")]
public sealed class TenancyThrowingController : Microsoft.AspNetCore.Mvc.ControllerBase
{
    [Microsoft.AspNetCore.Mvc.HttpGet("throw-tenancy")]
    public Microsoft.AspNetCore.Mvc.IActionResult ThrowTenancy() => throw new MissingTenantContextException();
}
