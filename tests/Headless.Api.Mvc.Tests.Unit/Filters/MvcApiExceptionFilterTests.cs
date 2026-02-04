// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation.Results;
using ValidationException = FluentValidation.ValidationException;
using Headless.Abstractions;
using Headless.Api.Abstractions;
using Headless.Api.Filters;
using Headless.Constants;
using Headless.Exceptions;
using Headless.Primitives;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Filters;

public sealed class MvcApiExceptionFilterTests : TestBase
{
    #region Helper Methods

    private static MvcApiExceptionFilter _CreateFilter(
        IProblemDetailsCreator? problemDetailsCreator = null,
        ILogger<MvcApiExceptionFilter>? logger = null
    )
    {
        problemDetailsCreator ??= _CreateProblemDetailsCreator();
        logger ??= Substitute.For<ILogger<MvcApiExceptionFilter>>();
        return new MvcApiExceptionFilter(problemDetailsCreator, logger);
    }

    private static ExceptionContext _CreateExceptionContext(
        Exception exception,
        string? acceptHeader = "application/json"
    )
    {
        var httpContext = new DefaultHttpContext();
        if (acceptHeader is not null)
        {
            httpContext.Request.Headers.Accept = acceptHeader;
        }

        var services = new ServiceCollection().AddLogging().AddProblemDetails().BuildServiceProvider();
        httpContext.RequestServices = services;
        httpContext.Response.Body = new MemoryStream();

        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        return new ExceptionContext(actionContext, []) { Exception = exception };
    }

    private static IProblemDetailsCreator _CreateProblemDetailsCreator()
    {
        var now = DateTimeOffset.UtcNow;
        var timeProvider = new FakeTimeProvider(now);

        var buildInformationAccessor = Substitute.For<IBuildInformationAccessor>();
        buildInformationAccessor.GetBuildNumber().Returns("test-build");
        buildInformationAccessor.GetCommitNumber().Returns("test-commit");

        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        httpContextAccessor.HttpContext.Returns(new DefaultHttpContext());

        var apiBehaviorOptions = Options.Create(new ApiBehaviorOptions());

        return new ProblemDetailsCreator(
            timeProvider,
            buildInformationAccessor,
            httpContextAccessor,
            apiBehaviorOptions
        );
    }

    private static async Task<JsonDocument> _GetResponseBody(ExceptionContext context)
    {
        context.HttpContext.Response.Body.Position = 0;
        using var reader = new StreamReader(context.HttpContext.Response.Body, leaveOpen: true);
        var body = await reader.ReadToEndAsync();
        return JsonDocument.Parse(body);
    }

    #endregion

    #region Happy Path Tests

    [Fact]
    public async Task should_skip_when_exception_already_handled()
    {
        // given
        var filter = _CreateFilter();
        var context = _CreateExceptionContext(new InvalidOperationException("test"));
        context.ExceptionHandled = true;

        // when
        await filter.OnExceptionAsync(context);

        // then
        context.HttpContext.Response.StatusCode.Should().Be(200); // unchanged default
    }

    [Theory]
    [InlineData("text/html")]
    [InlineData("text/plain")]
    [InlineData("application/xml")]
    public async Task should_skip_when_not_accepting_json(string acceptHeader)
    {
        // given
        var filter = _CreateFilter();
        var context = _CreateExceptionContext(new ConflictException("test"), acceptHeader);

        // when
        await filter.OnExceptionAsync(context);

        // then
        context.ExceptionHandled.Should().BeFalse();
        context.HttpContext.Response.StatusCode.Should().Be(200); // unchanged default
    }

    #endregion

    #region Exception Mapping Tests

    [Fact]
    public async Task should_return_409_for_ConflictException()
    {
        // given
        var filter = _CreateFilter();
        var errors = new ErrorDescriptor("test_code", "Test conflict message");
        var exception = new ConflictException(errors);
        var context = _CreateExceptionContext(exception);

        // when
        await filter.OnExceptionAsync(context);

        // then
        context.ExceptionHandled.Should().BeTrue();
        context.HttpContext.Response.StatusCode.Should().Be(StatusCodes.Status409Conflict);

        using var doc = await _GetResponseBody(context);
        var root = doc.RootElement;
        root.GetProperty("status").GetInt32().Should().Be(StatusCodes.Status409Conflict);
        root.GetProperty("title").GetString().Should().Be(HeadlessProblemDetailsConstants.Titles.Conflict);
        root.GetProperty("detail").GetString().Should().Be(HeadlessProblemDetailsConstants.Details.Conflict);
    }

