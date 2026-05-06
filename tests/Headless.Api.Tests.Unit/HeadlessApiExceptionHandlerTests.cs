// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json;
using FluentValidation.Results;
using Headless.Abstractions;
using Headless.Api;
using Headless.Api.Abstractions;
using Headless.Constants;
using Headless.Exceptions;
using Headless.Primitives;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using JsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;

namespace Tests;

public sealed class HeadlessApiExceptionHandlerTests : TestBase
{
    [Fact]
    public async Task should_return_false_for_unknown_exception()
    {
        // given
        var problemDetailsService = Substitute.For<IProblemDetailsService>();
        var creator = Substitute.For<IProblemDetailsCreator>();
        var handler = _CreateHandler(problemDetailsService, creator);
        var httpContext = new DefaultHttpContext();

        // when
        var result = await handler.TryHandleAsync(
            httpContext,
            new InvalidOperationException("not framework-known"),
            TestContext.Current.CancellationToken
        );

        // then
        result.Should().BeFalse();
        await problemDetailsService.DidNotReceive().TryWriteAsync(Arg.Any<ProblemDetailsContext>());
    }

    [Fact]
    public async Task should_map_missing_tenant_context_exception_to_400()
    {
        // given
        var problemDetailsService = Substitute.For<IProblemDetailsService>();
        problemDetailsService.TryWriteAsync(Arg.Any<ProblemDetailsContext>()).Returns(true);
        var handler = _CreateHandler(problemDetailsService, _CreateRealCreator());
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
                    c.ProblemDetails.Status == 400
                    && c.ProblemDetails.Title == HeadlessProblemDetailsConstants.Titles.BadRequest
                    && (string)c.ProblemDetails.Extensions["code"]!
                        == HeadlessProblemDetailsConstants.Codes.TenantContextRequired
                )
            );
    }

    [Fact]
    public async Task should_map_conflict_exception_to_409_with_errors_extension()
    {
        // given
        var problemDetailsService = Substitute.For<IProblemDetailsService>();
        problemDetailsService.TryWriteAsync(Arg.Any<ProblemDetailsContext>()).Returns(true);
        var handler = _CreateHandler(problemDetailsService, _CreateRealCreator());
        var httpContext = new DefaultHttpContext();
        var errors = new[] { new ErrorDescriptor("conflict_code", "conflict description") };

        // when
        var result = await handler.TryHandleAsync(
            httpContext,
            new ConflictException(errors),
            TestContext.Current.CancellationToken
        );

        // then
        result.Should().BeTrue();
        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        await problemDetailsService
            .Received(1)
            .TryWriteAsync(
                Arg.Is<ProblemDetailsContext>(c =>
                    c.ProblemDetails.Status == 409 && c.ProblemDetails.Extensions.ContainsKey("errors")
                )
            );
    }

    [Fact]
    public async Task should_map_validation_exception_to_422_with_field_errors_extension()
    {
        // given
        var problemDetailsService = Substitute.For<IProblemDetailsService>();
        problemDetailsService.TryWriteAsync(Arg.Any<ProblemDetailsContext>()).Returns(true);
        var handler = _CreateHandler(problemDetailsService, _CreateRealCreator());
        var httpContext = new DefaultHttpContext();
        var failures = new[] { new ValidationFailure("Name", "Name is required") { ErrorCode = "NotEmpty" } };

        // when
        var result = await handler.TryHandleAsync(
            httpContext,
            new FluentValidation.ValidationException(failures),
            TestContext.Current.CancellationToken
        );

        // then
        result.Should().BeTrue();
        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity);
        await problemDetailsService
            .Received(1)
            .TryWriteAsync(
                Arg.Is<ProblemDetailsContext>(c =>
                    c.ProblemDetails.Status == 422 && c.ProblemDetails.Extensions.ContainsKey("errors")
                )
            );
    }

    [Fact]
    public async Task should_map_entity_not_found_exception_to_404()
    {
        // given
        var problemDetailsService = Substitute.For<IProblemDetailsService>();
        problemDetailsService.TryWriteAsync(Arg.Any<ProblemDetailsContext>()).Returns(true);
        var handler = _CreateHandler(problemDetailsService, _CreateRealCreator());
        var httpContext = new DefaultHttpContext();

        // when
        var result = await handler.TryHandleAsync(
            httpContext,
            new EntityNotFoundException("User", "123"),
            TestContext.Current.CancellationToken
        );

        // then
        result.Should().BeTrue();
        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status404NotFound);
    }

    [Fact]
    public async Task should_map_db_update_concurrency_exception_to_409_via_type_name_match()
    {
        // given
        var problemDetailsService = Substitute.For<IProblemDetailsService>();
        problemDetailsService.TryWriteAsync(Arg.Any<ProblemDetailsContext>()).Returns(true);
        var handler = _CreateHandler(problemDetailsService, _CreateRealCreator());
        var httpContext = new DefaultHttpContext();

        // when
        var result = await handler.TryHandleAsync(
            httpContext,
            new DbUpdateConcurrencyException("Concurrency conflict"),
            TestContext.Current.CancellationToken
        );

        // then
        result.Should().BeTrue();
        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status409Conflict);
    }

    [Fact]
    public async Task should_map_timeout_exception_to_408()
    {
        // given
        var problemDetailsService = Substitute.For<IProblemDetailsService>();
        problemDetailsService.TryWriteAsync(Arg.Any<ProblemDetailsContext>()).Returns(true);
        var handler = _CreateHandler(problemDetailsService, _CreateRealCreator());
        var httpContext = new DefaultHttpContext();

        // when
        var result = await handler.TryHandleAsync(
            httpContext,
            new TimeoutException("timed out"),
            TestContext.Current.CancellationToken
        );

        // then
        result.Should().BeTrue();
        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status408RequestTimeout);
    }

    [Fact]
    public async Task should_map_not_implemented_exception_to_501()
    {
        // given
        var problemDetailsService = Substitute.For<IProblemDetailsService>();
        problemDetailsService.TryWriteAsync(Arg.Any<ProblemDetailsContext>()).Returns(true);
        var handler = _CreateHandler(problemDetailsService, _CreateRealCreator());
        var httpContext = new DefaultHttpContext();

        // when
        var result = await handler.TryHandleAsync(
            httpContext,
            new NotImplementedException(),
            TestContext.Current.CancellationToken
        );

        // then
        result.Should().BeTrue();
        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status501NotImplemented);
    }

    [Fact]
    public async Task should_map_operation_canceled_exception_to_499_with_no_body()
    {
        // given
        var problemDetailsService = Substitute.For<IProblemDetailsService>();
        var handler = _CreateHandler(problemDetailsService, _CreateRealCreator());
        var httpContext = new DefaultHttpContext();
        var responseBody = new MemoryStream();
        httpContext.Response.Body = responseBody;

        // when
        var result = await handler.TryHandleAsync(
            httpContext,
            new OperationCanceledException(),
            TestContext.Current.CancellationToken
        );

        // then
        result.Should().BeTrue();
        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status499ClientClosedRequest);
        responseBody.Length.Should().Be(0); // No body for cancellation
        await problemDetailsService.DidNotReceive().TryWriteAsync(Arg.Any<ProblemDetailsContext>());
    }

    [Fact]
    public async Task should_map_inner_operation_canceled_to_499_with_no_body()
    {
        // given
        var problemDetailsService = Substitute.For<IProblemDetailsService>();
        var handler = _CreateHandler(problemDetailsService, _CreateRealCreator());
        var httpContext = new DefaultHttpContext();
        var inner = new OperationCanceledException("inner");
        var outer = new InvalidOperationException("outer", inner);

        // when
        var result = await handler.TryHandleAsync(httpContext, outer, TestContext.Current.CancellationToken);

        // then
        result.Should().BeTrue();
        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status499ClientClosedRequest);
        await problemDetailsService.DidNotReceive().TryWriteAsync(Arg.Any<ProblemDetailsContext>());
    }

    [Fact]
    public async Task should_return_false_when_canceled_after_response_started()
    {
        // given
        var problemDetailsService = Substitute.For<IProblemDetailsService>();
        var handler = _CreateHandler(problemDetailsService, _CreateRealCreator());
        var httpContext = new DefaultHttpContext();
        httpContext.Features.Set<IHttpResponseFeature>(new _StartedResponseFeature());

        // when
        var result = await handler.TryHandleAsync(
            httpContext,
            new OperationCanceledException(),
            TestContext.Current.CancellationToken
        );

        // then
        result.Should().BeFalse();
    }

    [Fact]
    public async Task should_fall_back_to_write_as_json_when_problem_details_service_returns_false()
    {
        // given
        var problemDetailsService = Substitute.For<IProblemDetailsService>();
        problemDetailsService.TryWriteAsync(Arg.Any<ProblemDetailsContext>()).Returns(false);
        var handler = _CreateHandler(problemDetailsService, _CreateRealCreator());
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
        httpContext.Response.ContentType.Should().Be("application/problem+json");

        responseBody.Position = 0;
        using var doc = await JsonDocument.ParseAsync(
            responseBody,
            cancellationToken: TestContext.Current.CancellationToken
        );
        doc.RootElement.GetProperty("status").GetInt32().Should().Be(400);
        doc.RootElement.GetProperty("code")
            .GetString()
            .Should()
            .Be(HeadlessProblemDetailsConstants.Codes.TenantContextRequired);
    }

    [Fact]
    public async Task should_return_false_when_problem_details_service_fails_after_response_started()
    {
        // given
        var problemDetailsService = Substitute.For<IProblemDetailsService>();
        problemDetailsService.TryWriteAsync(Arg.Any<ProblemDetailsContext>()).Returns(false);
        var handler = _CreateHandler(problemDetailsService, _CreateRealCreator());
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
        var sensitiveInner = new InvalidOperationException("INNER_SECRET_DETAIL");
        var exception = new MissingTenantContextException("CUSTOM_OUTER_MESSAGE", sensitiveInner);
        exception.Data["Headless.Messaging.FailureCode"] = "SENSITIVE_LAYER_TAG";

        var problemDetailsService = Substitute.For<IProblemDetailsService>();
        problemDetailsService.TryWriteAsync(Arg.Any<ProblemDetailsContext>()).Returns(false);
        var handler = _CreateHandler(problemDetailsService, _CreateRealCreator());
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
        body.Should().Contain(HeadlessProblemDetailsConstants.Codes.TenantContextRequired);
    }

    private static HeadlessApiExceptionHandler _CreateHandler(
        IProblemDetailsService problemDetailsService,
        IProblemDetailsCreator creator
    )
    {
        var jsonOptions = Options.Create(new JsonOptions());
        return new HeadlessApiExceptionHandler(
            jsonOptions,
            problemDetailsService,
            creator,
            NullLogger<HeadlessApiExceptionHandler>.Instance
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

    /// <summary>
    /// Test exception class named to match EF Core's DbUpdateConcurrencyException for the
    /// duck-typing detection in HeadlessApiExceptionHandler. Avoids a hard EF Core dep in the
    /// test project.
    /// </summary>
    private sealed class DbUpdateConcurrencyException(string message) : Exception(message);
}
