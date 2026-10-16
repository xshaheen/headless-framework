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
    public void should_publish_201_and_problem_metadata_for_created_results()
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
        responses.Select(response => response.StatusCode).Should().BeEquivalentTo([201, 400, 401, 403, 404, 409, 422]);
        responses.Single(response => response.StatusCode == 201).Type.Should().Be<Item>();
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

        // then
        statuses.Should().Equal(404, 422, 403, 401, 409, 409);
    }

    private static ApiResultCreatedHttpResult<Item> _CreatedEndpoint() => throw new NotSupportedException();

    public sealed record Item(string Id, string Name);
}
