// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation.Results;
using ValidationException = FluentValidation.ValidationException;
using Headless.Api.Abstractions;
using Headless.Exceptions;
using Headless.Primitives;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Tests.Filters;

public sealed class MinimalApiExceptionFilterTests : TestBase
{
    #region Happy Path

    [Fact]
    public async Task should_pass_through_when_no_exception()
    {
        // given
        var filter = _CreateFilter();
        var context = _CreateContext();
        var expectedResult = new object();
        var next = _CreateNext(expectedResult);

        // when
        var result = await filter.InvokeAsync(context, next);

        // then
        result.Should().BeSameAs(expectedResult);
    }

    [Fact]
    public async Task should_pass_through_when_not_accepting_json()
    {
        // given
        var filter = _CreateFilter();
        var context = _CreateContext(acceptHeader: "text/html");
        var expectedResult = new object();
        var next = _CreateNext(expectedResult);

        // when
        var result = await filter.InvokeAsync(context, next);

        // then
        result.Should().BeSameAs(expectedResult);
    }

    #endregion

    #region Exception Mapping

    [Fact]
    public async Task should_return_409_for_ConflictException()
    {
        // given
        var errors = new List<ErrorDescriptor> { new("code", "description") };
        var exception = new ConflictException(errors);
        var expectedProblemDetails = new ProblemDetails { Status = StatusCodes.Status409Conflict };
        var problemDetailsCreator = _CreateProblemDetailsCreator();
        problemDetailsCreator.Conflict(Arg.Any<IEnumerable<ErrorDescriptor>>()).Returns(expectedProblemDetails);
        var filter = _CreateFilter(problemDetailsCreator);
        var context = _CreateContext();
        var next = _CreateThrowingNext(exception);

        // when
        var result = await filter.InvokeAsync(context, next);

        // then
        result.Should().BeOfType<ProblemHttpResult>();
        var problemResult = (ProblemHttpResult)result!;
        problemResult.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        problemDetailsCreator.Received(1).Conflict(Arg.Is<IEnumerable<ErrorDescriptor>>(e => e.SequenceEqual(errors)));
    }

