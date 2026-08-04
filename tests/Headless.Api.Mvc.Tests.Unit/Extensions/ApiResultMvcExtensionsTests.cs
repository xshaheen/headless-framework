// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
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
        const string expectedValue = "test-value";
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
    public void should_return_no_content_when_success()
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

    #region ApiResultError Mapping Tests

    [Fact]
    public void should_map_not_found_error_to_404()
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
    public void should_map_validation_error_to_422()
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
    public void should_map_forbidden_error_to_403()
    {
        // given
        var controller = _CreateController();
        var creator = _CreateProblemDetailsCreator();
        var error = new ForbiddenError(new ErrorDescriptor(ApiResultErrorCodes.Forbidden, "Access denied"));

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
    public void should_map_unauthorized_error_to_401()
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
    public void should_preserve_unauthorized_descriptor_when_mapping_to_401()
    {
        // given
        var controller = _CreateController();
        var creator = _CreateProblemDetailsCreator();
        var descriptor = new ErrorDescriptor("auth:expired", "Session expired");

        // when
        _ = new UnauthorizedError(descriptor).ToActionResult(controller, creator);

        // then
        creator.Received(1).Unauthorized(descriptor);
    }

    [Fact]
    public void should_map_aggregate_error_to_409()
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
    public void should_map_conflict_error_to_409()
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
    public void should_preserve_all_conflict_descriptors_when_mapping_to_409()
    {
        // given
        var controller = _CreateController();
        var creator = _CreateProblemDetailsCreator();
        var errors = new[]
        {
            new ErrorDescriptor("user:duplicate_email", "Email already exists").WithParam("email", "a@b.com"),
            new ErrorDescriptor("user:duplicate_phone", "Phone already exists"),
        };

        // when
        _ = new ConflictError(errors).ToActionResult(controller, creator);

        // then
        creator
            .Received(1)
            .Conflict(Arg.Is<IReadOnlyCollection<ErrorDescriptor>>(actual => actual.SequenceEqual(errors)));
    }

    [Fact]
    public void should_map_aggregate_of_validation_errors_to_422()
    {
        // given
        var controller = _CreateController();
        var creator = _CreateProblemDetailsCreator();
        var error = new AggregateError
        {
            Errors =
            [
                ValidationError.FromFields(("email", "Email is required")),
                ValidationError.FromFields(("name", "Name is required")),
            ],
        };

        // when
        var actionResult = error.ToActionResult(controller, creator);

        // then
        actionResult.Should().BeOfType<UnprocessableEntityObjectResult>();
        creator
            .Received(1)
            .UnprocessableEntity(
                Arg.Is<IReadOnlyDictionary<string, IReadOnlyList<ErrorDescriptor>>>(actual =>
                    actual.Count == 2 && actual.ContainsKey("email") && actual.ContainsKey("name")
                )
            );
    }

    [Fact]
    public void should_map_nested_validation_aggregate_to_422()
    {
        // given
        var controller = _CreateController();
        var creator = _CreateProblemDetailsCreator();
        var error = new AggregateError
        {
            Errors =
            [
                new AggregateError { Errors = [ValidationError.FromFields(("email", "Email is required"))] },
                ValidationError.FromFields(("name", "Name is required")),
            ],
        };

        // when
        var actionResult = error.ToActionResult(controller, creator);

        // then
        actionResult.Should().BeOfType<UnprocessableEntityObjectResult>();
        creator
            .Received(1)
            .UnprocessableEntity(
                Arg.Is<IReadOnlyDictionary<string, IReadOnlyList<ErrorDescriptor>>>(actual =>
                    actual.ContainsKey("email") && actual.ContainsKey("name")
                )
            );
    }

    [Fact]
    public void should_map_mixed_nested_aggregate_to_409()
    {
        // given
        var controller = _CreateController();
        var creator = _CreateProblemDetailsCreator();
        var error = new AggregateError
        {
            Errors =
            [
                new AggregateError { Errors = [ValidationError.FromFields(("email", "Email is required"))] },
                new ConflictError("user:duplicate", "User already exists"),
            ],
        };

        // when
        var actionResult = error.ToActionResult(controller, creator);

        // then
        actionResult.Should().BeOfType<ConflictObjectResult>();
        creator.Received(1).Conflict(Arg.Is<IReadOnlyCollection<ErrorDescriptor>>(actual => actual.Count == 2));
    }

    [Fact]
    public void should_throw_clear_error_when_mapping_default_result()
    {
        // given
        var controller = _CreateController();
        var creator = _CreateProblemDetailsCreator();

        // when
        var action = () => default(ApiResult).ToActionResult(controller, creator);

        // then
        action.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void should_throw_clear_error_when_mapping_default_generic_result()
    {
        // given
        var controller = _CreateController();
        var creator = _CreateProblemDetailsCreator();

        // when
        var action = () => default(ApiResult<string>).ToActionResult(controller, creator);

        // then
        action.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void should_map_unknown_error_to_409()
    {
        // given
        var controller = _CreateController();
        var creator = _CreateProblemDetailsCreator();
        var error = ApiResultError.Custom("custom:error", "Custom error message");

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
            .EntityNotFound()
            .Returns(new ProblemDetails { Status = StatusCodes.Status404NotFound, Title = "Entity Not Found" });

        creator
            .UnprocessableEntity(Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<ErrorDescriptor>>>())
            .Returns(ci => new ProblemDetails
            {
                Status = StatusCodes.Status422UnprocessableEntity,
                Title = "Unprocessable Entity",
            });

        creator
            .Forbidden(error: Arg.Any<ErrorDescriptor>())
            .Returns(ci => new ProblemDetails { Status = StatusCodes.Status403Forbidden, Title = "Forbidden" });

        creator
            .Unauthorized(Arg.Any<ErrorDescriptor?>())
            .Returns(new ProblemDetails { Status = StatusCodes.Status401Unauthorized, Title = "Unauthorized" });

        creator
            .Conflict(Arg.Any<IReadOnlyCollection<ErrorDescriptor>>())
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
