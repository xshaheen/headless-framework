// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Headless.Api;
using Headless.Context;
using Headless.Primitives;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Extensions;

public sealed class ApiResultMvcExtensionsTests : TestBase
{
    // PopulateMetadata ignores the method; any method stands in for the action.
    private static readonly MethodInfo _ActionMethod = ((Func<ApiActionResult?>)_Action).Method;

    #region OpenAPI metadata

    [Fact]
    public void should_publish_only_the_success_response_for_valued_results()
    {
        // given
        var builder = new RouteEndpointBuilder(_ => Task.CompletedTask, RoutePatternFactory.Parse("/"), order: 0);

        // when
        ApiActionResult<string>.PopulateMetadata(_ActionMethod, builder);

        // then
        var responses = builder.Metadata.OfType<IProducesResponseTypeMetadata>().ToList();
        responses.Select(response => response.StatusCode).Should().Equal([200]);
        var success = responses.Single(response => response.StatusCode == 200);
        success.Type.Should().Be<string>();
        success.ContentTypes.Should().Equal("application/json");
    }

    [Fact]
    public void should_publish_a_bodyless_204_for_unit_results()
    {
        // given
        var builder = new RouteEndpointBuilder(_ => Task.CompletedTask, RoutePatternFactory.Parse("/"), order: 0);

        // when
        ApiActionResult.PopulateMetadata(_ActionMethod, builder);

        // then
        var responses = builder.Metadata.OfType<IProducesResponseTypeMetadata>().ToList();
        responses.Select(response => response.StatusCode).Should().Equal([204]);
        var success = responses.Single(response => response.StatusCode == 204);
        success.Type.Should().Be(typeof(void));
        success.ContentTypes.Should().BeEmpty();
    }

    #endregion

    #region Error description localization

    [Fact]
    public void should_apply_error_description_localizer_when_converting_errors()
    {
        // given
        var controller = _CreateController();
        var creator = _CreateLocalizingCreator(new OutOfStockLocalizer());
        var result = ApiResult<string>.Fail(new ConflictError("app:out_of_stock", "Out of stock."));

        // when
        var actionResult = result.ToActionResult(controller, creator);

        // then
        var body = actionResult
            .Result.Should()
            .BeOfType<ConflictObjectResult>()
            .Which.Value.Should()
            .BeOfType<ProblemDetails>()
            .Subject;
        body.Extensions["errors"]
            .Should()
            .BeAssignableTo<IReadOnlyCollection<ErrorDescriptor>>()
            .Which.Should()
            .ContainSingle()
            .Which.Description.Should()
            .Be("نفدت الكمية");
    }