    [Fact]
    public async Task should_return_422_for_ValidationException()
    {
        // given
        var filter = _CreateFilter();
        var failure = new ValidationFailure("Name", "Name is required")
        {
            ErrorCode = "NotEmptyValidator",
            FormattedMessagePlaceholderValues = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["PropertyName"] = "Name",
                ["PropertyValue"] = "",
            },
        };
        var exception = new ValidationException([failure]);
        var context = _CreateExceptionContext(exception);

        // when
        await filter.OnExceptionAsync(context);

        // then
        context.ExceptionHandled.Should().BeTrue();
        context.HttpContext.Response.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity);

        using var doc = await _GetResponseBody(context);
        var root = doc.RootElement;
        root.GetProperty("status").GetInt32().Should().Be(StatusCodes.Status422UnprocessableEntity);
        root.GetProperty("title").GetString().Should().Be(HeadlessProblemDetailsConstants.Titles.UnprocessableEntity);
        root.GetProperty("detail").GetString().Should().Be(HeadlessProblemDetailsConstants.Details.UnprocessableEntity);
    }

    [Fact]
    public async Task should_return_404_for_EntityNotFoundException()
    {
        // given
        var filter = _CreateFilter();
        var exception = new EntityNotFoundException("User", "123");
        var context = _CreateExceptionContext(exception);

        // when
        await filter.OnExceptionAsync(context);

        // then
        context.ExceptionHandled.Should().BeTrue();
        context.HttpContext.Response.StatusCode.Should().Be(StatusCodes.Status404NotFound);

        using var doc = await _GetResponseBody(context);
        var root = doc.RootElement;
        root.GetProperty("status").GetInt32().Should().Be(StatusCodes.Status404NotFound);
        root.GetProperty("title").GetString().Should().Be(HeadlessProblemDetailsConstants.Titles.EntityNotFound);
        root.GetProperty("detail").GetString().Should().Be(HeadlessProblemDetailsConstants.Details.EntityNotFound);
    }

    [Fact]
    public async Task should_return_409_for_DbUpdateConcurrencyException()
    {
        // given
        var filter = _CreateFilter();
        var exception = new DbUpdateConcurrencyException("Concurrency error");
        var context = _CreateExceptionContext(exception);

        // when
        await filter.OnExceptionAsync(context);

        // then
        context.ExceptionHandled.Should().BeTrue();
        context.HttpContext.Response.StatusCode.Should().Be(StatusCodes.Status409Conflict);

        using var doc = await _GetResponseBody(context);
        var root = doc.RootElement;
        root.GetProperty("status").GetInt32().Should().Be(StatusCodes.Status409Conflict);
        root.GetProperty("title").GetString().Should().Be(HeadlessProblemDetailsConstants.Titles.Conflict);
    }

    [Fact]
    public async Task should_return_408_for_TimeoutException()
    {
        // given
        var filter = _CreateFilter();
        var exception = new TimeoutException("Request timed out");
        var context = _CreateExceptionContext(exception);

        // when
        await filter.OnExceptionAsync(context);

        // then
        context.ExceptionHandled.Should().BeTrue();
        context.HttpContext.Response.StatusCode.Should().Be(StatusCodes.Status408RequestTimeout);

        using var doc = await _GetResponseBody(context);
        var root = doc.RootElement;
        root.GetProperty("status").GetInt32().Should().Be(StatusCodes.Status408RequestTimeout);
    }

    [Fact]
    public async Task should_return_501_for_NotImplementedException()
    {
        // given
        var filter = _CreateFilter();
        var exception = new NotImplementedException("Feature not implemented");
        var context = _CreateExceptionContext(exception);

        // when
        await filter.OnExceptionAsync(context);

        // then
        context.ExceptionHandled.Should().BeTrue();
        context.HttpContext.Response.StatusCode.Should().Be(StatusCodes.Status501NotImplemented);

        using var doc = await _GetResponseBody(context);
        var root = doc.RootElement;
        root.GetProperty("status").GetInt32().Should().Be(StatusCodes.Status501NotImplemented);
    }

    [Fact]
    public async Task should_return_499_for_OperationCanceledException()
    {
        // given
        var filter = _CreateFilter();
        var exception = new OperationCanceledException("Operation was canceled");
        var context = _CreateExceptionContext(exception);

        // when
        await filter.OnExceptionAsync(context);

        // then
        context.ExceptionHandled.Should().BeTrue();
        context.HttpContext.Response.StatusCode.Should().Be(499);

        using var doc = await _GetResponseBody(context);
        var root = doc.RootElement;
        root.GetProperty("status").GetInt32().Should().Be(499);
    }

    [Fact]
    public async Task should_return_499_for_inner_OperationCanceledException()
    {
        // given
        var filter = _CreateFilter();
        var innerException = new OperationCanceledException("Inner operation was canceled");
        var exception = new InvalidOperationException("Outer exception", innerException);
        var context = _CreateExceptionContext(exception);

        // when
        await filter.OnExceptionAsync(context);

        // then
        context.ExceptionHandled.Should().BeTrue();
        context.HttpContext.Response.StatusCode.Should().Be(499);

        using var doc = await _GetResponseBody(context);
        var root = doc.RootElement;
        root.GetProperty("status").GetInt32().Should().Be(499);
    }

    #endregion

    #region Logging Tests

    [Fact]
    public async Task should_log_warning_for_DbUpdateConcurrencyException()
    {
        // given
        var logger = Substitute.For<ILogger<MvcApiExceptionFilter>>();
        var filter = _CreateFilter(logger: logger);
        var exception = new DbUpdateConcurrencyException("Concurrency error");
        var context = _CreateExceptionContext(exception);

        // when
        await filter.OnExceptionAsync(context);

        // then
        logger.Received().Log(
            LogLevel.Warning,
            Arg.Is<EventId>(e => e.Id == 5003),
            Arg.Any<object>(),
            exception,
            Arg.Any<Func<object, Exception?, string>>()
        );
    }

    [Fact]
    public async Task should_log_debug_for_TimeoutException()
    {
        // given
        var logger = Substitute.For<ILogger<MvcApiExceptionFilter>>();
        var filter = _CreateFilter(logger: logger);
        var exception = new TimeoutException("Request timed out");
        var context = _CreateExceptionContext(exception);

        // when
        await filter.OnExceptionAsync(context);

        // then
        logger.Received().Log(
            LogLevel.Debug,
            Arg.Is<EventId>(e => e.Id == 5004),
            Arg.Any<object>(),
            exception,
            Arg.Any<Func<object, Exception?, string>>()
        );
    }

    #endregion

    #region Edge Case Tests

    [Fact]
    public async Task should_set_ExceptionHandled_to_true()
    {
        // given
        var filter = _CreateFilter();
        var exception = new ConflictException("test");
        var context = _CreateExceptionContext(exception);

        // when
        await filter.OnExceptionAsync(context);

        // then
        context.ExceptionHandled.Should().BeTrue();
    }

    [Fact]
    public async Task should_accept_application_json()
    {
        // given
        var filter = _CreateFilter();
        var exception = new ConflictException("test");
        var context = _CreateExceptionContext(exception, "application/json");

        // when
        await filter.OnExceptionAsync(context);

        // then
        context.ExceptionHandled.Should().BeTrue();
        context.HttpContext.Response.StatusCode.Should().Be(StatusCodes.Status409Conflict);
    }

    [Fact]
    public async Task should_accept_problem_json()
    {
        // given
        var filter = _CreateFilter();
        var exception = new ConflictException("test");
        var context = _CreateExceptionContext(exception, "application/problem+json");

        // when
        await filter.OnExceptionAsync(context);

        // then
        context.ExceptionHandled.Should().BeTrue();
        context.HttpContext.Response.StatusCode.Should().Be(StatusCodes.Status409Conflict);
    }

    [Fact]
    public async Task should_not_modify_when_unknown_exception()
    {
        // given
        var filter = _CreateFilter();
        var exception = new InvalidOperationException("Unknown error");
        var context = _CreateExceptionContext(exception);

        // when
        await filter.OnExceptionAsync(context);

        // then
        context.ExceptionHandled.Should().BeFalse();
        context.HttpContext.Response.StatusCode.Should().Be(200); // unchanged default
    }

    #endregion
}
