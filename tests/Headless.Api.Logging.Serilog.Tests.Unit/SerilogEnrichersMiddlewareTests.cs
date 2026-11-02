// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api;
using Headless.Context;
using Headless.Logging;
using Headless.Primitives;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Tests;

public sealed class SerilogEnrichersMiddlewareTests : TestBase
{
    [Fact]
    public async Task should_push_identity_properties_when_request_context_has_values()
    {
        // given
        var requestContext = _CreateRequestContext(new UserId("user-1"), new AccountId("account-1"), "corr-1");
        var sink = new CollectingSink();
        var logger = _CreateLogger(sink);
        var sut = new SerilogEnrichersMiddleware(_ => _LogAsync(logger));

        // when
        await sut.InvokeAsync(new DefaultHttpContext(), requestContext);

        // then
        var logEvent = sink.Events.Should().ContainSingle().Subject;
        _ScalarText(logEvent, "UserId").Should().Be("user-1");
        _ScalarText(logEvent, "AccountId").Should().Be("account-1");
        _ScalarText(logEvent, "CorrelationId").Should().Be("corr-1");
    }

    [Fact]
    public async Task should_push_only_present_properties_when_some_values_are_null()
    {
        // given
        var requestContext = _CreateRequestContext(userId: null, accountId: null, correlationId: "corr-1");
        var sink = new CollectingSink();
        var logger = _CreateLogger(sink);
        var sut = new SerilogEnrichersMiddleware(_ => _LogAsync(logger));

        // when
        await sut.InvokeAsync(new DefaultHttpContext(), requestContext);

        // then
        var logEvent = sink.Events.Should().ContainSingle().Subject;
        logEvent.Properties.Should().NotContainKey("UserId").And.NotContainKey("AccountId");
        _ScalarText(logEvent, "CorrelationId").Should().Be("corr-1");
    }

    [Fact]
    public async Task should_invoke_next_without_properties_when_request_context_has_no_values()
    {
        // given
        var requestContext = _CreateRequestContext(userId: null, accountId: null, correlationId: null);
        var sink = new CollectingSink();
        var logger = _CreateLogger(sink);
        var context = new DefaultHttpContext();
        HttpContext? seen = null;
        var sut = new SerilogEnrichersMiddleware(httpContext =>
        {
            seen = httpContext;

            return _LogAsync(logger);
        });

        // when
        await sut.InvokeAsync(context, requestContext);

        // then
        seen.Should().BeSameAs(context);
        var logEvent = sink.Events.Should().ContainSingle().Subject;
        logEvent.Properties.Should().BeEmpty();
    }

    [Fact]
    public async Task should_pop_properties_when_request_completes()
    {
        // given
        var requestContext = _CreateRequestContext(new UserId("user-1"), accountId: null, correlationId: null);
        var sink = new CollectingSink();
        var logger = _CreateLogger(sink);
        var sut = new SerilogEnrichersMiddleware(_ => Task.CompletedTask);

        // when
        await sut.InvokeAsync(new DefaultHttpContext(), requestContext);
        logger.Information("after");

        // then
        sink.Events.Should().ContainSingle().Which.Properties.Should().NotContainKey("UserId");
    }

    [Fact]
    public async Task should_resolve_request_context_per_request_when_added_through_the_pipeline()
    {
        // given
        var requestContext = _CreateRequestContext(new UserId("user-1"), accountId: null, correlationId: null);
        var sink = new CollectingSink();
        var logger = _CreateLogger(sink);
        var services = new ServiceCollection().AddScoped(_ => requestContext).BuildServiceProvider();
        await using var _ = services;
        var app = new ApplicationBuilder(services);
        app.UseHeadlessSerilogEnrichers();
        app.Run(_ => _LogAsync(logger));
        var pipeline = app.Build();

        await using var scope = services.CreateAsyncScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };

        // when
        await pipeline(context);

        // then
        _ScalarText(sink.Events.Should().ContainSingle().Subject, "UserId").Should().Be("user-1");
    }

    private static IRequestContext _CreateRequestContext(UserId? userId, AccountId? accountId, string? correlationId)
    {
        var user = Substitute.For<ICurrentUser>();
        user.UserId.Returns(userId);
        user.AccountId.Returns(accountId);

        var requestContext = Substitute.For<IRequestContext>();
        requestContext.User.Returns(user);
        requestContext.CorrelationId.Returns(correlationId);

        return requestContext;
    }

    // Typed as ILogger: the logger has only an in-memory sink, so it holds nothing that needs disposing.
    private static ILogger _CreateLogger(ILogEventSink sink)
    {
        return new LoggerConfiguration().Enrich.FromLogContext().WriteTo.Sink(sink).CreateLogger();
    }

    private static Task _LogAsync(ILogger logger)
    {
        logger.Information("inside");

        return Task.CompletedTask;
    }

    private static string? _ScalarText(LogEvent logEvent, string name)
    {
        logEvent.Properties.Should().ContainKey(name);

        return logEvent.Properties.TryGetValue(name, out var value) ? ((ScalarValue)value).Value?.ToString() : null;
    }

    private sealed class CollectingSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];

        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }
}
