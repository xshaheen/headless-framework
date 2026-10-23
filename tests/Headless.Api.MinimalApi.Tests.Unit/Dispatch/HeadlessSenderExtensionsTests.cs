// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api;
using Headless.Primitives;
using Headless.Testing.Tests;
using Mediator;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;

namespace Tests.Dispatch;

public sealed class HeadlessSenderExtensionsTests : TestBase
{
    [Fact]
    public async Task should_return_200_with_the_handler_value()
    {
        // given
        var sender = Substitute.For<ISender>();
        var request = new GetItem("a1");
        sender.Send(request, AbortToken).Returns(ApiResult<ItemView>.Ok(new ItemView("a1", "Pen")));

        // when
        var result = await sender.DispatchAsync(request, AbortToken);

        // then
        ((IStatusCodeHttpResult)result)
            .StatusCode.Should()
            .Be(StatusCodes.Status200OK);
        ((IValueHttpResult)result).Value.Should().Be(new ItemView("a1", "Pen"));
    }

    [Fact]
    public async Task should_return_the_error_status_when_the_handler_fails()
    {
        // given
        var sender = Substitute.For<ISender>();
        var request = new GetItem("a1");
        sender
            .Send(request, AbortToken)
            .Returns(ApiResult<ItemView>.Fail(new NotFoundError { Entity = "Item", Key = "a1" }));

        // when
        var result = await sender.DispatchAsync(request, AbortToken);

        // then
        ((IStatusCodeHttpResult)result)
            .StatusCode.Should()
            .Be(StatusCodes.Status404NotFound);
    }

    [Fact]
    public async Task should_return_204_for_a_unit_request()
    {
        // given
        var sender = Substitute.For<ISender>();
        var request = new ArchiveItem("a1");
        sender.Send(request, AbortToken).Returns(ApiResult.Ok());

        // when
        var result = await sender.DispatchAsync(request, AbortToken);

        // then
        ((IStatusCodeHttpResult)result)
            .StatusCode.Should()
            .Be(StatusCodes.Status204NoContent);
    }

    [Fact]
    public async Task should_return_201_with_location_for_a_created_request()
    {
        // given
        var sender = Substitute.For<ISender>();
        var request = new CreateItem("Pen");
        sender.Send(request, AbortToken).Returns(ApiResult<ItemView>.Ok(new ItemView("a1", "Pen")));

        // when
        var result = await sender.DispatchCreatedAsync(request, item => $"/items/{item.Id}", AbortToken);

        // then
        ((IStatusCodeHttpResult)result)
            .StatusCode.Should()
            .Be(StatusCodes.Status201Created);
        result.Location.Should().Be("/items/a1");
    }

    [Fact]
    public async Task should_return_202_with_the_handler_value_for_an_accepted_request()
    {
        // given
        var sender = Substitute.For<ISender>();
        var request = new CreateItem("Pen");
        sender.Send(request, AbortToken).Returns(ApiResult<ItemView>.Ok(new ItemView("j1", "Pen")));

        // when
        var bare = await sender.DispatchAcceptedAsync(request, AbortToken);
        var located = await sender.DispatchAcceptedAsync(request, item => $"/jobs/{item.Id}", AbortToken);

        // then
        ((IStatusCodeHttpResult)bare)
            .StatusCode.Should()
            .Be(StatusCodes.Status202Accepted);
        ((IValueHttpResult)bare).Value.Should().Be(new ItemView("j1", "Pen"));
        bare.Location.Should().BeNull();
        ((IStatusCodeHttpResult)located).StatusCode.Should().Be(StatusCodes.Status202Accepted);
        located.Location.Should().Be("/jobs/j1");
    }

    [Fact]
    public async Task should_return_a_bodyless_202_for_an_accepted_unit_request()
    {
        // given
        var sender = Substitute.For<ISender>();
        var request = new ArchiveItem("a1");
        sender.Send(request, AbortToken).Returns(ApiResult.Ok());

        // when
        var bare = await sender.DispatchAcceptedAsync(request, AbortToken);
        var located = await sender.DispatchAcceptedAsync(request, "/jobs/archive-a1", AbortToken);

        // then
        ((IStatusCodeHttpResult)bare)
            .StatusCode.Should()
            .Be(StatusCodes.Status202Accepted);
        bare.Location.Should().BeNull();
        ((IStatusCodeHttpResult)located).StatusCode.Should().Be(StatusCodes.Status202Accepted);
        located.Location.Should().Be("/jobs/archive-a1");
    }

    [Fact]
    public async Task should_answer_400_without_sending_when_an_accepted_request_is_null()
    {
        // given
        var sender = Substitute.For<ISender>();

        // when
        var valued = await sender.DispatchAcceptedAsync((CreateItem?)null, AbortToken);
        var located = await sender.DispatchAcceptedAsync((CreateItem?)null, item => item.Id, AbortToken);
        var unit = await sender.DispatchAcceptedAsync((ArchiveItem?)null, "/jobs/x", AbortToken);

        // then
        ((IStatusCodeHttpResult)valued)
            .StatusCode.Should()
            .Be(StatusCodes.Status400BadRequest);
        ((IStatusCodeHttpResult)located).StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        located.Location.Should().BeNull();
        ((IStatusCodeHttpResult)unit).StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        unit.Location.Should().BeNull();
        await sender.DidNotReceiveWithAnyArgs().Send(default(CreateItem)!, AbortToken);
    }

