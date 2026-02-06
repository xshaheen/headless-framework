// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api.Abstractions;
using Headless.Primitives;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Tests.Extensions;

public sealed class ApiResultMvcExtensionsTests : TestBase
{
    #region ApiResult<T> Tests

    [Fact]
    public void should_return_ok_with_value_when_success()
    {
        // given
        var controller = _CreateController();
        var creator = _CreateProblemDetailsCreator();
        var expectedValue = "test-value";
        var result = ApiResult<string>.Ok(expectedValue);

        // when
        var actionResult = result.ToActionResult(controller, creator);

        // then
        actionResult.Result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)actionResult.Result!;
        okResult.Value.Should().Be(expectedValue);
        okResult.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public void should_return_problem_when_error_for_generic_result()
    {
        // given
        var controller = _CreateController();
        var creator = _CreateProblemDetailsCreator();
        var result = ApiResult<string>.Fail(new ConflictError("test:error", "Test error message"));

        // when
        var actionResult = result.ToActionResult(controller, creator);

        // then
        actionResult.Result.Should().BeOfType<ConflictObjectResult>();
        var conflictResult = (ConflictObjectResult)actionResult.Result!;
        conflictResult.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        conflictResult.Value.Should().BeOfType<ProblemDetails>();
    }

    #endregion

    #region ApiResult (void) Tests

    [Fact]
    public void should_return_NoContent_when_success()
    {
        // given
        var controller = _CreateController();
        var creator = _CreateProblemDetailsCreator();
        var result = ApiResult.Ok();

        // when
        var actionResult = result.ToActionResult(controller, creator);

        // then
        actionResult.Should().BeOfType<NoContentResult>();
        var noContentResult = (NoContentResult)actionResult;
        noContentResult.StatusCode.Should().Be(StatusCodes.Status204NoContent);
    }

    [Fact]
    public void should_return_problem_when_error_for_void_result()
    {
        // given
        var controller = _CreateController();
        var creator = _CreateProblemDetailsCreator();
        var result = ApiResult.Fail(new ConflictError("test:error", "Test error message"));

        // when
        var actionResult = result.ToActionResult(controller, creator);

        // then
        actionResult.Should().BeOfType<ConflictObjectResult>();
        var conflictResult = (ConflictObjectResult)actionResult;
        conflictResult.StatusCode.Should().Be(StatusCodes.Status409Conflict);
    }

    #endregion

    #region ResultError Mapping Tests

    [Fact]
    public void should_map_NotFoundError_to_404()
    {
        // given
        var controller = _CreateController();
        var creator = _CreateProblemDetailsCreator();
        var error = new NotFoundError { Entity = "User", Key = "123" };

        // when
        var actionResult = error.ToActionResult(controller, creator);

        // then
        actionResult.Should().BeOfType<NotFoundObjectResult>();
        var notFoundResult = (NotFoundObjectResult)actionResult;
        notFoundResult.StatusCode.Should().Be(StatusCodes.Status404NotFound);
        notFoundResult.Value.Should().BeOfType<ProblemDetails>();
        var problemDetails = (ProblemDetails)notFoundResult.Value!;
        problemDetails.Status.Should().Be(StatusCodes.Status404NotFound);
    }

