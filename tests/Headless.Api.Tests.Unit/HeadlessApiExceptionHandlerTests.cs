// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation.Results;
using Headless.Abstractions;
using Headless.Api;
using Headless.Api.Abstractions;
using Headless.Constants;
using Headless.Exceptions;
using Headless.Primitives;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using HeadlessApiExceptionHandler = Headless.Api.Middlewares.HeadlessApiExceptionHandler;
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
    public async Task should_map_operation_canceled_exception_to_499_with_no_body_when_request_aborted()
    {
        // given
        var problemDetailsService = Substitute.For<IProblemDetailsService>();
        var handler = _CreateHandler(problemDetailsService, _CreateRealCreator());
        var httpContext = _CreateAbortedContext();
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
    public async Task should_return_false_for_canceled_when_request_was_not_aborted()
    {
        // given - OCE thrown but RequestAborted not signaled (server-side cancellation, e.g.
        // RequestTimeouts middleware fired its own token, or a library threw OCE for a non-abort
        // reason). 499 ("Client Closed Request") would be misleading; let the default handler render.
        var problemDetailsService = Substitute.For<IProblemDetailsService>();
        var handler = _CreateHandler(problemDetailsService, _CreateRealCreator());
        var httpContext = new DefaultHttpContext();

        // when
        var result = await handler.TryHandleAsync(
            httpContext,
            new OperationCanceledException(),
            TestContext.Current.CancellationToken
        );

        // then
        result.Should().BeFalse();
        await problemDetailsService.DidNotReceive().TryWriteAsync(Arg.Any<ProblemDetailsContext>());
    }

    [Fact]
    public async Task should_map_inner_operation_canceled_to_499_with_no_body()
    {
        // given
        var problemDetailsService = Substitute.For<IProblemDetailsService>();
        var handler = _CreateHandler(problemDetailsService, _CreateRealCreator());
        var httpContext = _CreateAbortedContext();
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
    public async Task should_map_aggregate_with_non_first_canceled_to_499()
    {
        // given - AggregateException whose first inner is NOT OCE; OCE is the second inner.
        // Reproduces a Task.WhenAll(taskA, taskB) where taskA failed and taskB cancelled.
        var problemDetailsService = Substitute.For<IProblemDetailsService>();
        var handler = _CreateHandler(problemDetailsService, _CreateRealCreator());
        var httpContext = _CreateAbortedContext();
        var aggregate = new AggregateException(
            new InvalidOperationException("first inner is not cancellation"),
            new OperationCanceledException("second inner is cancellation")
        );

        // when
        var result = await handler.TryHandleAsync(httpContext, aggregate, TestContext.Current.CancellationToken);

        // then
        result.Should().BeTrue();
        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status499ClientClosedRequest);
        await problemDetailsService.DidNotReceive().TryWriteAsync(Arg.Any<ProblemDetailsContext>());
    }

    [Fact]
    public async Task should_map_aggregate_with_canceled_nested_in_non_first_child_to_499()
    {
        // given - AggregateException whose second inner is a non-AggregateException wrapping OCE.
        // Flatten() alone would not unwrap a regular Exception's InnerException, so this asserts
        // the recursive helper looks inside non-aggregate inners too.
        var problemDetailsService = Substitute.For<IProblemDetailsService>();
        var handler = _CreateHandler(problemDetailsService, _CreateRealCreator());
        var httpContext = _CreateAbortedContext();
        var aggregate = new AggregateException(
            new InvalidOperationException("first"),
            new InvalidOperationException("second wraps cancellation", new OperationCanceledException())
        );

        // when
        var result = await handler.TryHandleAsync(httpContext, aggregate, TestContext.Current.CancellationToken);

        // then
        result.Should().BeTrue();
        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status499ClientClosedRequest);
        await problemDetailsService.DidNotReceive().TryWriteAsync(Arg.Any<ProblemDetailsContext>());
    }

    [Fact]
    public async Task should_log_event_id_5002_and_add_activity_event_when_request_aborted()
    {
        // given
        var problemDetailsService = Substitute.For<IProblemDetailsService>();
        var logger = new CapturingLogger<HeadlessApiExceptionHandler>();
        var handler = _CreateHandler(problemDetailsService, _CreateRealCreator(), logger);
        var httpContext = _CreateAbortedContext();
        var activity = new System.Diagnostics.Activity("TestActivity");
        activity.Start();
        var activityFeature = Substitute.For<IHttpActivityFeature>();
        activityFeature.Activity.Returns(activity);
        httpContext.Features.Set(activityFeature);

        // when
        var result = await handler.TryHandleAsync(
            httpContext,
            new OperationCanceledException(),
            TestContext.Current.CancellationToken
        );

        // then
        result.Should().BeTrue();
        logger.Entries.Should().Contain(e => e.EventId.Id == 5002);
        activity.Events.Should().ContainSingle(e => e.Name == "Client cancelled the request");
        activity.Stop();
    }

    [Fact]
    public async Task should_return_false_when_canceled_after_response_started()
    {
        // given
        var problemDetailsService = Substitute.For<IProblemDetailsService>();
        var handler = _CreateHandler(problemDetailsService, _CreateRealCreator());
        var httpContext = _CreateAbortedContext();
        httpContext.Features.Set<IHttpResponseFeature>(new StartedResponseFeature());

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
        httpContext.Features.Set<IHttpResponseFeature>(new StartedResponseFeature());

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

    [Fact]
    public async Task should_return_false_when_problem_details_service_fails_and_request_does_not_accept_json()
    {
        // given
        var problemDetailsService = Substitute.For<IProblemDetailsService>();
        problemDetailsService.TryWriteAsync(Arg.Any<ProblemDetailsContext>()).Returns(false);
        var handler = _CreateHandler(problemDetailsService, _CreateRealCreator());
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Accept = "text/html"; // Explicit non-JSON accept

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
    public async Task should_log_5007_when_fallback_write_fails()
    {
        // given - TryWriteAsync fails, but WriteAsJsonAsync (the manual fallback) throws
        var problemDetailsService = Substitute.For<IProblemDetailsService>();
        problemDetailsService.TryWriteAsync(Arg.Any<ProblemDetailsContext>()).Returns(false);

        var logger = new CapturingLogger<HeadlessApiExceptionHandler>();
        var handler = _CreateHandler(problemDetailsService, _CreateRealCreator(), logger);

        var httpContext = new DefaultHttpContext();
        // Force WriteAsJsonAsync to fail by providing a body that throws on write
        var failingStream = Substitute.For<Stream>();
        failingStream.CanWrite.Returns(true);
        failingStream
            .WriteAsync(Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>())
            .Returns(_ => ValueTask.FromException(new IOException("disk full")));
        httpContext.Response.Body = failingStream;

        // when
        var result = await handler.TryHandleAsync(
            httpContext,
            new MissingTenantContextException(),
            TestContext.Current.CancellationToken
        );

        // then
        result.Should().BeFalse();
        logger.Entries.Should().Contain(e => e.EventId.Id == 5007);
        logger.Entries.Should().Contain(e => e.Message.Contains("IOException"));
    }

    [Fact]
    public async Task should_return_false_when_fallback_write_is_canceled()
    {
        // given
        var problemDetailsService = Substitute.For<IProblemDetailsService>();
        problemDetailsService.TryWriteAsync(Arg.Any<ProblemDetailsContext>()).Returns(false);
        var handler = _CreateHandler(problemDetailsService, _CreateRealCreator());
        var httpContext = new DefaultHttpContext();

        var failingStream = Substitute.For<Stream>();
        failingStream.CanWrite.Returns(true);
        failingStream
            .WriteAsync(Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>())
            .Returns(_ => ValueTask.FromException(new OperationCanceledException()));
        httpContext.Response.Body = failingStream;

        // when
        var result = await handler.TryHandleAsync(
            httpContext,
            new MissingTenantContextException(),
            TestContext.Current.CancellationToken
        );

        // then
        result.Should().BeFalse();
        // Should not log 5007 for cancellation (it has its own catch block)
    }

    [Fact]
    public async Task should_log_db_concurrency_exception_with_event_id_5003()
    {
        // given
        var problemDetailsService = Substitute.For<IProblemDetailsService>();
        problemDetailsService.TryWriteAsync(Arg.Any<ProblemDetailsContext>()).Returns(true);
        var logger = new CapturingLogger<HeadlessApiExceptionHandler>();
        var handler = _CreateHandler(problemDetailsService, _CreateRealCreator(), logger);
        var httpContext = new DefaultHttpContext();

        // when
        await handler.TryHandleAsync(
            httpContext,
            new DbUpdateConcurrencyException("conflict"),
            TestContext.Current.CancellationToken
        );

        // then
        logger.Entries.Should().Contain(e => e.EventId.Id == 5003);
    }

    [Fact]
    public async Task should_log_timeout_exception_with_event_id_5004()
    {
        // given
        var problemDetailsService = Substitute.For<IProblemDetailsService>();
        problemDetailsService.TryWriteAsync(Arg.Any<ProblemDetailsContext>()).Returns(true);
        var logger = new CapturingLogger<HeadlessApiExceptionHandler>();
        var handler = _CreateHandler(problemDetailsService, _CreateRealCreator(), logger);
        var httpContext = new DefaultHttpContext();

        // when
        await handler.TryHandleAsync(
            httpContext,
            new TimeoutException("timed out"),
            TestContext.Current.CancellationToken
        );

        // then
        logger.Entries.Should().Contain(e => e.EventId.Id == 5004);
    }

    [Fact]
    public async Task should_log_5006_via_early_guard_and_skip_TryWriteAsync_when_response_has_started_at_entry()
    {
        // given - response has already started before the handler runs; the early guard must
        // short-circuit before attempting to mutate StatusCode or call TryWriteAsync.
        var problemDetailsService = Substitute.For<IProblemDetailsService>();
        var logger = new CapturingLogger<HeadlessApiExceptionHandler>();
        var handler = _CreateHandler(problemDetailsService, _CreateRealCreator(), logger);
        var httpContext = new DefaultHttpContext();
        httpContext.Features.Set<IHttpResponseFeature>(new StartedResponseFeature());

        // when
        var result = await handler.TryHandleAsync(
            httpContext,
            new MissingTenantContextException(),
            TestContext.Current.CancellationToken
        );

        // then
        result.Should().BeFalse();
        logger
            .Entries.Should()
            .Contain(e => e.EventId.Id == 5006 && e.Message.Contains(nameof(MissingTenantContextException)));
        await problemDetailsService.DidNotReceive().TryWriteAsync(Arg.Any<ProblemDetailsContext>());
    }

    [Fact]
    public async Task should_log_5006_via_fallback_guard_when_TryWriteAsync_starts_response_then_fails()
    {
        // given - HasStarted is false at entry (early guard passes), but TryWriteAsync flips it to
        // true mid-write and then returns false. The fallback guard must catch this and log 5006.
        var responseFeature = new MutableResponseFeature();
        var problemDetailsService = Substitute.For<IProblemDetailsService>();
        problemDetailsService
            .TryWriteAsync(Arg.Any<ProblemDetailsContext>())
            .Returns(_ =>
            {
                responseFeature.HasStarted = true;
                return ValueTask.FromResult(false);
            });
        var logger = new CapturingLogger<HeadlessApiExceptionHandler>();
        var handler = _CreateHandler(problemDetailsService, _CreateRealCreator(), logger);
        var httpContext = new DefaultHttpContext();
        httpContext.Features.Set<IHttpResponseFeature>(responseFeature);

        // when
        var result = await handler.TryHandleAsync(
            httpContext,
            new MissingTenantContextException(),
            TestContext.Current.CancellationToken
        );

        // then
        result.Should().BeFalse();
        logger.Entries.Should().Contain(e => e.EventId.Id == 5006);
        await problemDetailsService.Received(1).TryWriteAsync(Arg.Any<ProblemDetailsContext>());
    }

    private static HeadlessApiExceptionHandler _CreateHandler(
        IProblemDetailsService problemDetailsService,
        IProblemDetailsCreator creator,
        ILogger<HeadlessApiExceptionHandler>? logger = null
    )
    {
        var jsonOptions = Options.Create(new JsonOptions());
        return new HeadlessApiExceptionHandler(
            jsonOptions,
            problemDetailsService,
            creator,
            logger ?? NullLogger<HeadlessApiExceptionHandler>.Instance
        );
    }

    private static DefaultHttpContext _CreateAbortedContext()
    {
        var context = new DefaultHttpContext();
        var lifetime = Substitute.For<IHttpRequestLifetimeFeature>();
        var cts = new CancellationTokenSource();
        cts.Cancel();
        lifetime.RequestAborted.Returns(cts.Token);
        context.Features.Set(lifetime);
        return context;
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

    private sealed class StartedResponseFeature : IHttpResponseFeature
    {
        public int StatusCode { get; set; } = 200;
        public string? ReasonPhrase { get; set; }
        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
        public Stream Body { get; set; } = Stream.Null;
        public bool HasStarted => true;

        public void OnStarting(Func<object, Task> callback, object state) { }

        public void OnCompleted(Func<object, Task> callback, object state) { }
    }

    private sealed class MutableResponseFeature : IHttpResponseFeature
    {
        public int StatusCode { get; set; } = 200;
        public string? ReasonPhrase { get; set; }
        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
        public Stream Body { get; set; } = Stream.Null;
        public bool HasStarted { get; set; }

        public void OnStarting(Func<object, Task> callback, object state) { }

        public void OnCompleted(Func<object, Task> callback, object state) { }
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            Entries.Add(new LogEntry(logLevel, eventId, exception, formatter(state, exception)));
        }

        public sealed record LogEntry(LogLevel Level, EventId EventId, Exception? Exception, string Message);
    }
}
