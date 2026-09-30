// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0062 // Local functions can be made static - closures needed for capturing test state

using Headless.Abstractions;
using Headless.Api.Middlewares;
using Headless.Api.MultiTenancy;
using Headless.Constants;
using Headless.Primitives;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Middlewares;

public sealed class StatusCodesRewriterMiddlewareTests : TestBase
{
    private static StatusCodesRewriterMiddleware _CreateMiddleware(IProblemDetailsCreator? problemDetailsCreator = null)
    {
        problemDetailsCreator ??= _CreateProblemDetailsCreator();
        return new StatusCodesRewriterMiddleware(problemDetailsCreator);
    }

    private static IProblemDetailsCreator _CreateProblemDetailsCreator()
    {
        var creator = Substitute.For<IProblemDetailsCreator>();

        creator
            .Unauthorized()
            .Returns(
                new ProblemDetails
                {
                    Status = StatusCodes.Status401Unauthorized,
                    Title = HeadlessProblemDetailsConstants.Titles.Unauthorized,
                    Detail = HeadlessProblemDetailsConstants.Details.Unauthorized,
                }
            );

        creator
            .Forbidden()
            .Returns(
                new ProblemDetails
                {
                    Status = StatusCodes.Status403Forbidden,
                    Title = HeadlessProblemDetailsConstants.Titles.Forbidden,
                    Detail = HeadlessProblemDetailsConstants.Details.Forbidden,
                }
            );

        creator
            .EndpointNotFound()
            .Returns(
                new ProblemDetails
                {
                    Status = StatusCodes.Status404NotFound,
                    Title = HeadlessProblemDetailsConstants.Titles.EndpointNotFound,
                    Detail = HeadlessProblemDetailsConstants.Details.EndpointNotFound("/test"),
                }
            );

        return creator;
    }

    // The creator is registered in request services for the tenancy rejections, which resolve it per
    // request rather than receiving the middleware's.
    private static DefaultHttpContext _CreateContext(IProblemDetailsCreator? problemDetailsCreator = null)
    {
        var ctx = new DefaultHttpContext();
        var serviceCollection = new ServiceCollection().AddLogging().AddProblemDetails();

        if (problemDetailsCreator is not null)
        {
            serviceCollection.AddSingleton(problemDetailsCreator);
        }

        var services = serviceCollection.BuildServiceProvider();
        ctx.RequestServices = services;
        ctx.Response.Body = new MemoryStream();
        return ctx;
    }

    [Fact]
    public async Task should_call_next_and_not_modify_response_when_2xx()
    {
        // given
        var middleware = _CreateMiddleware();
        var context = _CreateContext();
        var nextCalled = false;
        Task next(HttpContext ctx)
        {
            nextCalled = true;
            ctx.Response.StatusCode = StatusCodes.Status200OK;
            return Task.CompletedTask;
        }

        // when
        await middleware.InvokeAsync(context, next);

        // then
        nextCalled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        context.Response.ContentType.Should().BeNullOrEmpty();
    }

