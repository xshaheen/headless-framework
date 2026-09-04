// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Api;
using Headless.Api.Concurrency;
using Headless.Primitives;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;

namespace Tests.Filters;

public sealed class EntityTagEndpointFilterTests : TestBase
{
    [Fact]
    public async Task should_return_428_when_if_match_is_missing()
    {
        var (context, _) = _CreateContext();

        var result = await new IfMatchEndpointFilter().InvokeAsync(context, _UnexpectedNext);

        result.Should().BeAssignableTo<IStatusCodeHttpResult>().Which.StatusCode.Should().Be(428);
    }

    [Fact]
    public async Task should_expose_one_strong_if_match_tag_to_the_endpoint()
    {
        var (context, ifMatch) = _CreateContext("\"revision-42\"");
        var expected = new object();

        var result = await new IfMatchEndpointFilter().InvokeAsync(
            context,
            _ => ValueTask.FromResult<object?>(expected)
        );

        result.Should().BeSameAs(expected);
        ifMatch.EntityTag.Should().Be(EntityTag.CreateStrong("revision-42"));
    }

    [Theory]
    [InlineData("*")]
    [InlineData("W/\"revision-42\"")]
    [InlineData("\"one\", \"two\"")]
    public async Task should_return_400_when_if_match_is_not_one_strong_tag(string value)
    {
        var (context, _) = _CreateContext(value);

        var result = await new IfMatchEndpointFilter().InvokeAsync(context, _UnexpectedNext);

        result.Should().BeAssignableTo<IStatusCodeHttpResult>().Which.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task should_return_400_when_if_match_uses_multiple_header_fields()
    {
        var (context, _) = _CreateContext();
        context.HttpContext.Request.Headers.IfMatch = new StringValues(["\"one\"", "\"two\""]);

        var result = await new IfMatchEndpointFilter().InvokeAsync(context, _UnexpectedNext);

        result.Should().BeAssignableTo<IStatusCodeHttpResult>().Which.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task should_emit_an_entity_tag_for_a_successful_typed_result()
    {
        var (context, _) = _CreateContext();
        var expected = EntityTag.FromUInt32(0x01020304);
        var result = TypedResults.Ok(new EntityTaggedResource(expected));

        _ = await new EntityTagResponseEndpointFilter().InvokeAsync(
            context,
            _ => ValueTask.FromResult<object?>(result)
        );
        await _StartResponseAsync(context.HttpContext);

        context.HttpContext.Response.Headers.ETag.ToString().Should().Be(expected.HeaderValue);
    }

    [Fact]
    public async Task should_emit_an_entity_tag_for_a_successful_typed_result_union()
    {
        var (context, _) = _CreateContext();
        var expected = EntityTag.FromUInt32(0x01020304);
        Results<Ok<EntityTaggedResource>, NotFound> result = TypedResults.Ok(new EntityTaggedResource(expected));

        _ = await new EntityTagResponseEndpointFilter().InvokeAsync(
            context,
            _ => ValueTask.FromResult<object?>(result)
        );
        await _StartResponseAsync(context.HttpContext);

        context.HttpContext.Response.Headers.ETag.ToString().Should().Be(expected.HeaderValue);
    }

    [Fact]
    public async Task should_emit_an_entity_tag_for_a_successful_api_result()
    {
        var creator = _CreateProblemDetailsCreator();
        var (context, _) = _CreateContext(creator: creator);
        var expected = EntityTag.FromUInt32(0x01020304);
        var result = ApiResult<EntityTaggedResource>.Ok(new EntityTaggedResource(expected)).ToHttpResult(creator);

        _ = await new EntityTagResponseEndpointFilter().InvokeAsync(
            context,
            _ => ValueTask.FromResult<object?>(result)
        );
        await _StartResponseAsync(context.HttpContext);

        context.HttpContext.Response.Headers.ETag.ToString().Should().Be(expected.HeaderValue);
    }

    [Fact]
    public async Task should_not_replace_an_entity_tag_set_by_the_endpoint()
    {
        var (context, _) = _CreateContext();
        context.HttpContext.Response.Headers.ETag = "\"explicit\"";
        var result = TypedResults.Ok(new EntityTaggedResource(EntityTag.CreateStrong("automatic")));

        _ = await new EntityTagResponseEndpointFilter().InvokeAsync(
            context,
            _ => ValueTask.FromResult<object?>(result)
        );
        await _StartResponseAsync(context.HttpContext);

        context.HttpContext.Response.Headers.ETag.ToString().Should().Be("\"explicit\"");
    }

    [Fact]
    public async Task should_not_emit_an_entity_tag_for_a_failed_response()
    {
        var (context, _) = _CreateContext();
        context.HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
        var result = new EntityTaggedResource(EntityTag.CreateStrong("revision-42"));

        _ = await new EntityTagResponseEndpointFilter().InvokeAsync(
            context,
            _ => ValueTask.FromResult<object?>(result)
        );
        await _StartResponseAsync(context.HttpContext);

        context.HttpContext.Response.Headers.Should().NotContainKey("ETag");
    }

    [Fact]
    public async Task should_add_if_match_metadata_to_the_endpoint()
    {
        await using var app = WebApplication.Create();

        app.MapPut("/orders/{id}", (string id) => TypedResults.Ok(id)).RequireIfMatch();

        var endpoints = ((IEndpointRouteBuilder)app).DataSources.SelectMany(static source => source.Endpoints);
        endpoints.Should().ContainSingle().Which.Metadata.GetMetadata<RequireIfMatchAttribute>().Should().NotBeNull();
    }

    private static (EndpointFilterInvocationContext Context, IIfMatchContext IfMatch) _CreateContext(
        string? ifMatchHeader = null,
        IProblemDetailsCreator? creator = null
    )
    {
        var services = new ServiceCollection();
        services.AddSingleton(creator ?? _CreateProblemDetailsCreator());
        services.AddHeadlessMinimalApiEntityTagConcurrency();

        var httpContext = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        httpContext.Features.Set<IHttpResponseFeature>(new CallbackResponseFeature());
        if (ifMatchHeader is not null)
        {
            httpContext.Request.Headers.IfMatch = ifMatchHeader;
        }

        var context = Substitute.For<EndpointFilterInvocationContext>();
        context.HttpContext.Returns(httpContext);
        return (context, httpContext.RequestServices.GetRequiredService<IIfMatchContext>());
    }

    private static IProblemDetailsCreator _CreateProblemDetailsCreator()
    {
        var creator = Substitute.For<IProblemDetailsCreator>();
        creator
            .BadRequest(Arg.Any<string>(), Arg.Any<ErrorDescriptor>())
            .Returns(new ProblemDetails { Status = StatusCodes.Status400BadRequest });
        return creator;
    }

    private static ValueTask<object?> _UnexpectedNext(EndpointFilterInvocationContext _)
    {
        throw new InvalidOperationException("The endpoint should not execute.");
    }

    private static Task _StartResponseAsync(HttpContext context) =>
        ((CallbackResponseFeature)context.Features.GetRequiredFeature<IHttpResponseFeature>()).StartAsync();

    private sealed class CallbackResponseFeature : IHttpResponseFeature
    {
        private readonly Stack<(Func<object, Task> Callback, object State)> _callbacks = [];

        public int StatusCode { get; set; } = StatusCodes.Status200OK;
        public string? ReasonPhrase { get; set; }
        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
        public Stream Body { get; set; } = Stream.Null;
        public bool HasStarted { get; private set; }

        public void OnStarting(Func<object, Task> callback, object state) => _callbacks.Push((callback, state));

        public void OnCompleted(Func<object, Task> callback, object state) { }

        public async Task StartAsync()
        {
            while (_callbacks.TryPop(out var callback))
            {
                await callback.Callback(callback.State);
            }

            HasStarted = true;
        }
    }

    private sealed record EntityTaggedResource(EntityTag Tag) : IHasEntityTag
    {
        public EntityTag GetEntityTag() => Tag;
    }
}
