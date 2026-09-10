// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Api;
using Headless.Api.Concurrency;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests.Concurrency;

public sealed class EntityTagResponseFilterTests : TestBase
{
    [Fact]
    public async Task should_emit_a_strong_etag_for_successful_etag_results()
    {
        var result = new OkObjectResult(new EntityTaggedResource(EntityTag.FromUInt32(0x01020304)));
        var context = _CreateContext(result);

        await new EntityTagResponseFilter().OnResultExecutionAsync(context, () => _Next(context));
        await _StartResponseAsync(context.HttpContext);

        context.HttpContext.Response.Headers.ETag.ToString().Should().Be("\"AQIDBA==\"");
    }

    [Fact]
    public async Task should_not_emit_an_etag_for_error_results()
    {
        var result = new ObjectResult(new EntityTaggedResource(EntityTag.FromUInt32(0x01020304))) { StatusCode = 409 };
        var context = _CreateContext(result);

        await new EntityTagResponseFilter().OnResultExecutionAsync(context, () => _Next(context));
        await _StartResponseAsync(context.HttpContext);

        context.HttpContext.Response.Headers.Should().NotContainKey("ETag");
    }

    [Fact]
    public async Task should_not_emit_an_etag_when_result_execution_changes_the_status_to_an_error()
    {
        var result = new OkObjectResult(new EntityTaggedResource(EntityTag.FromUInt32(0x01020304)));
        var context = _CreateContext(result);

        await new EntityTagResponseFilter().OnResultExecutionAsync(
            context,
            () =>
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status406NotAcceptable;
                return _Next(context);
            }
        );
        await _StartResponseAsync(context.HttpContext);

        context.HttpContext.Response.Headers.Should().NotContainKey("ETag");
    }

    [Fact]
    public void should_register_the_profile_once()
    {
        var services = new ServiceCollection();

        services.AddHeadlessMvcEntityTagConcurrency();
        services.AddHeadlessMvcEntityTagConcurrency();

        using var provider = services.BuildServiceProvider();
        provider
            .GetRequiredService<IOptions<MvcOptions>>()
            .Value.Filters.OfType<TypeFilterAttribute>()
            .Should()
            .ContainSingle(x => x.ImplementationType == typeof(EntityTagResponseFilter));
        provider
            .GetRequiredService<IOptions<MvcOptions>>()
            .Value.Filters.OfType<TypeFilterAttribute>()
            .Should()
            .ContainSingle(x => x.ImplementationType == typeof(IfMatchActionFilter));
    }

    private static ResultExecutingContext _CreateContext(ObjectResult result)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Features.Set<IHttpResponseFeature>(new CallbackResponseFeature());
        var action = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        return new ResultExecutingContext(action, [], result, new object());
    }

    private static Task<ResultExecutedContext> _Next(ResultExecutingContext context)
    {
        if (
            context.HttpContext.Response.StatusCode == StatusCodes.Status200OK
            && context.Result is ObjectResult { StatusCode: { } statusCode }
        )
        {
            context.HttpContext.Response.StatusCode = statusCode;
        }

        return Task.FromResult(new ResultExecutedContext(context, [], context.Result, context.Controller));
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
