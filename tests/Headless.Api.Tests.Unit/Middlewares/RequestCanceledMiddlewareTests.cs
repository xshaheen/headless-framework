// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0062 // Local functions can be made static - closures needed for capturing test state

using System.Diagnostics;
using Headless.Api.Middlewares;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;

namespace Tests.Middlewares;

public sealed class RequestCanceledMiddlewareTests : TestBase
{
    private static RequestCanceledMiddleware _CreateMiddleware(ILogger<RequestCanceledMiddleware>? logger = null)
    {
        logger ??= Substitute.For<ILogger<RequestCanceledMiddleware>>();
        return new RequestCanceledMiddleware(logger);
    }

    private static DefaultHttpContext _CreateContext(CancellationToken? requestAborted = null)
    {
        var ctx = new DefaultHttpContext();

        if (requestAborted.HasValue)
        {
            var lifetimeFeature = Substitute.For<IHttpRequestLifetimeFeature>();
            lifetimeFeature.RequestAborted.Returns(requestAborted.Value);
            ctx.Features.Set(lifetimeFeature);
        }

        return ctx;
    }

    [Fact]
    public async Task should_call_next_when_not_cancelled()
    {
        // given
        var middleware = _CreateMiddleware();
        var context = _CreateContext();
        var nextCalled = false;
        Task next(HttpContext _)
        {
            nextCalled = true;
            return Task.CompletedTask;
        }

        // when
        await middleware.InvokeAsync(context, next);

        // then
        nextCalled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task should_return_499_when_request_aborted()
    {
        // given
        var middleware = _CreateMiddleware();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var context = _CreateContext(cts.Token);
        Task next(HttpContext _) => throw new OperationCanceledException();

        // when
        await middleware.InvokeAsync(context, next);

        // then
        context.Response.StatusCode.Should().Be(StatusCodes.Status499ClientClosedRequest);
    }

    [Fact]
    public async Task should_not_catch_non_abort_cancellation()
    {
        // given
        var middleware = _CreateMiddleware();
        var context = _CreateContext(CancellationToken.None);
        using var differentCts = new CancellationTokenSource();
        await differentCts.CancelAsync();
        Task next(HttpContext _) => throw new OperationCanceledException(differentCts.Token);

        // when
        var act = () => middleware.InvokeAsync(context, next);

        // then
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task should_add_activity_event_when_cancelled()
    {
        // given
        var middleware = _CreateMiddleware();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var context = _CreateContext(cts.Token);

        var activity = new Activity("TestActivity");
        activity.Start();

        var activityFeature = Substitute.For<IHttpActivityFeature>();
        activityFeature.Activity.Returns(activity);
        context.Features.Set(activityFeature);

        Task next(HttpContext _) => throw new OperationCanceledException();

        // when
        await middleware.InvokeAsync(context, next);

        // then
        activity.Events.Should().ContainSingle(e => e.Name == "Client cancelled the request");

        activity.Stop();
    }

    [Fact]
    public async Task should_log_information_when_cancelled()
    {
        // given
        var logger = Substitute.For<ILogger<RequestCanceledMiddleware>>();
        logger.IsEnabled(LogLevel.Information).Returns(true);
        var middleware = _CreateMiddleware(logger);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var context = _CreateContext(cts.Token);
        Task next(HttpContext _) => throw new OperationCanceledException();

        // when
        await middleware.InvokeAsync(context, next);

        // then
        logger
            .Received()
            .Log(
                LogLevel.Information,
                Arg.Is<EventId>(e => e.Id == 5002 && e.Name == "RequestCancelled"),
                Arg.Any<object>(),
                Arg.Is<Exception?>(e => e == null),
                Arg.Any<Func<object, Exception?, string>>()
            );
    }

    [Fact]
    public async Task should_throw_when_context_null()
    {
        // given
        var middleware = _CreateMiddleware();
        Task next(HttpContext _) => Task.CompletedTask;

        // when
        var act = () => middleware.InvokeAsync(null!, next);

        // then
        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("context");
    }
}
