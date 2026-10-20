// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api;
using Headless.Primitives;
using Headless.Testing.Tests;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Dispatch;

public sealed class HeadlessRequestEndpointRouteBuilderExtensionsTests : TestBase
{
    private readonly ISender _sender = Substitute.For<ISender>();

    [Fact]
    public async Task should_bind_a_get_request_from_route_and_query_values()
    {
        // given
        await using var services = _CreateServices();
        var endpoints = new TestEndpointRouteBuilder(services);
        _sender
            .Send(Arg.Any<FindItem>(), Arg.Any<CancellationToken>())
            .Returns(call => ApiResult<ItemView>.Ok(new ItemView(call.Arg<FindItem>().Id, call.Arg<FindItem>().Lang)));

        // when
        endpoints.MapGetRequest<FindItem, ItemView>("/items/{id}");
        var httpContext = MinimalApiTestHost.CreateHttpContext(services);
        httpContext.Request.Method = HttpMethods.Get;
        httpContext.Request.RouteValues["id"] = "a1";
        httpContext.Request.QueryString = new QueryString("?lang=ar");
        await MinimalApiTestHost.SingleEndpoint(endpoints).RequestDelegate!(httpContext);

        // then
        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        var body = await MinimalApiTestHost.ReadJsonAsync(httpContext, AbortToken);
        body.GetProperty("id").GetString().Should().Be("a1");
        body.GetProperty("name").GetString().Should().Be("ar");
    }

    [Fact]
    public async Task should_bind_a_post_request_from_the_body()
    {
        // given
        await using var services = _CreateServices();
        var endpoints = new TestEndpointRouteBuilder(services);
        _sender
            .Send(Arg.Any<CreateItem>(), Arg.Any<CancellationToken>())
            .Returns(call => ApiResult<ItemView>.Ok(new ItemView("a1", call.Arg<CreateItem>().Name)));

        // when
        endpoints.MapPostRequest<CreateItem, ItemView>("/items");
        var httpContext = MinimalApiTestHost.CreatePostContext(services, """{"name":"Pen"}""");
        await MinimalApiTestHost.SingleEndpoint(endpoints).RequestDelegate!(httpContext);

        // then
        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        var body = await MinimalApiTestHost.ReadJsonAsync(httpContext, AbortToken);
        body.GetProperty("name").GetString().Should().Be("Pen");
    }

    [Theory]
    [InlineData("")]
    [InlineData("null")]
    public async Task should_answer_400_problem_for_an_empty_body_without_sending(string body)
    {
        // given
        await using var services = _CreateServices();
        var endpoints = new TestEndpointRouteBuilder(services);

        // when
        endpoints.MapPostRequest<CreateItem, ItemView>("/items");
        var httpContext = MinimalApiTestHost.CreatePostContext(services, body);
        await MinimalApiTestHost.SingleEndpoint(endpoints).RequestDelegate!(httpContext);

        // then
        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        httpContext.Response.ContentType.Should().StartWith("application/problem+json");
        await _sender.DidNotReceiveWithAnyArgs().Send(default(CreateItem)!, AbortToken);
    }

    [Theory]
    [InlineData("")]
    [InlineData("null")]
    public async Task should_answer_400_problem_for_an_empty_body_on_a_created_post_without_sending(string body)
    {
        // given
        await using var services = _CreateServices();
        var endpoints = new TestEndpointRouteBuilder(services);
        var locationCalls = 0;

        // when
        endpoints.MapPostRequest<CreateItem, ItemView>(
            "/items",
            item =>
            {
                locationCalls++;
                return $"/items/{item.Id}";
            }
        );
        var httpContext = MinimalApiTestHost.CreatePostContext(services, body);
        await MinimalApiTestHost.SingleEndpoint(endpoints).RequestDelegate!(httpContext);

        // then
        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        httpContext.Response.ContentType.Should().StartWith("application/problem+json");
        httpContext.Response.Headers.Location.Should().BeEmpty();
        locationCalls.Should().Be(0);
        await _sender.DidNotReceiveWithAnyArgs().Send(default(CreateItem)!, AbortToken);
    }

