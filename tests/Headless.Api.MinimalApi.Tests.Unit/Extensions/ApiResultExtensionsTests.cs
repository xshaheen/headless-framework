// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api.Abstractions;
using Headless.Primitives;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Tests.Extensions;

public sealed class ApiResultExtensionsTests : TestBase
{
    #region ApiResult<T> Tests

    [Fact]
    public void should_return_ok_with_value_when_success()
    {
        // given
        var creator = _CreateProblemDetailsCreator();
        var expectedValue = "test-value";
        var result = ApiResult<string>.Ok(expectedValue);

        // when
        var httpResult = result.ToHttpResult(creator);

        // then
        httpResult.Should().BeOfType<Ok<string>>();
        var okResult = (Ok<string>)httpResult;
        okResult.Value.Should().Be(expectedValue);
        okResult.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public void should_return_problem_when_error_for_generic_result()
    {
        // given
        var creator = _CreateProblemDetailsCreator();
        var result = ApiResult<string>.Fail(new ConflictError("test:error", "Test error message"));

        // when
        var httpResult = result.ToHttpResult(creator);

        // then
        httpResult.Should().BeOfType<ProblemHttpResult>();
        var problemResult = (ProblemHttpResult)httpResult;
        problemResult.StatusCode.Should().Be(StatusCodes.Status409Conflict);
    }

    #endregion

    #region ApiResult (void) Tests

    [Fact]
    public void should_return_no_content_when_success()
    {
        // given
        var creator = _CreateProblemDetailsCreator();
        var result = ApiResult.Ok();

        // when
        var httpResult = result.ToHttpResult(creator);

        // then
        httpResult.Should().BeOfType<NoContent>();
        var noContentResult = (NoContent)httpResult;
        noContentResult.StatusCode.Should().Be(StatusCodes.Status204NoContent);
    }

    [Fact]
    public void should_return_problem_when_error_for_void_result()
    {
        // given
        var creator = _CreateProblemDetailsCreator();
        var result = ApiResult.Fail(new ConflictError("test:error", "Test error message"));

        // when
        var httpResult = result.ToHttpResult(creator);

        // then
        httpResult.Should().BeOfType<ProblemHttpResult>();
        var problemResult = (ProblemHttpResult)httpResult;
        problemResult.StatusCode.Should().Be(StatusCodes.Status409Conflict);
    }

    #endregion

    #region ResultError Mapping Tests

    [Fact]
    public void should_map_NotFoundError_to_404()
    {
        // given
        var creator = _CreateProblemDetailsCreator();
        var error = new NotFoundError { Entity = "User", Key = "123" };

        // when
        var httpResult = error.ToHttpResult(creator);

        // then
        httpResult.Should().BeOfType<ProblemHttpResult>();
        var problemResult = (ProblemHttpResult)httpResult;
        problemResult.StatusCode.Should().Be(StatusCodes.Status404NotFound);
    }

    [Fact]
    public void should_map_ValidationError_to_422()
    {
        // given
        var creator = _CreateProblemDetailsCreator();
        var error = ValidationError.FromFields(("Name", "Name is required"));

        // when
        var httpResult = error.ToHttpResult(creator);

        // then
        httpResult.Should().BeOfType<ProblemHttpResult>();
        var problemResult = (ProblemHttpResult)httpResult;
        problemResult.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity);
    }

