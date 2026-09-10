// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Api;
using Headless.Api.Concurrency;
using Headless.Api.Resources;
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

        var result = context.Result.Should().BeOfType<ObjectResult>().Which;
        result.StatusCode.Should().Be(428);
        result
            .Value.Should()
            .BeOfType<ProblemDetails>()
            .Which.Extensions["error"]
            .Should()
            .BeOfType<ErrorDescriptor>()
            .Which.Code.Should()
            .Be(GeneralErrorCodes.IfMatchRequired);
    }

    [Fact]
    public async Task should_expose_valid_strong_tag_through_scoped_context()
    {
        var (context, ifMatch) = _CreateContext("\"revision-42\"");
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
        ifMatch.EntityTag.Should().Be(EntityTag.CreateStrong("revision-42"));
    }

    [Fact]
    public async Task should_return_400_when_configured_if_match_validator_rejects_tag()
    {
        var (context, _) = _CreateContext(
            "\"revision-42\"",
            configure: options => options.IfMatchValidator = static tag => tag.TryGetUInt32(out _)
        );

        await new IfMatchActionFilter().OnActionExecutionAsync(context, _Next);

        context.Result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(400);
        context
            .HttpContext.RequestServices.GetRequiredService<IProblemDetailsCreator>()
            .Received(1)
            .BadRequest(
                Arg.Any<string>(),
                Arg.Is<ErrorDescriptor>(descriptor => descriptor.Code == GeneralErrorCodes.IfMatchInvalid)
            );
    }

    [Fact]
    public async Task should_accept_tag_when_configured_if_match_validator_accepts_it()
    {
        var expected = EntityTag.FromUInt32(42);
        var (context, ifMatch) = _CreateContext(
            expected.HeaderValue,
            configure: options => options.IfMatchValidator = static tag => tag.TryGetUInt32(out _)
        );

        await new IfMatchActionFilter().OnActionExecutionAsync(
            context,
            () => Task.FromResult(new ActionExecutedContext(context, [], new object()))
        );

        ifMatch.EntityTag.Should().Be(expected);
    }

    [Theory]
    [InlineData("*")]
    [InlineData("W/\"revision-42\"")]
    [InlineData("\"one\", \"two\"")]
    [InlineData("revision-42")]
    public async Task should_return_400_when_if_match_is_not_one_strong_entity_tag(string value)
    {
        var (context, _) = _CreateContext(value);

        await new IfMatchActionFilter().OnActionExecutionAsync(context, _Next);

        context.Result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(400);
        context
            .HttpContext.RequestServices.GetRequiredService<IProblemDetailsCreator>()
            .Received(1)
            .BadRequest(
                Arg.Any<string>(),
                Arg.Is<ErrorDescriptor>(descriptor => descriptor.Code == GeneralErrorCodes.IfMatchInvalid)
            );
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

    private static (ActionExecutingContext Context, IIfMatchContext IfMatch) _CreateContext(
        string? header = null,
        bool requiresIfMatch = true,
        Action<EntityTagConcurrencyOptions>? configure = null
    )
    {
        var creator = Substitute.For<IProblemDetailsCreator>();
        creator.BadRequest(Arg.Any<string>(), Arg.Any<ErrorDescriptor>()).Returns(new ProblemDetails { Status = 400 });
        var services = new ServiceCollection().AddSingleton(creator);
        _ = configure is null
            ? services.AddHeadlessMvcEntityTagConcurrency()
            : services.AddHeadlessMvcEntityTagConcurrency(configure);
        var provider = services.BuildServiceProvider();
        var ifMatch = provider.GetRequiredService<IIfMatchContext>();
        var http = new DefaultHttpContext { RequestServices = provider };
        if (header is not null)
        {
            http.Request.Headers.IfMatch = header;
        }

        var descriptor = new ActionDescriptor
        {
            EndpointMetadata = requiresIfMatch ? [new RequireIfMatchAttribute()] : [],
        };
        var action = new ActionContext(http, new RouteData(), descriptor);
        return (
            new ActionExecutingContext(
                action,
                [],
                new Dictionary<string, object?>(StringComparer.Ordinal),
                new object()
            ),
            ifMatch
        );
    }

    private static Task<ActionExecutedContext> _Next() =>
        throw new InvalidOperationException("Action should not execute.");
}