    [Fact]
    public async Task should_reject_null_sender_and_location_for_accepted()
    {
        // given
        var sender = Substitute.For<ISender>();

        // when
        var nullSender = async () => await ((ISender)null!).DispatchAcceptedAsync(new ArchiveItem("a1"), AbortToken);
        var nullLocation = async () =>
            await sender.DispatchAcceptedAsync(new CreateItem("Pen"), (Func<ItemView, string>)null!, AbortToken);
        var nullUnitLocation = async () =>
            await sender.DispatchAcceptedAsync(new ArchiveItem("a1"), (string)null!, AbortToken);

        // then
        await nullSender.Should().ThrowAsync<ArgumentNullException>();
        await nullLocation.Should().ThrowAsync<ArgumentNullException>();
        await nullUnitLocation.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task should_answer_400_without_sending_when_the_request_is_null()
    {
        // given
        await using var services = MinimalApiTestHost.CreateServices();
        var sender = Substitute.For<ISender>();

        // when
        var valued = await sender.DispatchAsync((GetItem?)null, AbortToken);
        var unit = await sender.DispatchAsync((ArchiveItem?)null, AbortToken);
        var created = await sender.DispatchCreatedAsync((CreateItem?)null, item => item.Id, AbortToken);
        var httpContext = MinimalApiTestHost.CreateHttpContext(services);
        await valued.ExecuteAsync(httpContext);

        // then
        ((IStatusCodeHttpResult)valued)
            .StatusCode.Should()
            .Be(StatusCodes.Status400BadRequest);
        ((IStatusCodeHttpResult)unit).StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        ((IStatusCodeHttpResult)created).StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        await sender.DidNotReceiveWithAnyArgs().Send(default(GetItem)!, AbortToken);
        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        var body = await MinimalApiTestHost.ReadJsonAsync(httpContext, AbortToken);
        body.GetProperty("status").GetInt32().Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task should_write_400_problem_for_null_unit_and_created_requests()
    {
        // given
        await using var services = MinimalApiTestHost.CreateServices();
        var sender = Substitute.For<ISender>();
        var unitContext = MinimalApiTestHost.CreateHttpContext(services);
        var createdContext = MinimalApiTestHost.CreateHttpContext(services);

        // when
        await (await sender.DispatchAsync((ArchiveItem?)null, AbortToken)).ExecuteAsync(unitContext);
        await (await sender.DispatchCreatedAsync((CreateItem?)null, item => item.Id, AbortToken)).ExecuteAsync(
            createdContext
        );

        // then
        unitContext.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        createdContext.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        createdContext.Response.Headers.Location.Should().BeEmpty();
    }

    [Fact]
    public void should_describe_the_malformed_request_sentinel_with_the_bad_request_code()
    {
        // then
        MalformedRequestError.Instance.Code.Should().Be("g:bad_request");
        MalformedRequestError.Instance.Message.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task should_reject_null_sender_and_location()
    {
        // given
        var sender = Substitute.For<ISender>();

        // when
        var nullSender = async () => await ((ISender)null!).DispatchAsync(new GetItem("a1"), AbortToken);
        var nullLocation = async () => await sender.DispatchCreatedAsync(new CreateItem("Pen"), null!, AbortToken);

        // then
        await nullSender.Should().ThrowAsync<ArgumentNullException>();
        await nullLocation.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public void should_publish_openapi_metadata_through_the_dispatch_return_type()
    {
        // given
        var builder = new RouteEndpointBuilder(
            static _ => Task.CompletedTask,
            RoutePatternFactory.Parse("/items/{id}"),
            order: 0
        );

        // when
        _ = RequestDelegateFactory.Create(
            (Func<ValueTask<ApiResultHttpResult<ItemView>>>)_DispatchEndpoint,
            new RequestDelegateFactoryOptions { EndpointBuilder = builder }
        );

        // then
        var responses = builder.Metadata.OfType<IProducesResponseTypeMetadata>().ToList();
        responses.Select(response => response.StatusCode).Should().Equal([200]);
        responses.Single(response => response.StatusCode == 200).Type.Should().Be<ItemView>();
    }

    private static ValueTask<ApiResultHttpResult<ItemView>> _DispatchEndpoint() => throw new NotSupportedException();

    public sealed record ItemView(string Id, string Name);

    public sealed record GetItem(string Id) : IRequest<ApiResult<ItemView>>;

    public sealed record CreateItem(string Name) : IRequest<ApiResult<ItemView>>;

    public sealed record ArchiveItem(string Id) : IRequest<ApiResult>;
}
