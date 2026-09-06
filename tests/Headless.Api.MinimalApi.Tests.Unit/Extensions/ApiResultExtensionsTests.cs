// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Primitives;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;

namespace Tests.Extensions;

public sealed class ApiResultExtensionsTests : TestBase
{
    [Fact]
    public void should_publish_complete_openapi_metadata_for_valued_results()
    {
        // given
        var builder = _CreateEndpointBuilder();

        // when
        _ = RequestDelegateFactory.Create(
            (Func<ApiResultHttpResult<string>>)_GenericEndpoint,
            new RequestDelegateFactoryOptions { EndpointBuilder = builder }
        );

        // then
        var responses = builder.Metadata.OfType<IProducesResponseTypeMetadata>().ToList();
        responses.Select(response => response.StatusCode).Should().BeEquivalentTo([200, 401, 403, 404, 409, 422]);
        var success = responses.Single(response => response.StatusCode == 200);
        success.Type.Should().Be<string>();
        success.ContentTypes.Should().Equal("application/json");
        responses
            .Where(response => response.StatusCode >= 400)
            .Should()
            .AllSatisfy(response =>
            {
                response.Type.Should().Be<ProblemDetails>();
                response.ContentTypes.Should().Equal("application/problem+json");
            });
    }

    [Fact]
    public void should_publish_complete_openapi_metadata_for_unit_results()
    {
        // given
        var builder = _CreateEndpointBuilder();

        // when
        _ = RequestDelegateFactory.Create(
            (Func<ApiResultHttpResult>)_UnitEndpoint,
            new RequestDelegateFactoryOptions { EndpointBuilder = builder }
        );

        // then
        var responses = builder.Metadata.OfType<IProducesResponseTypeMetadata>().ToList();
        responses.Select(response => response.StatusCode).Should().BeEquivalentTo([204, 401, 403, 404, 409, 422]);
        responses.Single(response => response.StatusCode == 204).ContentTypes.Should().BeEmpty();
    }

    [Fact]
    public void should_publish_openapi_metadata_for_async_valued_results()
    {
        // given
        var builder = _CreateEndpointBuilder();

        // when
        _ = RequestDelegateFactory.Create(
            (Func<Task<ApiResultHttpResult<string>>>)_GenericAsyncEndpoint,
            new RequestDelegateFactoryOptions { EndpointBuilder = builder }
        );

        // then
        builder
            .Metadata.OfType<IProducesResponseTypeMetadata>()
            .Select(response => response.StatusCode)
            .Should()
            .BeEquivalentTo([200, 401, 403, 404, 409, 422]);
    }

    #region ApiResult<T> Tests

    [Fact]
    public void should_return_ok_with_value_when_success()
    {
        // given
        var creator = _CreateProblemDetailsCreator();
        const string expectedValue = "test-value";
        var result = ApiResult<string>.Ok(expectedValue);

        // when
        var httpResult = result.ToHttpResult(creator);

        // then
        httpResult.Should().BeOfType<ApiResultHttpResult<string>>();
        ((IStatusCodeHttpResult)httpResult).StatusCode.Should().Be(StatusCodes.Status200OK);
        ((IValueHttpResult)httpResult).Value.Should().Be(expectedValue);
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
        httpResult.Should().BeOfType<ApiResultHttpResult<string>>();
        ((IStatusCodeHttpResult)httpResult).StatusCode.Should().Be(StatusCodes.Status409Conflict);
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
        httpResult.Should().BeOfType<ApiResultHttpResult>();
        ((IStatusCodeHttpResult)httpResult).StatusCode.Should().Be(StatusCodes.Status204NoContent);
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
        httpResult.Should().BeOfType<ApiResultHttpResult>();
        ((IStatusCodeHttpResult)httpResult).StatusCode.Should().Be(StatusCodes.Status409Conflict);
    }

    #endregion

    #region ApiResultError Mapping Tests