    [Fact]
    public void should_map_ForbiddenError_to_403()
    {
        // given
        var creator = _CreateProblemDetailsCreator();
        var error = new ForbiddenError { Reason = "Access denied" };

        // when
        var httpResult = error.ToHttpResult(creator);

        // then
        httpResult.Should().BeOfType<ProblemHttpResult>();
        var problemResult = (ProblemHttpResult)httpResult;
        problemResult.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public void should_map_UnauthorizedError_to_401()
    {
        // given
        var creator = _CreateProblemDetailsCreator();
        var error = UnauthorizedError.Instance;

        // when
        var httpResult = error.ToHttpResult(creator);

        // then
        httpResult.Should().BeOfType<ProblemHttpResult>();
        var problemResult = (ProblemHttpResult)httpResult;
        problemResult.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public void should_map_AggregateError_to_409()
    {
        // given
        var creator = _CreateProblemDetailsCreator();
        var error = new AggregateError
        {
            Errors =
            [
                new ConflictError("error1", "First error"),
                new ConflictError("error2", "Second error"),
            ],
        };

        // when
        var httpResult = error.ToHttpResult(creator);

        // then
        httpResult.Should().BeOfType<ProblemHttpResult>();
        var problemResult = (ProblemHttpResult)httpResult;
        problemResult.StatusCode.Should().Be(StatusCodes.Status409Conflict);
    }

    [Fact]
    public void should_map_ConflictError_to_409()
    {
        // given
        var creator = _CreateProblemDetailsCreator();
        var error = new ConflictError("duplicate:email", "Email already exists");

        // when
        var httpResult = error.ToHttpResult(creator);

        // then
        httpResult.Should().BeOfType<ProblemHttpResult>();
        var problemResult = (ProblemHttpResult)httpResult;
        problemResult.StatusCode.Should().Be(StatusCodes.Status409Conflict);
    }

    [Fact]
    public void should_map_unknown_error_to_409()
    {
        // given
        var creator = _CreateProblemDetailsCreator();
        var error = ResultError.Custom("custom:error", "Custom error message");

        // when
        var httpResult = error.ToHttpResult(creator);

        // then
        httpResult.Should().BeOfType<ProblemHttpResult>();
        var problemResult = (ProblemHttpResult)httpResult;
        problemResult.StatusCode.Should().Be(StatusCodes.Status409Conflict);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void should_pass_entity_and_key_to_creator_for_NotFoundError()
    {
        // given
        var creator = _CreateProblemDetailsCreator();
        var error = new NotFoundError { Entity = "Order", Key = "ORD-456" };

        // when
        _ = error.ToHttpResult(creator);

        // then
        creator.Received(1).EntityNotFound("Order", "ORD-456");
    }

    [Fact]
    public void should_pass_validation_errors_dictionary_to_creator()
    {
        // given
        var creator = _CreateProblemDetailsCreator();
        var error = ValidationError.FromFields(
            ("Email", "Email is required"),
            ("Email", "Email format is invalid"),
            ("Name", "Name is required")
        );

        // when
        _ = error.ToHttpResult(creator);

        // then
        creator.Received(1).UnprocessableEntity(Arg.Is<Dictionary<string, List<ErrorDescriptor>>>(d =>
            d.ContainsKey("Email") && d["Email"].Count == 2 &&
            d.ContainsKey("Name") && d["Name"].Count == 1
        ));
    }

    [Fact]
    public void should_pass_all_aggregate_errors_to_creator()
    {
        // given
        var creator = _CreateProblemDetailsCreator();
        var error = new AggregateError
        {
            Errors =
            [
                new ConflictError("error1", "First error"),
                new ConflictError("error2", "Second error"),
                new ConflictError("error3", "Third error"),
            ],
        };

        // when
        _ = error.ToHttpResult(creator);

        // then
        creator.Received(1).Conflict(Arg.Is<IEnumerable<ErrorDescriptor>>(errors =>
            errors.Count() == 3 &&
            errors.Any(e => e.Code == "error1" && e.Description == "First error") &&
            errors.Any(e => e.Code == "error2" && e.Description == "Second error") &&
            errors.Any(e => e.Code == "error3" && e.Description == "Third error")
        ));
    }

    #endregion

    #region Helper Methods

    private static IProblemDetailsCreator _CreateProblemDetailsCreator()
    {
        var creator = Substitute.For<IProblemDetailsCreator>();

        creator.EntityNotFound(Arg.Any<string>(), Arg.Any<string>()).Returns(ci => new ProblemDetails
        {
            Status = StatusCodes.Status404NotFound,
            Title = "Entity Not Found",
        });

        creator.UnprocessableEntity(Arg.Any<Dictionary<string, List<ErrorDescriptor>>>()).Returns(ci => new ProblemDetails
        {
            Status = StatusCodes.Status422UnprocessableEntity,
            Title = "Unprocessable Entity",
        });

        creator.Forbidden(Arg.Any<IReadOnlyCollection<ErrorDescriptor>>()).Returns(ci => new ProblemDetails
        {
            Status = StatusCodes.Status403Forbidden,
            Title = "Forbidden",
        });

        creator.Unauthorized().Returns(new ProblemDetails
        {
            Status = StatusCodes.Status401Unauthorized,
            Title = "Unauthorized",
        });

        creator.Conflict(Arg.Any<IEnumerable<ErrorDescriptor>>()).Returns(ci => new ProblemDetails
        {
            Status = StatusCodes.Status409Conflict,
            Title = "Conflict",
        });

        return creator;
    }

    #endregion
}
