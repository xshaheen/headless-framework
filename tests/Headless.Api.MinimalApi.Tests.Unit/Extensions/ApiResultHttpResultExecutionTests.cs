// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api;
using Headless.Primitives;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Extensions;

public sealed class ApiResultHttpResultExecutionTests : TestBase
{
    [Fact]
    public async Task should_write_the_value_as_200_json_on_success()
    {
        // given
        await using var services = MinimalApiTestHost.CreateServices();
        var httpContext = MinimalApiTestHost.CreateHttpContext(services);
        var result = ApiResult<Item>.Ok(new Item("a1", "Pen")).ToHttpResult();

        // when
        await result.ExecuteAsync(httpContext);

        // then
        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        var body = await MinimalApiTestHost.ReadJsonAsync(httpContext, AbortToken);
        body.GetProperty("name").GetString().Should().Be("Pen");
    }

    [Fact]
    public async Task should_build_the_problem_from_the_request_services_on_failure()
    {
        // given
        await using var services = MinimalApiTestHost.CreateServices();
        var httpContext = MinimalApiTestHost.CreateHttpContext(services);
        var result = ApiResult<Item>.Fail(new ConflictError("item:taken", "The item is taken.")).ToHttpResult();

        // when
        await result.ExecuteAsync(httpContext);

        // then
        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        httpContext.Response.ContentType.Should().StartWith("application/problem+json");
        var body = await MinimalApiTestHost.ReadJsonAsync(httpContext, AbortToken);
        body.GetProperty("status").GetInt32().Should().Be(StatusCodes.Status409Conflict);
        body.GetProperty("errors")[0].GetProperty("code").GetString().Should().Be("item:taken");
    }

    [Fact]
    public async Task should_write_the_forbidden_reason_as_the_problem_detail()
    {
        // given
        await using var services = MinimalApiTestHost.CreateServices();
        var httpContext = MinimalApiTestHost.CreateHttpContext(services);
        var result = ApiResult.Forbidden(new ErrorDescriptor("item:locked", "Only owners can edit.")).ToHttpResult();

        // when
        await result.ExecuteAsync(httpContext);

        // then
        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        var body = await MinimalApiTestHost.ReadJsonAsync(httpContext, AbortToken);
        body.GetProperty("detail").GetString().Should().Be("Only owners can edit.");
    }

