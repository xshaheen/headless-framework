// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Api;
using Headless.Api.Concurrency;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Http;
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

        context.HttpContext.Response.Headers.ETag.ToString().Should().Be("\"AQIDBA==\"");
    }

    [Fact]
    public async Task should_not_emit_an_etag_for_error_results()
    {
        var result = new ObjectResult(new EntityTaggedResource(EntityTag.FromUInt32(0x01020304))) { StatusCode = 409 };
        var context = _CreateContext(result);

        await new EntityTagResponseFilter().OnResultExecutionAsync(context, () => _Next(context));

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
        var action = new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor());
        return new ResultExecutingContext(action, [], result, new object());
    }

    private static Task<ResultExecutedContext> _Next(ResultExecutingContext context) =>
        Task.FromResult(new ResultExecutedContext(context, [], context.Result, context.Controller));

    private sealed record EntityTaggedResource(EntityTag Tag) : IHasEntityTag
    {
        public EntityTag GetEntityTag() => Tag;
    }
}
