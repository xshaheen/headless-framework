// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0062 // Local functions can be made static - closures needed for capturing test state

using Headless.Api.Middlewares;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace Tests.Middlewares;

public sealed class ServerTimingMiddlewareTests : TestBase
{
    private const string _ServerTimingHeader = "Server-Timing";

    private static ServerTimingMiddleware _CreateMiddleware()
    {
        return new ServerTimingMiddleware();
    }

    private static DefaultHttpContext _CreateContext(bool supportsTrailers = true)
    {
        var ctx = new DefaultHttpContext();

        if (supportsTrailers)
        {
            var trailersFeature = Substitute.For<IHttpResponseTrailersFeature>();
            var trailers = new HeaderDictionary();
            trailersFeature.Trailers.Returns(trailers);
            ctx.Features.Set(trailersFeature);
        }

        return ctx;
    }

    [Fact]
    public async Task should_call_next_without_trailer_when_not_supported()
    {
        // given
        var middleware = _CreateMiddleware();
        var context = _CreateContext(supportsTrailers: false);
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
        context.Response.Headers.Should().NotContainKey("Trailer");
    }

    [Fact]
    public async Task should_declare_server_timing_trailer()
    {
        // given
        var middleware = _CreateMiddleware();
        var context = _CreateContext();
        Task next(HttpContext _) => Task.CompletedTask;

        // when
        await middleware.InvokeAsync(context, next);

        // then
        context.Response.Headers.Should().ContainKey("Trailer");
        context.Response.Headers["Trailer"].ToString().Should().Contain(_ServerTimingHeader);
    }

    [Fact]
    public async Task should_append_server_timing_trailer_after_next()
    {
        // given
        var middleware = _CreateMiddleware();
        var context = _CreateContext();
        var trailersFeature = context.Features.Get<IHttpResponseTrailersFeature>()!;
        Task next(HttpContext _) => Task.CompletedTask;

        // when
        await middleware.InvokeAsync(context, next);

        // then
        trailersFeature.Trailers.Should().ContainKey(_ServerTimingHeader);
        var timingValue = trailersFeature.Trailers[_ServerTimingHeader].ToString();
        timingValue.Should().StartWith("app;dur=");
    }

    [Fact]
    public async Task should_format_timing_in_microseconds()
    {
        // given
        var middleware = _CreateMiddleware();
        var context = _CreateContext();
        var trailersFeature = context.Features.Get<IHttpResponseTrailersFeature>()!;
        Task next(HttpContext _) => Task.Delay(10); // Small delay to ensure measurable time

        // when
        await middleware.InvokeAsync(context, next);

        // then
        var timingValue = trailersFeature.Trailers[_ServerTimingHeader].ToString();
        // Format: app;dur=12345.0
        timingValue.Should().MatchRegex(@"^app;dur=\d+\.0$");
    }

    [Fact]
    public async Task should_use_invariant_culture_for_formatting()
    {
        // given
        var middleware = _CreateMiddleware();
        var context = _CreateContext();
        var trailersFeature = context.Features.Get<IHttpResponseTrailersFeature>()!;
        var originalCulture = Thread.CurrentThread.CurrentCulture;

        try
        {
            // Set culture that uses comma as decimal separator
            Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
            Task next(HttpContext _) => Task.CompletedTask;

            // when
            await middleware.InvokeAsync(context, next);

            // then
            var timingValue = trailersFeature.Trailers[_ServerTimingHeader].ToString();
            // Should use period, not comma (invariant culture)
            timingValue.Should().Contain(".0");
            timingValue.Should().NotContain(",");
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = originalCulture;
        }
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

    [Fact]
    public async Task should_throw_when_next_null()
    {
        // given
        var middleware = _CreateMiddleware();
        var context = _CreateContext();

        // when
        var act = () => middleware.InvokeAsync(context, null!);

        // then
        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("next");
    }
}