    [Fact]
    public void should_map_not_found_error_to_404()
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
    public void should_map_validation_error_to_422()
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
    public void should_map_forbidden_error_to_403()
    {
        // given
        var creator = _CreateProblemDetailsCreator();
        var error = new ForbiddenError(new ErrorDescriptor(ApiResultErrorCodes.Forbidden, "Access denied"));

        // when
        var httpResult = error.ToHttpResult(creator);

        // then
        httpResult.Should().BeOfType<ProblemHttpResult>();
        var problemResult = (ProblemHttpResult)httpResult;
        problemResult.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public void should_pass_g_forbidden_error_code_to_creator_for_forbidden_error()
    {
        // given — verify the g: prefix is used (framework snake_case convention)
        var creator = _CreateProblemDetailsCreator();
        var error = new ForbiddenError(new ErrorDescriptor(ApiResultErrorCodes.Forbidden, "Access denied"));

        // when
        _ = error.ToHttpResult(creator);

        // then
        creator.Received(1).Forbidden(error: Arg.Is<ErrorDescriptor>(e => e.Code == "g:forbidden"));
    }

    [Fact]
    public void should_map_unauthorized_error_to_401()
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
    public void should_preserve_unauthorized_descriptor_when_mapping_to_401()
    {
        // given
        var creator = _CreateProblemDetailsCreator();
        var descriptor = new ErrorDescriptor("auth:expired", "Session expired");

        // when
        _ = new UnauthorizedError(descriptor).ToHttpResult(creator);

        // then
        creator.Received(1).Unauthorized(descriptor);
    }

    [Fact]
    public void should_map_aggregate_error_to_409()
    {
        // given
        var creator = _CreateProblemDetailsCreator();
        var error = new AggregateError
        {
            Errors = [new ConflictError("error1", "First error"), new ConflictError("error2", "Second error")],
        };

        // when
        var httpResult = error.ToHttpResult(creator);

        // then
        httpResult.Should().BeOfType<ProblemHttpResult>();
        var problemResult = (ProblemHttpResult)httpResult;
        problemResult.StatusCode.Should().Be(StatusCodes.Status409Conflict);
    }

    [Fact]
    public void should_map_conflict_error_to_409()
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
    public void should_preserve_all_conflict_descriptors_when_mapping_to_409()
    {
        // given
        var creator = _CreateProblemDetailsCreator();
        var errors = new[]
        {
            new ErrorDescriptor("user:duplicate_email", "Email already exists").WithParam("email", "a@b.com"),
            new ErrorDescriptor("user:duplicate_phone", "Phone already exists"),
        };

        // when
        _ = new ConflictError(errors).ToHttpResult(creator);

        // then
        creator
            .Received(1)
            .Conflict(Arg.Is<IReadOnlyCollection<ErrorDescriptor>>(actual => actual.SequenceEqual(errors)));
    }

    [Fact]
    public void should_map_aggregate_of_validation_errors_to_422()
    {
        // given
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
        var httpResult = error.ToHttpResult(creator);

        // then
        httpResult
            .Should()
            .BeOfType<ProblemHttpResult>()
            .Which.StatusCode.Should()
            .Be(StatusCodes.Status422UnprocessableEntity);
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
        var httpResult = error.ToHttpResult(creator);

        // then
        httpResult.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity);
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
        var httpResult = error.ToHttpResult(creator);

        // then
        httpResult.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        creator.Received(1).Conflict(Arg.Is<IReadOnlyCollection<ErrorDescriptor>>(actual => actual.Count == 2));
    }

    [Fact]
    public void should_throw_clear_error_when_mapping_default_result()
    {
        // given
        var creator = _CreateProblemDetailsCreator();

        // when
        var action = () => default(ApiResult).ToHttpResult(creator);

        // then
        action.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void should_throw_clear_error_when_mapping_default_generic_result()
    {
        // given
        var creator = _CreateProblemDetailsCreator();

        // when
        var action = () => default(ApiResult<string>).ToHttpResult(creator);

        // then
        action.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void should_map_unknown_error_to_409()
    {
        // given
        var creator = _CreateProblemDetailsCreator();
        var error = ApiResultError.Custom("custom:error", "Custom error message");

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
    public void should_invoke_entity_not_found_factory_for_not_found_error()
    {
        // given
        var creator = _CreateProblemDetailsCreator();
        var error = new NotFoundError { Entity = "Order", Key = "ORD-456" };

        // when
        _ = error.ToHttpResult(creator);

        // then
        creator.Received(1).EntityNotFound();
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
        creator
            .Received(1)
            .UnprocessableEntity(
                Arg.Is<IReadOnlyDictionary<string, IReadOnlyList<ErrorDescriptor>>>(d =>
                    d.ContainsKey("Email") && d["Email"].Count == 2 && d.ContainsKey("Name") && d["Name"].Count == 1
                )
            );
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
        creator
            .Received(1)
            .Conflict(
                Arg.Is<IReadOnlyCollection<ErrorDescriptor>>(errors =>
                    errors.Count == 3
                    && errors.Any(e => e.Code == "error1" && e.Description == "First error")
                    && errors.Any(e => e.Code == "error2" && e.Description == "Second error")
                    && errors.Any(e => e.Code == "error3" && e.Description == "Third error")
                )
            );
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

    private static RouteEndpointBuilder _CreateEndpointBuilder()
    {
        return new RouteEndpointBuilder(
            static _ => Task.CompletedTask,
            RoutePatternFactory.Parse("/api-result"),
            order: 0
        );
    }

    private static ApiResultHttpResult<string> _GenericEndpoint() => throw new NotSupportedException();

    private static Task<ApiResultHttpResult<string>> _GenericAsyncEndpoint() => throw new NotSupportedException();

    private static ApiResultHttpResult _UnitEndpoint() => throw new NotSupportedException();

    #endregion
}
