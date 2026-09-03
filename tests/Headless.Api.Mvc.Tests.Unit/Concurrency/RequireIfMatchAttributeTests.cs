// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Api.Concurrency;
using Headless.Primitives;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Concurrency;

public sealed class RequireIfMatchAttributeTests : TestBase
{
    [Fact]
    public async Task should_return_428_when_header_is_missing()
    {
        var (context, _) = _CreateContext();

        await new IfMatchActionFilter().OnActionExecutionAsync(context, _Next);

        context.Result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(428);
    }

    [Fact]
    public async Task should_decode_valid_tag_into_scoped_context()
    {
        byte[] token = [1, 2, 3, 4, 5];
        var (context, ifMatch) = _CreateContext(EntityTagCodec.Format(token));
        var called = false;

        await new IfMatchActionFilter().OnActionExecutionAsync(
            context,
            () =>
            {
                called = true;
                return Task.FromResult(new ActionExecutedContext(context, [], new object()));
            }
        );

        called.Should().BeTrue();
        ifMatch.ETag.ToArray().Should().Equal(token);
    }

    [Fact]
    public async Task should_ignore_unmarked_actions()
    {
        var (context, _) = _CreateContext(requiresIfMatch: false);
        var called = false;

        await new IfMatchActionFilter().OnActionExecutionAsync(
            context,
            () =>
            {
                called = true;
                return Task.FromResult(new ActionExecutedContext(context, [], new object()));
            }
        );

        called.Should().BeTrue();
        context.Result.Should().BeNull();
    }

    private static (ActionExecutingContext Context, IfMatchContext IfMatch) _CreateContext(
        string? header = null,
        bool requiresIfMatch = true
    )
    {
        var creator = Substitute.For<IProblemDetailsCreator>();
        creator.BadRequest(Arg.Any<string>(), Arg.Any<ErrorDescriptor>()).Returns(new ProblemDetails { Status = 400 });
        var ifMatch = new IfMatchContext();
        var services = new ServiceCollection().AddSingleton(creator).AddSingleton(ifMatch).BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = services };
        if (header is not null)
        {
            http.Request.Headers.IfMatch = header;
        }

        var descriptor = new ActionDescriptor
        {
            EndpointMetadata = requiresIfMatch ? [new RequireIfMatchAttribute()] : [],
        };
        var action = new ActionContext(http, new RouteData(), descriptor);
        return (new ActionExecutingContext(action, [], new Dictionary<string, object?>(), new object()), ifMatch);
    }

    private static Task<ActionExecutedContext> _Next() =>
        throw new InvalidOperationException("Action should not execute.");
}
