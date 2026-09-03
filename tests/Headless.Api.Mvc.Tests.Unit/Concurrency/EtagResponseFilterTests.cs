// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api;
using Headless.Api.Concurrency;
using Headless.Domain;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests.Concurrency;

public sealed class EtagResponseFilterTests : TestBase
{
    [Fact]
    public async Task should_emit_a_strong_etag_for_successful_etag_results()
    {
        var result = new OkObjectResult(new EtagResource { ETag = [1, 2, 3, 4] });
        var context = _CreateContext(result);

        await new EtagResponseFilter().OnResultExecutionAsync(context, () => _Next(context));

        context.HttpContext.Response.Headers.ETag.ToString().Should().Be("\"AQIDBA==\"");
    }

    [Fact]
    public async Task should_not_emit_an_etag_for_error_results()
    {
        var result = new ObjectResult(new EtagResource { ETag = [1, 2, 3, 4] }) { StatusCode = 409 };
        var context = _CreateContext(result);

        await new EtagResponseFilter().OnResultExecutionAsync(context, () => _Next(context));

        context.HttpContext.Response.Headers.Should().NotContainKey("ETag");
    }

    [Fact]
    public void should_register_the_profile_once()
    {
        var services = new ServiceCollection();

        services.AddHeadlessEtagConcurrency();
        services.AddHeadlessEtagConcurrency();

        using var provider = services.BuildServiceProvider();
        provider
            .GetRequiredService<IOptions<MvcOptions>>()
            .Value.Filters.OfType<TypeFilterAttribute>()
            .Should()
            .ContainSingle(x => x.ImplementationType == typeof(EtagResponseFilter));
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

    private sealed class EtagResource : IHasETag
    {
        public byte[]? ETag { get; set; }
    }
}
