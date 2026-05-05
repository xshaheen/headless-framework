// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Text.Json;
using Headless.Abstractions;
using Headless.Api.Abstractions;
using Headless.Api.MultiTenancy;
using Headless.Constants;
using Headless.Primitives;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Tests.MultiTenancy;

public sealed class TenantContextExceptionHandlerTests : TestBase
{
    private const string _DefaultErrorCode = "tenancy.tenant-required";
    private const string _DefaultPrefix = "https://errors.example.com/tenancy";

    [Fact]
    public async Task should_return_false_when_exception_is_not_missing_tenant_context()
    {
        // given
        var problemDetailsService = Substitute.For<IProblemDetailsService>();
        var creator = Substitute.For<IProblemDetailsCreator>();
        var handler = _CreateHandler(problemDetailsService, creator);
        var httpContext = new DefaultHttpContext();
        var exception = new InvalidOperationException("not tenancy");

        // when
        var result = await handler.TryHandleAsync(httpContext, exception, TestContext.Current.CancellationToken);

        // then
        result.Should().BeFalse();
        await problemDetailsService.DidNotReceive().TryWriteAsync(Arg.Any<ProblemDetailsContext>());
        creator.DidNotReceive().TenantRequired(Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task should_handle_missing_tenant_context_via_problem_details_service()
    {
        // given
        var problemDetailsService = Substitute.For<IProblemDetailsService>();
        problemDetailsService.TryWriteAsync(Arg.Any<ProblemDetailsContext>()).Returns(true);
        var creator = _CreateRealCreator();
        var handler = _CreateHandler(problemDetailsService, creator);
        var httpContext = new DefaultHttpContext();

        // when
        var result = await handler.TryHandleAsync(
            httpContext,
            new MissingTenantContextException(),
            TestContext.Current.CancellationToken
        );

        // then
        result.Should().BeTrue();
        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        await problemDetailsService
            .Received(1)
            .TryWriteAsync(
                Arg.Is<ProblemDetailsContext>(c =>
                    c.ProblemDetails.Title == HeadlessProblemDetailsConstants.Titles.TenantContextRequired
                    && c.ProblemDetails.Status == StatusCodes.Status400BadRequest
                    && c.ProblemDetails.Type == _DefaultPrefix + "/tenant-required"
                )
            );
    }

    [Fact]
    public async Task should_fall_back_to_write_as_json_when_problem_details_service_returns_false()
    {
        // given
        var problemDetailsService = Substitute.For<IProblemDetailsService>();
        problemDetailsService.TryWriteAsync(Arg.Any<ProblemDetailsContext>()).Returns(false);
        var creator = _CreateRealCreator();
        var handler = _CreateHandler(problemDetailsService, creator);

        var httpContext = new DefaultHttpContext();
        var responseBody = new MemoryStream();
        httpContext.Response.Body = responseBody;

        // when
        var result = await handler.TryHandleAsync(
            httpContext,
            new MissingTenantContextException(),
            TestContext.Current.CancellationToken
        );

        // then
        result.Should().BeTrue();
        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        httpContext.Response.ContentType.Should().Be("application/problem+json");

        responseBody.Position = 0;
        using var doc = await JsonDocument.ParseAsync(
            responseBody,
            cancellationToken: TestContext.Current.CancellationToken
        );
        doc.RootElement.GetProperty("status").GetInt32().Should().Be(400);
        doc.RootElement.GetProperty("title").GetString().Should().Be("tenant-context-required");
        doc.RootElement.GetProperty("type").GetString().Should().Be(_DefaultPrefix + "/tenant-required");
        doc.RootElement.GetProperty("code").GetString().Should().Be(_DefaultErrorCode);
    }

    [Fact]
    public async Task should_return_false_when_response_already_started_and_problem_details_service_failed()
    {
        // given
        var problemDetailsService = Substitute.For<IProblemDetailsService>();
        problemDetailsService.TryWriteAsync(Arg.Any<ProblemDetailsContext>()).Returns(false);
        var creator = _CreateRealCreator();
        var handler = _CreateHandler(problemDetailsService, creator);

        var httpContext = new DefaultHttpContext();
        httpContext.Features.Set<IHttpResponseFeature>(new _StartedResponseFeature());

        // when
        var result = await handler.TryHandleAsync(
            httpContext,
            new MissingTenantContextException(),
            TestContext.Current.CancellationToken
        );

        // then
        result.Should().BeFalse();
    }

    [Fact]
    public async Task should_not_leak_inner_exception_or_message_in_fallback_response()
    {
        // given - exception with sensitive inner exception and custom message
        var sensitiveInner = new InvalidOperationException("INNER_SECRET_DETAIL_query=user-id-42");
        var exception = new MissingTenantContextException("CUSTOM_OUTER_MESSAGE_with_sensitive_data", sensitiveInner);
        exception.Data["Headless.Messaging.FailureCode"] = "SENSITIVE_LAYER_TAG";

        var problemDetailsService = Substitute.For<IProblemDetailsService>();
        problemDetailsService.TryWriteAsync(Arg.Any<ProblemDetailsContext>()).Returns(false);
        var creator = _CreateRealCreator();
        var handler = _CreateHandler(problemDetailsService, creator);

        var httpContext = new DefaultHttpContext();
        var responseBody = new MemoryStream();
        httpContext.Response.Body = responseBody;

        // when
        await handler.TryHandleAsync(httpContext, exception, TestContext.Current.CancellationToken);

        // then - response body must not contain any exception-internal content
        responseBody.Position = 0;
        using var reader = new StreamReader(responseBody);
        var body = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);

        body.Should().NotContain("INNER_SECRET_DETAIL");
        body.Should().NotContain("CUSTOM_OUTER_MESSAGE");
        body.Should().NotContain("SENSITIVE_LAYER_TAG");
        body.Should().NotContain("Headless.Messaging.FailureCode");
        body.Should().Contain(_DefaultErrorCode);
    }

    [Fact]
    public async Task should_use_custom_type_uri_prefix_from_options()
    {
        // given
        var problemDetailsService = Substitute.For<IProblemDetailsService>();
        problemDetailsService.TryWriteAsync(Arg.Any<ProblemDetailsContext>()).Returns(true);
        var creator = _CreateRealCreator();
        var optionsValue = new TenantContextProblemDetailsOptions
        {
            TypeUriPrefix = "https://custom.example.com/errors/tenancy/",
            ErrorCode = "custom.code",
        };
        var handler = _CreateHandler(problemDetailsService, creator, optionsValue);

        // when
        await handler.TryHandleAsync(
            new DefaultHttpContext(),
            new MissingTenantContextException(),
            TestContext.Current.CancellationToken
        );

        // then - trailing slash trimmed, custom code applied
        await problemDetailsService
            .Received(1)
            .TryWriteAsync(
                Arg.Is<ProblemDetailsContext>(c =>
                    c.ProblemDetails.Type == "https://custom.example.com/errors/tenancy/tenant-required"
                    && (string)c.ProblemDetails.Extensions["code"]! == "custom.code"
                )
            );
    }

    private static TenantContextExceptionHandler _CreateHandler(
        IProblemDetailsService problemDetailsService,
        IProblemDetailsCreator creator,
        TenantContextProblemDetailsOptions? optionsValue = null
    )
    {
        var options = Options.Create(
            optionsValue ?? new TenantContextProblemDetailsOptions { TypeUriPrefix = _DefaultPrefix }
        );
        return new TenantContextExceptionHandler(
            options,
            problemDetailsService,
            creator,
            NullLogger<TenantContextExceptionHandler>.Instance
        );
    }

    private static ProblemDetailsCreator _CreateRealCreator()
    {
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var buildInfo = Substitute.For<IBuildInformationAccessor>();
        buildInfo.GetBuildNumber().Returns("1.0.0");
        buildInfo.GetCommitNumber().Returns("abc123");
        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        var apiBehaviorOptions = Options.Create(new ApiBehaviorOptions());
        return new ProblemDetailsCreator(timeProvider, buildInfo, httpContextAccessor, apiBehaviorOptions);
    }

    private sealed class _StartedResponseFeature : IHttpResponseFeature
    {
        public int StatusCode { get; set; } = 200;
        public string? ReasonPhrase { get; set; }
        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
        public Stream Body { get; set; } = Stream.Null;
        public bool HasStarted => true;

        public void OnStarting(Func<object, Task> callback, object state) { }

        public void OnCompleted(Func<object, Task> callback, object state) { }
    }
}