    [Fact]
    public async Task should_call_next_and_not_modify_response_when_3xx()
    {
        // given
        var middleware = _CreateMiddleware();
        var context = _CreateContext();
        var nextCalled = false;
        Task next(HttpContext ctx)
        {
            nextCalled = true;
            ctx.Response.StatusCode = StatusCodes.Status301MovedPermanently;
            return Task.CompletedTask;
        }

        // when
        await middleware.InvokeAsync(context, next);

        // then
        nextCalled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status301MovedPermanently);
        context.Response.ContentType.Should().BeNullOrEmpty();
    }

    [Fact]
    public async Task should_not_modify_when_response_already_started()
    {
        // given
        var problemCreator = Substitute.For<IProblemDetailsCreator>();
        var middleware = _CreateMiddleware(problemCreator);
        var context = _CreateContext();

        // Simulate HasStarted by using a custom feature
        var responseFeature = Substitute.For<IHttpResponseFeature>();
        responseFeature.HasStarted.Returns(true);
        context.Features.Set(responseFeature);

        Task next(HttpContext ctx)
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        }

        // when
        await middleware.InvokeAsync(context, next);

        // then
        problemCreator.DidNotReceive().Unauthorized();
    }

    [Fact]
    public async Task should_not_modify_when_content_length_set()
    {
        // given
        var problemCreator = Substitute.For<IProblemDetailsCreator>();
        var middleware = _CreateMiddleware(problemCreator);
        var context = _CreateContext();
        Task next(HttpContext ctx)
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            ctx.Response.ContentLength = 100;
            return Task.CompletedTask;
        }

        // when
        await middleware.InvokeAsync(context, next);

        // then
        problemCreator.DidNotReceive().Unauthorized();
    }

    [Fact]
    public async Task should_not_modify_when_content_type_set()
    {
        // given
        var problemCreator = Substitute.For<IProblemDetailsCreator>();
        var middleware = _CreateMiddleware(problemCreator);
        var context = _CreateContext();
        Task next(HttpContext ctx)
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            ctx.Response.ContentType = "application/json";
            return Task.CompletedTask;
        }

        // when
        await middleware.InvokeAsync(context, next);

        // then
        problemCreator.DidNotReceive().Unauthorized();
    }

    [Fact]
    public async Task should_return_problem_details_for_401()
    {
        // given
        var problemCreator = _CreateProblemDetailsCreator();
        var middleware = _CreateMiddleware(problemCreator);
        var context = _CreateContext();
        Task next(HttpContext ctx)
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        }

        // when
        await middleware.InvokeAsync(context, next);

        // then
        context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        context.Response.ContentType.Should().StartWith("application/problem+json");

        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body, leaveOpen: true);
        var body = await reader.ReadToEndAsync(AbortToken);

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        root.GetProperty("status").GetInt32().Should().Be(StatusCodes.Status401Unauthorized);
        root.GetProperty("title").GetString().Should().Be(HeadlessProblemDetailsConstants.Titles.Unauthorized);
    }

    [Fact]
    public async Task should_return_problem_details_for_403()
    {
        // given
        var problemCreator = _CreateProblemDetailsCreator();
        var middleware = _CreateMiddleware(problemCreator);
        var context = _CreateContext();
        Task next(HttpContext ctx)
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        }

        // when
        await middleware.InvokeAsync(context, next);

        // then
        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        context.Response.ContentType.Should().StartWith("application/problem+json");

        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body, leaveOpen: true);
        var body = await reader.ReadToEndAsync(AbortToken);

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        root.GetProperty("status").GetInt32().Should().Be(StatusCodes.Status403Forbidden);
        root.GetProperty("title").GetString().Should().Be(HeadlessProblemDetailsConstants.Titles.Forbidden);
    }

    [Fact]
    public async Task should_return_tenant_required_problem_details_when_typed_feature_set()
    {
        // given - TenantRequirementHandler sets this rejection when it fails authorization; it replaces
        // the generic Forbidden body with the structured g:tenant_required body. This is the path that
        // decouples tenant 403s from the IAuthorizationMiddlewareResultHandler registration order.
        var problemCreator = Substitute.For<IProblemDetailsCreator>();
        problemCreator
            .Forbidden(
                detail: HeadlessProblemDetailsConstants.Details.TenantContextRequired,
                error: HeadlessProblemDetailsConstants.Errors.TenantContextRequired
            )
            .Returns(
                new ProblemDetails
                {
                    Status = StatusCodes.Status403Forbidden,
                    Title = HeadlessProblemDetailsConstants.Titles.Forbidden,
                    Detail = HeadlessProblemDetailsConstants.Details.TenantContextRequired,
                    Extensions =
                    {
                        ["error"] = new { code = HeadlessProblemDetailsConstants.Errors.TenantContextRequired.Code },
                    },
                }
            );
        var middleware = _CreateMiddleware(problemCreator);
        var context = _CreateContext(problemCreator);
        context.TrySetStatusCodeRejection(TenantContextRequiredFeature.Instance);
        Task next(HttpContext ctx)
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        }

        // when
        await middleware.InvokeAsync(context, next);

        // then
        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        context.Response.ContentType.Should().StartWith("application/problem+json");

        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body, leaveOpen: true);
        var body = await reader.ReadToEndAsync(AbortToken);

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        root.GetProperty("status").GetInt32().Should().Be(StatusCodes.Status403Forbidden);
        root.GetProperty("error")
            .GetProperty("code")
            .GetString()
            .Should()
            .Be(HeadlessProblemDetailsConstants.Errors.TenantContextRequired.Code);
        problemCreator
            .Received(1)
            .Forbidden(
                detail: HeadlessProblemDetailsConstants.Details.TenantContextRequired,
                error: HeadlessProblemDetailsConstants.Errors.TenantContextRequired
            );
        problemCreator.DidNotReceive().Forbidden();
    }

    [Fact]
    public async Task should_return_problem_details_for_404()
    {
        // given
        var problemCreator = _CreateProblemDetailsCreator();
        var middleware = _CreateMiddleware(problemCreator);
        var context = _CreateContext();
        Task next(HttpContext ctx)
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            return Task.CompletedTask;
        }

        // when
        await middleware.InvokeAsync(context, next);

        // then
        context.Response.StatusCode.Should().Be(StatusCodes.Status404NotFound);
        context.Response.ContentType.Should().StartWith("application/problem+json");

        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body, leaveOpen: true);
        var body = await reader.ReadToEndAsync(AbortToken);

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        root.GetProperty("status").GetInt32().Should().Be(StatusCodes.Status404NotFound);
        root.GetProperty("title").GetString().Should().Be(HeadlessProblemDetailsConstants.Titles.EndpointNotFound);
    }

    [Fact]
    public async Task should_not_rewrite_when_status_is_401_and_tenant_feature_set()
    {
        // given - the tenant feature is scoped to 403; a 401 with the feature set must not be
        // mistakenly rewritten as a g:tenant_required response.
        var problemCreator = Substitute.For<IProblemDetailsCreator>();
        problemCreator.Unauthorized().Returns(new ProblemDetails { Status = StatusCodes.Status401Unauthorized });
        var middleware = _CreateMiddleware(problemCreator);
        var context = _CreateContext(problemCreator);
        context.TrySetStatusCodeRejection(TenantContextRequiredFeature.Instance);
        Task next(HttpContext ctx)
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        }

        // when
        await middleware.InvokeAsync(context, next);

        // then
        context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        problemCreator.Received(1).Unauthorized();
        problemCreator.DidNotReceive().Forbidden(Arg.Any<string?>(), Arg.Any<ErrorDescriptor?>());
        problemCreator.DidNotReceive().Forbidden();
    }

    [Fact]
    public async Task should_rewrite_when_typed_feature_set_even_if_content_type_already_set()
    {
        // given - the tenant rejection runs ahead of the Content-Type/Content-Length short-circuit so that
        // a partial response written by a consumer's IAuthorizationMiddlewareResultHandler is
        // replaced with the structured g:tenant_required body.
        var problemCreator = Substitute.For<IProblemDetailsCreator>();
        problemCreator
            .Forbidden(
                detail: HeadlessProblemDetailsConstants.Details.TenantContextRequired,
                error: HeadlessProblemDetailsConstants.Errors.TenantContextRequired
            )
            .Returns(
                new ProblemDetails
                {
                    Status = StatusCodes.Status403Forbidden,
                    Title = HeadlessProblemDetailsConstants.Titles.Forbidden,
                }
            );
        var middleware = _CreateMiddleware(problemCreator);
        var context = _CreateContext(problemCreator);
        context.TrySetStatusCodeRejection(TenantContextRequiredFeature.Instance);
        Task next(HttpContext ctx)
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            ctx.Response.ContentType = "text/plain"; // partial upstream write
            return Task.CompletedTask;
        }

        // when
        await middleware.InvokeAsync(context, next);

        // then
        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        problemCreator
            .Received(1)
            .Forbidden(
                detail: HeadlessProblemDetailsConstants.Details.TenantContextRequired,
                error: HeadlessProblemDetailsConstants.Errors.TenantContextRequired
            );
    }

    [Theory]
    [InlineData(StatusCodes.Status400BadRequest)]
    [InlineData(StatusCodes.Status405MethodNotAllowed)]
    [InlineData(StatusCodes.Status409Conflict)]
    [InlineData(StatusCodes.Status422UnprocessableEntity)]
    [InlineData(StatusCodes.Status429TooManyRequests)]
    [InlineData(StatusCodes.Status500InternalServerError)]
    public async Task should_not_modify_other_4xx_errors(int statusCode)
    {
        // given
        var problemCreator = Substitute.For<IProblemDetailsCreator>();
        var middleware = _CreateMiddleware(problemCreator);
        var context = _CreateContext();
        Task next(HttpContext ctx)
        {
            ctx.Response.StatusCode = statusCode;
            return Task.CompletedTask;
        }

        // when
        await middleware.InvokeAsync(context, next);

        // then
        context.Response.StatusCode.Should().Be(statusCode);
        problemCreator.DidNotReceive().Unauthorized();
        problemCreator.DidNotReceive().Forbidden();
        problemCreator.DidNotReceive().EndpointNotFound();
    }

    [Fact]
    public async Task should_set_content_type_to_problem_json()
    {
        // given
        var middleware = _CreateMiddleware();
        var context = _CreateContext();
        Task next(HttpContext ctx)
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        }

        // when
        await middleware.InvokeAsync(context, next);

        // then
        context.Response.ContentType.Should().StartWith("application/problem+json");
    }

    [Fact]
    public async Task should_let_an_accepting_rejection_own_the_response_whatever_the_status()
    {
        // given - a cookie-style scheme forbids with a 302, which the bare-status rewrite ignores
        var problemCreator = Substitute.For<IProblemDetailsCreator>();
        var middleware = _CreateMiddleware(problemCreator);
        var context = _CreateContext();
        var rejection = new RecordingRejection(accept: true, writeStatus: StatusCodes.Status404NotFound);
        context.TrySetStatusCodeRejection(rejection);
        Task next(HttpContext ctx)
        {
            ctx.Response.StatusCode = StatusCodes.Status302Found;
            return Task.CompletedTask;
        }

        // when
        await middleware.InvokeAsync(context, next);

        // then
        rejection.Calls.Should().Be(1);
        context.Response.StatusCode.Should().Be(StatusCodes.Status404NotFound);
        problemCreator.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task should_fall_back_to_the_bare_status_rewrite_when_the_rejection_declines()
    {
        // given
        var problemCreator = _CreateProblemDetailsCreator();
        var middleware = _CreateMiddleware(problemCreator);
        var context = _CreateContext();
        var rejection = new RecordingRejection(accept: false, writeStatus: StatusCodes.Status404NotFound);
        context.TrySetStatusCodeRejection(rejection);
        Task next(HttpContext ctx)
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        }

        // when
        await middleware.InvokeAsync(context, next);

        // then
        rejection.Calls.Should().Be(1);
        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        problemCreator.Received(1).Forbidden();
    }

    [Fact]
    public async Task should_not_offer_the_rejection_when_the_response_already_started()
    {
        // given
        var middleware = _CreateMiddleware();
        var context = _CreateContext();
        var responseFeature = Substitute.For<IHttpResponseFeature>();
        responseFeature.HasStarted.Returns(true);
        context.Features.Set(responseFeature);
        var rejection = new RecordingRejection(accept: true, writeStatus: StatusCodes.Status404NotFound);
        context.TrySetStatusCodeRejection(rejection);
        Task next(HttpContext ctx) => Task.CompletedTask;

        // when
        await middleware.InvokeAsync(context, next);

        // then
        rejection.Calls.Should().Be(0);
    }

    [Fact]
    public void should_keep_the_first_rejection_when_another_handler_tries_to_set_one()
    {
        // given
        var context = _CreateContext();
        var first = new RecordingRejection(accept: true, writeStatus: StatusCodes.Status404NotFound);
        var second = new RecordingRejection(accept: true, writeStatus: StatusCodes.Status403Forbidden);

        // when
        var firstStored = context.TrySetStatusCodeRejection(first);
        var secondStored = context.TrySetStatusCodeRejection(second);

        // then
        firstStored.Should().BeTrue();
        secondStored.Should().BeFalse();
        context.Features.Get<IStatusCodeRejectionFeature>().Should().BeSameAs(first);
    }

    private sealed class RecordingRejection(bool accept, int writeStatus) : IStatusCodeRejectionFeature
    {
        public int Calls { get; private set; }

        public Task<bool> TryWriteResponseAsync(HttpContext context)
        {
            Calls++;

            if (accept)
            {
                context.Response.StatusCode = writeStatus;
            }

            return Task.FromResult(accept);
        }
    }
}