    [Fact]
    public void should_map_ValidationError_to_422()
    {
        // given
        var controller = _CreateController();
        var creator = _CreateProblemDetailsCreator();
        var error = ValidationError.FromFields(("Name", "Name is required"));

        // when
        var actionResult = error.ToActionResult(controller, creator);

        // then
        actionResult.Should().BeOfType<UnprocessableEntityObjectResult>();
        var unprocessableResult = (UnprocessableEntityObjectResult)actionResult;
        unprocessableResult.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity);
        unprocessableResult.Value.Should().BeOfType<ProblemDetails>();
        var problemDetails = (ProblemDetails)unprocessableResult.Value!;
        problemDetails.Status.Should().Be(StatusCodes.Status422UnprocessableEntity);
    }

    [Fact]
    public void should_map_ForbiddenError_to_403()
    {
        // given
        var controller = _CreateController();
        var creator = _CreateProblemDetailsCreator();
        var error = new ForbiddenError { Reason = "Access denied" };

        // when
        var actionResult = error.ToActionResult(controller, creator);

        // then
        actionResult.Should().BeOfType<ObjectResult>();
        var objectResult = (ObjectResult)actionResult;
        objectResult.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        objectResult.Value.Should().BeOfType<ProblemDetails>();
        var problemDetails = (ProblemDetails)objectResult.Value!;
        problemDetails.Status.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public void should_map_UnauthorizedError_to_401()
    {
        // given
        var controller = _CreateController();
        var creator = _CreateProblemDetailsCreator();
        var error = UnauthorizedError.Instance;

        // when
        var actionResult = error.ToActionResult(controller, creator);

        // then
        actionResult.Should().BeOfType<UnauthorizedObjectResult>();
        var unauthorizedResult = (UnauthorizedObjectResult)actionResult;
        unauthorizedResult.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        unauthorizedResult.Value.Should().BeOfType<ProblemDetails>();
        var problemDetails = (ProblemDetails)unauthorizedResult.Value!;
        problemDetails.Status.Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public void should_map_AggregateError_to_409()
    {
        // given
        var controller = _CreateController();
        var creator = _CreateProblemDetailsCreator();
        var error = new AggregateError
        {
            Errors = [new ConflictError("error1", "First error"), new ConflictError("error2", "Second error")],
        };

        // when
        var actionResult = error.ToActionResult(controller, creator);

        // then
        actionResult.Should().BeOfType<ConflictObjectResult>();
        var conflictResult = (ConflictObjectResult)actionResult;
        conflictResult.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        conflictResult.Value.Should().BeOfType<ProblemDetails>();
        var problemDetails = (ProblemDetails)conflictResult.Value!;
        problemDetails.Status.Should().Be(StatusCodes.Status409Conflict);
    }

    [Fact]
    public void should_map_ConflictError_to_409()
    {
        // given
        var controller = _CreateController();
        var creator = _CreateProblemDetailsCreator();
        var error = new ConflictError("duplicate:email", "Email already exists");

        // when
        var actionResult = error.ToActionResult(controller, creator);

        // then
        actionResult.Should().BeOfType<ConflictObjectResult>();
        var conflictResult = (ConflictObjectResult)actionResult;
        conflictResult.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        conflictResult.Value.Should().BeOfType<ProblemDetails>();
        var problemDetails = (ProblemDetails)conflictResult.Value!;
        problemDetails.Status.Should().Be(StatusCodes.Status409Conflict);
    }

    [Fact]
    public void should_map_unknown_error_to_409()
    {
        // given
        var controller = _CreateController();
        var creator = _CreateProblemDetailsCreator();
        var error = ResultError.Custom("custom:error", "Custom error message");

        // when
        var actionResult = error.ToActionResult(controller, creator);

        // then
        actionResult.Should().BeOfType<ConflictObjectResult>();
        var conflictResult = (ConflictObjectResult)actionResult;
        conflictResult.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        conflictResult.Value.Should().BeOfType<ProblemDetails>();
    }

    #endregion

    #region Helper Methods

    private static IProblemDetailsCreator _CreateProblemDetailsCreator()
    {
        var creator = Substitute.For<IProblemDetailsCreator>();

        creator
            .EntityNotFound(Arg.Any<string>(), Arg.Any<string>())
            .Returns(ci => new ProblemDetails { Status = StatusCodes.Status404NotFound, Title = "Entity Not Found" });

        creator
            .UnprocessableEntity(Arg.Any<Dictionary<string, List<ErrorDescriptor>>>())
            .Returns(ci => new ProblemDetails
            {
                Status = StatusCodes.Status422UnprocessableEntity,
                Title = "Unprocessable Entity",
            });

        creator
            .Forbidden(Arg.Any<IReadOnlyCollection<ErrorDescriptor>>())
            .Returns(ci => new ProblemDetails { Status = StatusCodes.Status403Forbidden, Title = "Forbidden" });

        creator
            .Unauthorized()
            .Returns(new ProblemDetails { Status = StatusCodes.Status401Unauthorized, Title = "Unauthorized" });

        creator
            .Conflict(Arg.Any<IEnumerable<ErrorDescriptor>>())
            .Returns(ci => new ProblemDetails { Status = StatusCodes.Status409Conflict, Title = "Conflict" });

        return creator;
    }

    private static TestController _CreateController()
    {
        var controller = new TestController
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        return controller;
    }

    private sealed class TestController : ControllerBase;

    #endregion
}
