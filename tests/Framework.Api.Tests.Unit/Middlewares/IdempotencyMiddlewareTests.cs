// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Framework.Abstractions;
using Framework.Api.Abstractions;
using Framework.Api.Middlewares;
using Framework.Caching;
using Framework.Constants;
using Framework.Testing.Helpers;
using Framework.Testing.Tests;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Middlewares;

public sealed class IdempotencyMiddlewareTests : TestBase
{
    private static IdempotencyMiddleware _CreateMiddleware(
        ICache? cache = null,
        IOptionsSnapshot<IdempotencyMiddlewareOptions>? options = null,
        ICancellationTokenProvider? cts = null,
        IProblemDetailsCreator? problem = null,
        IClock? clock = null,
        ILogger<IdempotencyMiddleware>? logger = null
    )
    {
        var now = DateTimeOffset.UtcNow;

        cache ??= Substitute.For<ICache>();

        var opts = new IdempotencyMiddlewareOptions { IdempotencyKeyExpiration = TimeSpan.FromMinutes(5) };
        options ??= Substitute.For<IOptionsSnapshot<IdempotencyMiddlewareOptions>>();
        options.Value.Returns(opts);

        cts ??= Substitute.For<ICancellationTokenProvider>();
        cts.Token.Returns(CancellationToken.None);

        var timeProvider = new FakeTimeProvider(now);
        clock ??= new TestClock(timeProvider);

        var buildInformationAccessor = Substitute.For<IBuildInformationAccessor>();
        buildInformationAccessor.GetBuildNumber().Returns("test-build");
        buildInformationAccessor.GetCommitNumber().Returns("test-commit");

        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        httpContextAccessor.HttpContext.Returns(new DefaultHttpContext());

        var apiBehaviorOptions = Options.Create(new ApiBehaviorOptions());

        problem ??= new ProblemDetailsCreator(
            timeProvider,
            buildInformationAccessor,
            httpContextAccessor,
            apiBehaviorOptions
        );

        logger ??= Substitute.For<ILogger<IdempotencyMiddleware>>();

        return new IdempotencyMiddleware(cache, options, cts, problem, clock, logger);
    }

    private static DefaultHttpContext _CreateContext(string? idempotencyKey = null)
    {
        var ctx = new DefaultHttpContext();
        // Provide DI for Results.Problem(...) to resolve ProblemDetailsService
        var services = new ServiceCollection().AddLogging().AddProblemDetails().BuildServiceProvider();
        ctx.RequestServices = services;

        // Ensure response body is readable for assertions
        ctx.Response.Body = new MemoryStream();
        if (idempotencyKey is not null)
        {
            ctx.Request.Headers.Append(HttpHeaderNames.IdempotencyKey, idempotencyKey);
        }
        return ctx;
    }

    [Fact]
    public async Task should_pass_through_when_header_missing()
    {
        // arrange
        var cache = Substitute.For<ICache>();
        var middleware = _CreateMiddleware(cache: cache);
        var context = _CreateContext();
        var nextCalled = false;
        Task next(HttpContext _)
        {
            nextCalled = true;
            return Task.CompletedTask;
        }

        // act
        await middleware.InvokeAsync(context, next);

        // assert
        nextCalled.Should().BeTrue();
        await cache
            .DidNotReceiveWithAnyArgs()
            .TryInsertAsync(null!, default(DateTimeOffset), null, CancellationToken.None);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public async Task should_pass_through_when_header_empty_or_null(string? value)
    {
        // arrange
        var cache = Substitute.For<ICache>();
        var middleware = _CreateMiddleware(cache: cache);
        var context = _CreateContext(value);
        var nextCalled = false;
        Task next(HttpContext _)
        {
            nextCalled = true;
            return Task.CompletedTask;
        }

        // act
        await middleware.InvokeAsync(context, next);

        // assert
        nextCalled.Should().BeTrue();
        await cache
            .DidNotReceiveWithAnyArgs()
            .TryInsertAsync(null!, default(DateTimeOffset), null, CancellationToken.None);
    }

    [Fact]
    public async Task should_insert_and_call_next_when_new_key()
    {
        // arrange
        var cache = Substitute.For<ICache>();
        cache
            .TryInsertAsync(
                Arg.Any<string>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.FromResult(true));
        var middleware = _CreateMiddleware(cache: cache);
        var context = _CreateContext("abc-123");
        var nextCalled = false;
        Task next(HttpContext _)
        {
            nextCalled = true;
            return Task.CompletedTask;
        }

        // act
        await middleware.InvokeAsync(context, next);

        // assert
        nextCalled.Should().BeTrue();
        await cache
            .Received(1)
            .TryInsertAsync(
                "idempotency_key:abc-123",
                Arg.Any<DateTimeOffset>(),
                TimeSpan.FromMinutes(5),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_return_409_when_duplicate_key()
    {
        // arrange
        var cache = Substitute.For<ICache>();
        cache
            .TryInsertAsync(
                Arg.Any<string>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.FromResult(false));
        var middleware = _CreateMiddleware(cache: cache);
        var context = _CreateContext("dup-001");
        var nextCalled = false;
        Task next(HttpContext _)
        {
            nextCalled = true;
            return Task.CompletedTask;
        }

        // act
        await middleware.InvokeAsync(context, next);

        // assert
        nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be((int)HttpStatusCode.Conflict);
        context.Response.ContentType.Should().StartWith("application/problem+json");

        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body, leaveOpen: true);
        var body = await reader.ReadToEndAsync(AbortToken);
        body.Should().NotBeNullOrWhiteSpace();

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        root.GetProperty("status").GetInt32().Should().Be((int)HttpStatusCode.Conflict);
        root.GetProperty("title").GetString().Should().Be(HeadlessProblemDetailsConstants.Titles.Conflict);
        root.GetProperty("detail").GetString().Should().Be(HeadlessProblemDetailsConstants.Details.Conflict);

        await cache
            .Received(1)
            .TryInsertAsync(
                "idempotency_key:dup-001",
                Arg.Any<DateTimeOffset>(),
                TimeSpan.FromMinutes(5),
                Arg.Any<CancellationToken>()
            );
    }
}