    [Fact]
    public async Task should_answer_201_with_location_for_a_created_post()
    {
        // given
        await using var services = _CreateServices();
        var endpoints = new TestEndpointRouteBuilder(services);
        _sender
            .Send(Arg.Any<CreateItem>(), Arg.Any<CancellationToken>())
            .Returns(ApiResult<ItemView>.Ok(new ItemView("a1", "Pen")));

        // when
        endpoints.MapPostRequest<CreateItem, ItemView>("/items", item => $"/items/{item.Id}");
        var httpContext = MinimalApiTestHost.CreatePostContext(services, """{"name":"Pen"}""");
        var endpoint = MinimalApiTestHost.SingleEndpoint(endpoints);
        await endpoint.RequestDelegate!(httpContext);

        // then
        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status201Created);
        httpContext.Response.Headers.Location.ToString().Should().Be("/items/a1");
        endpoint
            .Metadata.OfType<IProducesResponseTypeMetadata>()
            .Should()
            .Contain(response => response.StatusCode == StatusCodes.Status201Created);
    }

    [Fact]
    public async Task should_answer_204_for_unit_requests_on_every_verb()
    {
        // given
        await using var services = _CreateServices();
        _sender.Send(Arg.Any<ArchiveItem>(), Arg.Any<CancellationToken>()).Returns(ApiResult.Ok());
        var cases = new (string Method, Action<IEndpointRouteBuilder> Map)[]
        {
            (HttpMethods.Get, static e => e.MapGetRequest<ArchiveItem>("/items/{id}")),
            (HttpMethods.Delete, static e => e.MapDeleteRequest<ArchiveItem>("/items/{id}")),
            (HttpMethods.Post, static e => e.MapPostRequest<ArchiveItem>("/items/{id}")),
            (HttpMethods.Put, static e => e.MapPutRequest<ArchiveItem>("/items/{id}")),
            (HttpMethods.Patch, static e => e.MapPatchRequest<ArchiveItem>("/items/{id}")),
        };

        foreach (var (method, map) in cases)
        {
            var endpoints = new TestEndpointRouteBuilder(services);
            map(endpoints);
            var httpContext = MinimalApiTestHost.CreatePostContext(services, """{"id":"a1"}""");
            httpContext.Request.Method = method;
            httpContext.Request.RouteValues["id"] = "a1";

            // when
            var endpoint = MinimalApiTestHost.SingleEndpoint(endpoints);
            await endpoint.RequestDelegate!(httpContext);

            // then
            httpContext.Response.StatusCode.Should().Be(StatusCodes.Status204NoContent, method);
            endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()!.HttpMethods.Should().Equal(method);
        }
    }

    [Fact]
    public async Task should_answer_200_for_valued_requests_on_every_verb()
    {
        // given
        await using var services = _CreateServices();
        _sender
            .Send(Arg.Any<FindItem>(), Arg.Any<CancellationToken>())
            .Returns(ApiResult<ItemView>.Ok(new ItemView("a1", "Pen")));
        var cases = new (string Method, Action<IEndpointRouteBuilder> Map)[]
        {
            (HttpMethods.Delete, static e => e.MapDeleteRequest<FindItem, ItemView>("/items/{id}")),
            (HttpMethods.Put, static e => e.MapPutRequest<FindItem, ItemView>("/items/{id}")),
            (HttpMethods.Patch, static e => e.MapPatchRequest<FindItem, ItemView>("/items/{id}")),
        };

        foreach (var (method, map) in cases)
        {
            var endpoints = new TestEndpointRouteBuilder(services);
            map(endpoints);
            var httpContext = MinimalApiTestHost.CreatePostContext(services, """{"id":"a1","lang":"en"}""");
            httpContext.Request.Method = method;
            httpContext.Request.RouteValues["id"] = "a1";

            // when
            var endpoint = MinimalApiTestHost.SingleEndpoint(endpoints);
            await endpoint.RequestDelegate!(httpContext);

            // then
            httpContext.Response.StatusCode.Should().Be(StatusCodes.Status200OK, method);
            endpoint
                .Metadata.OfType<IProducesResponseTypeMetadata>()
                .Select(response => response.StatusCode)
                .Should()
                .Equal([200], method);
        }
    }

    [Fact]
    public void should_reject_null_arguments()
    {
        // given
        var endpoints = new TestEndpointRouteBuilder(new ServiceCollection().BuildServiceProvider());

        // when
        var nullBuilder = () => ((IEndpointRouteBuilder)null!).MapGetRequest<FindItem, ItemView>("/items");
        var nullPattern = () => endpoints.MapPostRequest<ArchiveItem>(null!);
        var nullLocation = () => endpoints.MapPostRequest<CreateItem, ItemView>("/items", null!);

        // then
        nullBuilder.Should().Throw<ArgumentNullException>();
        nullPattern.Should().Throw<ArgumentNullException>();
        nullLocation.Should().Throw<ArgumentNullException>();
    }

    private ServiceProvider _CreateServices()
    {
        return MinimalApiTestHost.CreateServices(services => services.AddSingleton(_sender));
    }

    public sealed record ItemView(string Id, string Name);

    public sealed record FindItem(string Id, string? Lang) : IRequest<ApiResult<ItemView>>;

    public sealed record CreateItem(string Name) : IRequest<ApiResult<ItemView>>;

    public sealed record ArchiveItem(string Id) : IRequest<ApiResult>;
}