    #endregion

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
        var okResult = actionResult.Result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.Value.Should().Be(expectedValue);
        okResult.StatusCode.Should().Be(StatusCodes.Status200OK);
        ((IConvertToActionResult)actionResult).Convert().Should().BeSameAs(okResult);
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
        var noContentResult = actionResult.Result.Should().BeOfType<NoContentResult>().Subject;
        noContentResult.StatusCode.Should().Be(StatusCodes.Status204NoContent);
        ((IConvertToActionResult)actionResult).Convert().Should().BeSameAs(noContentResult);
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
        var conflictResult = actionResult.Result.Should().BeOfType<ConflictObjectResult>().Subject;
        conflictResult.StatusCode.Should().Be(StatusCodes.Status409Conflict);
    }

    #endregion

    #region Created and Accepted

    [Fact]
    public void should_return_created_with_location_and_value_when_success()
    {
        // given
        var controller = _CreateController();
        var result = ApiResult<string>.Ok("a1");

        // when
        var actionResult = result.ToCreatedActionResult(
            controller,
            _CreateProblemDetailsCreator(),
            id => $"/items/{id}"
        );

        // then
        var created = actionResult.Result.Should().BeOfType<CreatedResult>().Subject;
        created.StatusCode.Should().Be(StatusCodes.Status201Created);
        created.Location.Should().Be("/items/a1");
        created.Value.Should().Be("a1");
        ((IConvertToActionResult)actionResult).Convert().Should().BeSameAs(created);
    }

    [Fact]
    public void should_not_build_a_location_when_created_fails()
    {
        // given
        var controller = _CreateController();
        var result = ApiResult<string>.Fail(new NotFoundError { Entity = "Item", Key = "a1" });
        var locationCalls = 0;

        // when
        var actionResult = result.ToCreatedActionResult(
            controller,
            _CreateProblemDetailsCreator(),
            id =>
            {
                locationCalls++;
                return $"/items/{id}";
            }
        );

        // then
        locationCalls.Should().Be(0);
        actionResult.Result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public void should_return_accepted_with_the_value_and_no_location_when_success()
    {
        // given - a string value must stay the body, not become the Location
        var controller = _CreateController();
        var result = ApiResult<string>.Ok("job-1");

        // when
        var actionResult = result.ToAcceptedActionResult(controller, _CreateProblemDetailsCreator());

        // then
        var accepted = actionResult.Result.Should().BeOfType<AcceptedResult>().Subject;
        accepted.StatusCode.Should().Be(StatusCodes.Status202Accepted);
        accepted.Value.Should().Be("job-1");
        accepted.Location.Should().BeNull();
    }

    [Fact]
    public void should_return_accepted_with_location_when_success()
    {
        // given
        var controller = _CreateController();
        var result = ApiResult<string>.Ok("job-1");

        // when
        var actionResult = result.ToAcceptedActionResult(
            controller,
            _CreateProblemDetailsCreator(),
            id => $"/jobs/{id}"
        );

        // then
        var accepted = actionResult.Result.Should().BeOfType<AcceptedResult>().Subject;
        accepted.Location.Should().Be("/jobs/job-1");
        accepted.Value.Should().Be("job-1");
    }

    [Fact]
    public void should_return_bodyless_accepted_for_a_unit_success()
    {
        // given
        var controller = _CreateController();
        var creator = _CreateProblemDetailsCreator();

        // when
        var withoutLocation = ApiResult.Ok().ToAcceptedActionResult(controller, creator);
        var withLocation = ApiResult.Ok().ToAcceptedActionResult(controller, creator, "/jobs/job-1");

        // then
        var bare = withoutLocation.Result.Should().BeOfType<AcceptedResult>().Subject;
        bare.Value.Should().BeNull();
        bare.Location.Should().BeNull();
        var located = withLocation.Result.Should().BeOfType<AcceptedResult>().Subject;
        located.Value.Should().BeNull();
        located.Location.Should().Be("/jobs/job-1");
    }

    [Fact]
    public void should_return_problem_when_accepted_fails()
    {
        // given
        var controller = _CreateController();
        var creator = _CreateProblemDetailsCreator();

        // when
        var valued = ApiResult<string>
            .Fail(new ConflictError("job:busy", "Busy."))
            .ToAcceptedActionResult(controller, creator);
        var unit = ApiResult
            .Fail(new ConflictError("job:busy", "Busy."))
            .ToAcceptedActionResult(controller, creator, "/jobs/x");

        // then
        valued.Result.Should().BeOfType<ConflictObjectResult>();
        unit.Result.Should().BeOfType<ConflictObjectResult>();
    }

    [Fact]
    public void should_reject_a_null_location_for_created_and_accepted()
    {
        // given
        var controller = _CreateController();
        var creator = _CreateProblemDetailsCreator();

        // when
        var created = () => ApiResult<string>.Ok("a1").ToCreatedActionResult(controller, creator, null!);
        var accepted = () =>
            ApiResult<string>.Ok("a1").ToAcceptedActionResult(controller, creator, (Func<string, string>)null!);
        var unit = () => ApiResult.Ok().ToAcceptedActionResult(controller, creator, (string)null!);

        // then
        created.Should().Throw<ArgumentNullException>();
        accepted.Should().Throw<ArgumentNullException>();
        unit.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void should_publish_the_created_and_accepted_success_responses()
    {
        // given
        var created = new RouteEndpointBuilder(_ => Task.CompletedTask, RoutePatternFactory.Parse("/"), order: 0);
        var accepted = new RouteEndpointBuilder(_ => Task.CompletedTask, RoutePatternFactory.Parse("/"), order: 0);
        var acceptedUnit = new RouteEndpointBuilder(_ => Task.CompletedTask, RoutePatternFactory.Parse("/"), order: 0);

        // when
        ApiCreatedActionResult<string>.PopulateMetadata(_ActionMethod, created);
        ApiAcceptedActionResult<string>.PopulateMetadata(_ActionMethod, accepted);
        ApiAcceptedActionResult.PopulateMetadata(_ActionMethod, acceptedUnit);

        // then
        var createdResponse = created.Metadata.OfType<IProducesResponseTypeMetadata>().Should().ContainSingle().Subject;
        createdResponse.StatusCode.Should().Be(201);
        createdResponse.Type.Should().Be<string>();
        var acceptedResponse = accepted
            .Metadata.OfType<IProducesResponseTypeMetadata>()
            .Should()
            .ContainSingle()
            .Subject;
        acceptedResponse.StatusCode.Should().Be(202);
        acceptedResponse.Type.Should().Be<string>();
        var unitResponse = acceptedUnit
            .Metadata.OfType<IProducesResponseTypeMetadata>()
            .Should()
            .ContainSingle()
            .Subject;
        unitResponse.StatusCode.Should().Be(202);
        unitResponse.Type.Should().Be(typeof(void));
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
        creator.Received(1).Unauthorized(error: descriptor);
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

    [Fact]
    public void should_map_an_error_the_application_mapped_with_its_status()
    {
        // given - the mappings come from the request's services, like the creator in a running app
        var services = new ServiceCollection();
        services.AddHeadlessProblemDetails(problems =>
            problems.MapError<QuotaError>(StatusCodes.Status429TooManyRequests)
        );
        var controller = new TestController
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() },
            },
        };

        // when
        var actionResult = new QuotaError().ToActionResult(
            controller,
            _CreateLocalizingCreator(new OutOfStockLocalizer())
        );

        // then
        var objectResult = actionResult.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(StatusCodes.Status429TooManyRequests);
        objectResult.Value.Should().BeOfType<ProblemDetails>().Which.Detail.Should().Be("The quota is used up.");
    }

    private sealed record QuotaError : ApiResultError
    {
        public override string Code => "app:quota";

        public override string Message => "The quota is used up.";
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
            .Unauthorized(Arg.Any<string?>(), Arg.Any<ErrorDescriptor?>())
            .Returns(new ProblemDetails { Status = StatusCodes.Status401Unauthorized, Title = "Unauthorized" });

        creator
            .Conflict(Arg.Any<IReadOnlyCollection<ErrorDescriptor>>())
            .Returns(ci => new ProblemDetails { Status = StatusCodes.Status409Conflict, Title = "Conflict" });

        return creator;
    }

    // Resolves the real creator through the public registration so the test also proves that a
    // localizer registered by the application replaces the framework's no-op default.
    private static IProblemDetailsCreator _CreateLocalizingCreator(IErrorDescriptionLocalizer localizer)
    {
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(Substitute.For<IBuildInformationAccessor>());
        services.AddHttpContextAccessor();
        services.AddSingleton(localizer);
        services.AddHeadlessProblemDetails();

        return services.BuildServiceProvider().GetRequiredService<IProblemDetailsCreator>();
    }

    private sealed class OutOfStockLocalizer : IErrorDescriptionLocalizer
    {
        public string? Localize(ErrorDescriptor error) =>
            string.Equals(error.Code, "app:out_of_stock", StringComparison.Ordinal) ? "نفدت الكمية" : null;
    }

    private static ApiActionResult? _Action() => null;

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