    [Fact]
    public async Task should_return_422_for_ValidationException()
    {
        // given
        var failures = new List<ValidationFailure> { new("Property", "Error message") { ErrorCode = "code" } };
        var exception = new ValidationException(failures);
        var expectedProblemDetails = new ProblemDetails { Status = StatusCodes.Status422UnprocessableEntity };
        var problemDetailsCreator = _CreateProblemDetailsCreator();
        problemDetailsCreator.UnprocessableEntity(Arg.Any<Dictionary<string, List<ErrorDescriptor>>>()).Returns(expectedProblemDetails);
        var filter = _CreateFilter(problemDetailsCreator);
        var context = _CreateContext();
        var next = _CreateThrowingNext(exception);

        // when
        var result = await filter.InvokeAsync(context, next);

        // then
        result.Should().BeOfType<ProblemHttpResult>();
        var problemResult = (ProblemHttpResult)result!;
        problemResult.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity);
        problemDetailsCreator.Received(1).UnprocessableEntity(Arg.Any<Dictionary<string, List<ErrorDescriptor>>>());
    }

    [Fact]
    public async Task should_return_404_for_EntityNotFoundException()
    {
        // given
        var exception = new EntityNotFoundException("User", "123");
        var expectedProblemDetails = new ProblemDetails { Status = StatusCodes.Status404NotFound };
        var problemDetailsCreator = _CreateProblemDetailsCreator();
        problemDetailsCreator.EntityNotFound(Arg.Any<string>(), Arg.Any<string>()).Returns(expectedProblemDetails);
        var filter = _CreateFilter(problemDetailsCreator);
        var context = _CreateContext();
        var next = _CreateThrowingNext(exception);

        // when
        var result = await filter.InvokeAsync(context, next);

        // then
        result.Should().BeOfType<ProblemHttpResult>();
        var problemResult = (ProblemHttpResult)result!;
        problemResult.StatusCode.Should().Be(StatusCodes.Status404NotFound);
        problemDetailsCreator.Received(1).EntityNotFound("User", "123");
    }

    [Fact]
    public async Task should_return_409_for_DbUpdateConcurrencyException()
    {
        // given
        var exception = new DbUpdateConcurrencyException("Concurrency conflict");
        var expectedProblemDetails = new ProblemDetails { Status = StatusCodes.Status409Conflict };
        var problemDetailsCreator = _CreateProblemDetailsCreator();
        problemDetailsCreator.Conflict(Arg.Any<IEnumerable<ErrorDescriptor>>()).Returns(expectedProblemDetails);
        var filter = _CreateFilter(problemDetailsCreator);
        var context = _CreateContext();
        var next = _CreateThrowingNext(exception);

        // when
        var result = await filter.InvokeAsync(context, next);

        // then
        result.Should().BeOfType<ProblemHttpResult>();
        var problemResult = (ProblemHttpResult)result!;
        problemResult.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        problemDetailsCreator.Received(1).Conflict(Arg.Any<IEnumerable<ErrorDescriptor>>());
    }

    [Fact]
    public async Task should_return_408_for_TimeoutException()
    {
        // given
        var exception = new TimeoutException("Request timed out");
        var filter = _CreateFilter();
        var context = _CreateContext();
        var next = _CreateThrowingNext(exception);

        // when
        var result = await filter.InvokeAsync(context, next);

        // then
        result.Should().BeOfType<ProblemHttpResult>();
        var problemResult = (ProblemHttpResult)result!;
        problemResult.StatusCode.Should().Be(StatusCodes.Status408RequestTimeout);
    }

    [Fact]
    public async Task should_return_501_for_NotImplementedException()
    {
        // given
        var exception = new NotImplementedException("Not implemented");
        var filter = _CreateFilter();
        var context = _CreateContext();
        var next = _CreateThrowingNext(exception);

        // when
        var result = await filter.InvokeAsync(context, next);

        // then
        result.Should().BeOfType<ProblemHttpResult>();
        var problemResult = (ProblemHttpResult)result!;
        problemResult.StatusCode.Should().Be(StatusCodes.Status501NotImplemented);
    }

    [Fact]
    public async Task should_return_499_for_OperationCanceledException()
    {
        // given
        var exception = new OperationCanceledException("Request cancelled");
        var filter = _CreateFilter();
        var context = _CreateContext();
        var next = _CreateThrowingNext(exception);

        // when
        var result = await filter.InvokeAsync(context, next);

        // then
        result.Should().BeOfType<StatusCodeHttpResult>();
        var statusCodeResult = (StatusCodeHttpResult)result!;
        statusCodeResult.StatusCode.Should().Be(StatusCodes.Status499ClientClosedRequest);
    }

    [Fact]
    public async Task should_return_499_for_inner_OperationCanceledException()
    {
        // given
        var innerException = new OperationCanceledException("Inner cancellation");
        var exception = new InvalidOperationException("Outer exception", innerException);
        var filter = _CreateFilter();
        var context = _CreateContext();
        var next = _CreateThrowingNext(exception);

        // when
        var result = await filter.InvokeAsync(context, next);

        // then
        result.Should().BeOfType<StatusCodeHttpResult>();
        var statusCodeResult = (StatusCodeHttpResult)result!;
        statusCodeResult.StatusCode.Should().Be(StatusCodes.Status499ClientClosedRequest);
    }

    #endregion

    #region Logging

    [Fact]
    public async Task should_log_warning_for_DbUpdateConcurrencyException()
    {
        // given
        var exception = new DbUpdateConcurrencyException("Concurrency conflict");
        var logger = Substitute.For<ILogger<MinimalApiExceptionFilter>>();
        var filter = _CreateFilter(logger: logger);
        var context = _CreateContext();
        var next = _CreateThrowingNext(exception);

        // when
        _ = await filter.InvokeAsync(context, next);

        // then
        logger.Received(1).Log(
            LogLevel.Warning,
            Arg.Is<EventId>(e => e.Id == 5003 && e.Name == "DbConcurrencyException"),
            Arg.Any<object>(),
            exception,
            Arg.Any<Func<object, Exception?, string>>()
        );
    }

    [Fact]
    public async Task should_log_debug_for_TimeoutException()
    {
        // given
        var exception = new TimeoutException("Request timed out");
        var logger = Substitute.For<ILogger<MinimalApiExceptionFilter>>();
        var filter = _CreateFilter(logger: logger);
        var context = _CreateContext();
        var next = _CreateThrowingNext(exception);

        // when
        _ = await filter.InvokeAsync(context, next);

        // then
        logger.Received(1).Log(
            LogLevel.Debug,
            Arg.Is<EventId>(e => e.Id == 5004 && e.Name == "RequestTimeoutException"),
            Arg.Any<object>(),
            exception,
            Arg.Any<Func<object, Exception?, string>>()
        );
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task should_accept_application_json()
    {
        // given
        var filter = _CreateFilter();
        var context = _CreateContext(acceptHeader: "application/json");
        var exception = new TimeoutException();
        var next = _CreateThrowingNext(exception);

        // when
        var result = await filter.InvokeAsync(context, next);

        // then
        result.Should().BeOfType<ProblemHttpResult>();
        var problemResult = (ProblemHttpResult)result!;
        problemResult.StatusCode.Should().Be(StatusCodes.Status408RequestTimeout);
    }

    [Fact]
    public async Task should_accept_problem_json()
    {
        // given
        var filter = _CreateFilter();
        var context = _CreateContext(acceptHeader: "application/problem+json");
        var exception = new TimeoutException();
        var next = _CreateThrowingNext(exception);

        // when
        var result = await filter.InvokeAsync(context, next);

        // then
        result.Should().BeOfType<ProblemHttpResult>();
        var problemResult = (ProblemHttpResult)result!;
        problemResult.StatusCode.Should().Be(StatusCodes.Status408RequestTimeout);
    }

    [Fact]
    public async Task should_not_catch_unknown_exception()
    {
        // given
        var exception = new InvalidOperationException("Unknown error");
        var filter = _CreateFilter();
        var context = _CreateContext();
        var next = _CreateThrowingNext(exception);

        // when
        var act = () => filter.InvokeAsync(context, next).AsTask();

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Unknown error");
    }

    [Fact]
    public async Task should_handle_exception_when_no_accept_header()
    {
        // given - no Accept header defaults to accepting all types
        var filter = _CreateFilter();
        var context = _CreateContextWithoutAcceptHeader();
        var exception = new TimeoutException();
        var next = _CreateThrowingNext(exception);

        // when
        var result = await filter.InvokeAsync(context, next);

        // then
        result.Should().BeOfType<ProblemHttpResult>();
        var problemResult = (ProblemHttpResult)result!;
        problemResult.StatusCode.Should().Be(StatusCodes.Status408RequestTimeout);
    }

    #endregion

    #region Helpers

    private static MinimalApiExceptionFilter _CreateFilter(
        IProblemDetailsCreator? problemDetailsCreator = null,
        ILogger<MinimalApiExceptionFilter>? logger = null)
    {
        problemDetailsCreator ??= _CreateProblemDetailsCreator();
        logger ??= Substitute.For<ILogger<MinimalApiExceptionFilter>>();
        return new MinimalApiExceptionFilter(problemDetailsCreator, logger);
    }

    private static IProblemDetailsCreator _CreateProblemDetailsCreator()
    {
        var creator = Substitute.For<IProblemDetailsCreator>();
        creator.Conflict(Arg.Any<IEnumerable<ErrorDescriptor>>()).Returns(new ProblemDetails { Status = StatusCodes.Status409Conflict });
        creator.UnprocessableEntity(Arg.Any<Dictionary<string, List<ErrorDescriptor>>>()).Returns(new ProblemDetails { Status = StatusCodes.Status422UnprocessableEntity });
        creator.EntityNotFound(Arg.Any<string>(), Arg.Any<string>()).Returns(new ProblemDetails { Status = StatusCodes.Status404NotFound });
        return creator;
    }

    private static EndpointFilterInvocationContext _CreateContext(string acceptHeader = "application/json")
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Accept = acceptHeader;

        var context = Substitute.For<EndpointFilterInvocationContext>();
        context.HttpContext.Returns(httpContext);
        return context;
    }

    private static EndpointFilterInvocationContext _CreateContextWithoutAcceptHeader()
    {
        var httpContext = new DefaultHttpContext();
        // Don't set Accept header at all - Count will be 0

        var context = Substitute.For<EndpointFilterInvocationContext>();
        context.HttpContext.Returns(httpContext);
        return context;
    }

    private static EndpointFilterDelegate _CreateNext(object? result = null)
    {
        return _ => ValueTask.FromResult(result);
    }

    private static EndpointFilterDelegate _CreateThrowingNext<TException>(TException exception)
        where TException : Exception
    {
        return _ => throw exception;
    }

    #endregion
}

/// <summary>
/// Test exception class named to match EF Core's DbUpdateConcurrencyException
/// for duck-typing detection in MinimalApiExceptionFilter.
/// </summary>
public sealed class DbUpdateConcurrencyException : Exception
{
    public DbUpdateConcurrencyException() { }

    public DbUpdateConcurrencyException(string message) : base(message) { }

    public DbUpdateConcurrencyException(string message, Exception inner) : base(message, inner) { }
}