    [Fact]
    public async Task should_write_204_for_a_unit_success()
    {
        // given
        await using var services = MinimalApiTestHost.CreateServices();
        var httpContext = MinimalApiTestHost.CreateHttpContext(services);

        // when
        await ApiResult.Ok().ToHttpResult().ExecuteAsync(httpContext);

        // then
        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status204NoContent);
        httpContext.Response.Body.Length.Should().Be(0);
    }

    [Fact]
    public async Task should_write_the_problem_for_a_unit_failure()
    {
        // given
        await using var services = MinimalApiTestHost.CreateServices();
        var httpContext = MinimalApiTestHost.CreateHttpContext(services);

        // when
        await ApiResult.NotFound("Item", "a1").ToHttpResult().ExecuteAsync(httpContext);

        // then
        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status404NotFound);
    }

    [Fact]
    public async Task should_throw_when_a_failure_executes_without_a_registered_creator()
    {
        // given
        await using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        var httpContext = MinimalApiTestHost.CreateHttpContext(services);
        var result = ApiResult.Conflict("Taken.").ToHttpResult();

        // when
        var action = () => result.ExecuteAsync(httpContext);

        // then
        await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("*IProblemDetailsCreator*");
    }

    #region Created

    [Fact]
    public async Task should_write_201_with_location_and_value_on_created_success()
    {
        // given
        await using var services = MinimalApiTestHost.CreateServices();
        var httpContext = MinimalApiTestHost.CreateHttpContext(services);
        var result = ApiResult<Item>.Ok(new Item("a1", "Pen")).ToCreatedHttpResult(item => $"/items/{item.Id}");

        // when
        await result.ExecuteAsync(httpContext);

        // then
        result.Location.Should().Be("/items/a1");
        ((IStatusCodeHttpResult)result).StatusCode.Should().Be(StatusCodes.Status201Created);
        ((IValueHttpResult)result).Value.Should().Be(new Item("a1", "Pen"));
        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status201Created);
        httpContext.Response.Headers.Location.ToString().Should().Be("/items/a1");
        var body = await MinimalApiTestHost.ReadJsonAsync(httpContext, AbortToken);
        body.GetProperty("id").GetString().Should().Be("a1");
    }

    [Fact]
    public async Task should_not_build_a_location_for_a_created_failure()
    {
        // given
        await using var services = MinimalApiTestHost.CreateServices();
        var httpContext = MinimalApiTestHost.CreateHttpContext(services);
        var locationCalls = 0;

        // when
        var result = ApiResult<Item>
            .Fail(new NotFoundError { Entity = "Item", Key = "a1" })
            .ToCreatedHttpResult(item =>
            {
                locationCalls++;
                return $"/items/{item.Id}";
            });
        await result.ExecuteAsync(httpContext);

        // then
        locationCalls.Should().Be(0);
        result.Location.Should().BeNull();
        ((IStatusCodeHttpResult)result).StatusCode.Should().Be(StatusCodes.Status404NotFound);
        ((IValueHttpResult)result).Value.Should().BeNull();
        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status404NotFound);
        httpContext.Response.Headers.Location.Should().BeEmpty();
    }

    [Fact]
    public void should_reject_a_null_location_and_a_default_result_for_created()
    {
        // when
        var nullLocation = () => ApiResult<Item>.Ok(new Item("a1", "Pen")).ToCreatedHttpResult(null!);
        var defaultResult = () => default(ApiResult<Item>).ToCreatedHttpResult(item => item.Id);

        // then
        nullLocation.Should().Throw<ArgumentNullException>();
        defaultResult.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void should_publish_only_the_201_response_for_created_results()
    {
        // given
        var builder = new RouteEndpointBuilder(
            static _ => Task.CompletedTask,
            RoutePatternFactory.Parse("/items"),
            order: 0
        );

        // when
        _ = RequestDelegateFactory.Create(
            (Func<ApiResultCreatedHttpResult<Item>>)_CreatedEndpoint,
            new RequestDelegateFactoryOptions { EndpointBuilder = builder }
        );

        // then
        var responses = builder.Metadata.OfType<IProducesResponseTypeMetadata>().ToList();
        responses.Select(response => response.StatusCode).Should().Equal([201]);
        responses.Single(response => response.StatusCode == 201).Type.Should().Be<Item>();
    }

    #endregion

    #region Accepted

    [Fact]
    public async Task should_write_202_with_the_value_and_no_location_on_accepted_success()
    {
        // given
        await using var services = MinimalApiTestHost.CreateServices();
        var httpContext = MinimalApiTestHost.CreateHttpContext(services);
        var result = ApiResult<Item>.Ok(new Item("j1", "Export")).ToAcceptedHttpResult();

        // when
        await result.ExecuteAsync(httpContext);

        // then
        result.Location.Should().BeNull();
        ((IStatusCodeHttpResult)result).StatusCode.Should().Be(StatusCodes.Status202Accepted);
        ((IValueHttpResult)result).Value.Should().Be(new Item("j1", "Export"));
        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status202Accepted);
        httpContext.Response.Headers.Location.Should().BeEmpty();
        var body = await MinimalApiTestHost.ReadJsonAsync(httpContext, AbortToken);
        body.GetProperty("id").GetString().Should().Be("j1");
    }

    [Fact]
    public async Task should_write_202_with_location_on_accepted_success()
    {
        // given
        await using var services = MinimalApiTestHost.CreateServices();
        var httpContext = MinimalApiTestHost.CreateHttpContext(services);
        var result = ApiResult<Item>.Ok(new Item("j1", "Export")).ToAcceptedHttpResult(item => $"/jobs/{item.Id}");

        // when
        await result.ExecuteAsync(httpContext);

        // then
        result.Location.Should().Be("/jobs/j1");
        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status202Accepted);
        httpContext.Response.Headers.Location.ToString().Should().Be("/jobs/j1");
    }

    [Fact]
    public async Task should_not_build_a_location_for_an_accepted_failure()
    {
        // given
        await using var services = MinimalApiTestHost.CreateServices();
        var httpContext = MinimalApiTestHost.CreateHttpContext(services);
        var locationCalls = 0;

        // when
        var result = ApiResult<Item>
            .Fail(new ConflictError("job:busy", "Busy."))
            .ToAcceptedHttpResult(item =>
            {
                locationCalls++;
                return $"/jobs/{item.Id}";
            });
        await result.ExecuteAsync(httpContext);

        // then
        locationCalls.Should().Be(0);
        result.Location.Should().BeNull();
        ((IValueHttpResult)result).Value.Should().BeNull();
        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        httpContext.Response.Headers.Location.Should().BeEmpty();
    }

    [Fact]
    public async Task should_write_a_bodyless_202_for_a_unit_accepted_success()
    {
        // given
        await using var services = MinimalApiTestHost.CreateServices();
        var bareContext = MinimalApiTestHost.CreateHttpContext(services);
        var locatedContext = MinimalApiTestHost.CreateHttpContext(services);

        // when
        await ApiResult.Ok().ToAcceptedHttpResult().ExecuteAsync(bareContext);
        var located = ApiResult.Ok().ToAcceptedHttpResult("/jobs/j1");
        await located.ExecuteAsync(locatedContext);

        // then
        bareContext.Response.StatusCode.Should().Be(StatusCodes.Status202Accepted);
        bareContext.Response.Body.Length.Should().Be(0);
        bareContext.Response.Headers.Location.Should().BeEmpty();
        located.Location.Should().Be("/jobs/j1");
        locatedContext.Response.StatusCode.Should().Be(StatusCodes.Status202Accepted);
        locatedContext.Response.Body.Length.Should().Be(0);
        locatedContext.Response.Headers.Location.ToString().Should().Be("/jobs/j1");
    }

    [Fact]
    public async Task should_write_the_problem_and_no_location_for_a_unit_accepted_failure()
    {
        // given
        await using var services = MinimalApiTestHost.CreateServices();
        var httpContext = MinimalApiTestHost.CreateHttpContext(services);
        var result = ApiResult.NotFound("Job", "j1").ToAcceptedHttpResult("/jobs/j1");

        // when
        await result.ExecuteAsync(httpContext);

        // then
        result.Location.Should().BeNull();
        ((IStatusCodeHttpResult)result).StatusCode.Should().Be(StatusCodes.Status404NotFound);
        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status404NotFound);
        httpContext.Response.Headers.Location.Should().BeEmpty();
    }

    [Fact]
    public void should_reject_a_null_location_and_a_default_result_for_accepted()
    {
        // when
        var nullLocation = () => ApiResult<Item>.Ok(new Item("j1", "Export")).ToAcceptedHttpResult(null!);
        var nullUnitLocation = () => ApiResult.Ok().ToAcceptedHttpResult(null!);
        var defaultResult = () => default(ApiResult<Item>).ToAcceptedHttpResult();
        var defaultUnitResult = () => default(ApiResult).ToAcceptedHttpResult();

        // then
        nullLocation.Should().Throw<ArgumentNullException>();
        nullUnitLocation.Should().Throw<ArgumentNullException>();
        defaultResult.Should().Throw<InvalidOperationException>();
        defaultUnitResult.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void should_publish_only_the_202_response_for_accepted_results()
    {
        // given
        var valued = new RouteEndpointBuilder(static _ => Task.CompletedTask, RoutePatternFactory.Parse("/jobs"), 0);
        var unit = new RouteEndpointBuilder(static _ => Task.CompletedTask, RoutePatternFactory.Parse("/jobs"), 0);

        // when
        _ = RequestDelegateFactory.Create(
            (Func<ApiResultAcceptedHttpResult<Item>>)_AcceptedEndpoint,
            new RequestDelegateFactoryOptions { EndpointBuilder = valued }
        );
        _ = RequestDelegateFactory.Create(
            (Func<ApiResultAcceptedHttpResult>)_AcceptedUnitEndpoint,
            new RequestDelegateFactoryOptions { EndpointBuilder = unit }
        );

        // then
        var valuedResponse = valued.Metadata.OfType<IProducesResponseTypeMetadata>().Should().ContainSingle().Subject;
        valuedResponse.StatusCode.Should().Be(202);
        valuedResponse.Type.Should().Be<Item>();
        var unitResponse = unit.Metadata.OfType<IProducesResponseTypeMetadata>().Should().ContainSingle().Subject;
        unitResponse.StatusCode.Should().Be(202);
        unitResponse.Type.Should().Be(typeof(void));
    }

    #endregion

    [Fact]
    public void should_report_the_status_each_error_kind_executes_with()
    {
        // given
        ApiResultError[] errors =
        [
            new NotFoundError { Entity = "Item", Key = "a1" },
            ValidationError.FromFields(("name", "Name is required")),
            new ForbiddenError(new ErrorDescriptor("item:locked", "Locked.")),
            UnauthorizedError.Instance,
            new ConflictError("item:taken", "Taken."),
            ApiResultError.Custom("item:custom", "Custom."),
        ];

        // when
        var statuses = errors.Select(error => ((IStatusCodeHttpResult)ApiResult.Fail(error).ToHttpResult()).StatusCode);

        // then - an error the framework does not know reports no status before it executes: the application may map
        // it to another status, and only the request's services can say which
        statuses.Should().Equal(404, 422, 403, 401, 409, null);
    }

    [Fact]
    public async Task should_answer_an_error_the_application_mapped_with_its_status_and_descriptor()
    {
        // given
        await using var services = MinimalApiTestHost.CreateServices(services =>
            services.AddHeadlessProblemDetails(problems =>
                problems.MapError<QuotaExceededError>(StatusCodes.Status429TooManyRequests)
            )
        );
        var httpContext = MinimalApiTestHost.CreateHttpContext(services);

        // when
        await ApiResult.Fail(new QuotaExceededError()).ToHttpResult().ExecuteAsync(httpContext);

        // then
        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status429TooManyRequests);
        var body = await MinimalApiTestHost.ReadJsonAsync(httpContext, AbortToken);
        body.GetProperty("status").GetInt32().Should().Be(StatusCodes.Status429TooManyRequests);
        body.GetProperty("detail").GetString().Should().Be("The monthly quota is used up.");
        body.GetProperty("error").GetProperty("code").GetString().Should().Be("item:quota_exceeded");
    }

    [Fact]
    public async Task should_keep_answering_409_for_an_error_no_mapping_covers()
    {
        // given
        await using var services = MinimalApiTestHost.CreateServices();
        var httpContext = MinimalApiTestHost.CreateHttpContext(services);

        // when
        await ApiResult.Fail(new QuotaExceededError()).ToHttpResult().ExecuteAsync(httpContext);

        // then
        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status409Conflict);
    }

    private sealed record QuotaExceededError : ApiResultError
    {
        public override string Code => "item:quota_exceeded";

        public override string Message => "The monthly quota is used up.";
    }

    private static ApiResultCreatedHttpResult<Item> _CreatedEndpoint() => throw new NotSupportedException();

    private static ApiResultAcceptedHttpResult<Item> _AcceptedEndpoint() => throw new NotSupportedException();

    private static ApiResultAcceptedHttpResult _AcceptedUnitEndpoint() => throw new NotSupportedException();

    public sealed record Item(string Id, string Name);
}
