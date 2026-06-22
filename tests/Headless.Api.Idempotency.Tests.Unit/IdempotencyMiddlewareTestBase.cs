// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Api;
using Headless.Api.Abstractions;
using Headless.Caching;
using Headless.Constants;
using Headless.Primitives;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using IdempotencyMiddleware = Headless.Api.IdempotencyMiddleware;

namespace Tests;

/// <summary>
/// Shared fixture-style helpers used by IdempotencyMiddleware unit tests. Centralizes the
/// substitute graph (tenant, user, clock, ct provider, logger, service provider) so individual
/// tests can override only the collaborators they care about.
/// </summary>
public abstract class IdempotencyMiddlewareTestBase : TestBase
{
    internal IdempotencyMiddleware CreateMiddleware(
        IOptionsMonitor<IdempotencyOptions>? options = null,
        ICache? cache = null,
        ICurrentTenant? currentTenant = null,
        ICurrentUser? currentUser = null,
        IProblemDetailsCreator? problemDetailsCreator = null,
        IClock? clock = null,
        ICancellationTokenProvider? cancellationTokenProvider = null,
        ILogger<IdempotencyMiddleware>? logger = null,
        IServiceProvider? serviceProvider = null
    )
    {
        if (options is null)
        {
            var snapshot = Substitute.For<IOptionsMonitor<IdempotencyOptions>>();
            snapshot.CurrentValue.Returns(new IdempotencyOptions());
            options = snapshot;
        }

        cache ??= Substitute.For<ICache>();

        // Default test identity: tenant "t1" + authenticated user "u1". A real user is the
        // default because IdempotencyOptions.RequireUserIdentity defaults to true — anonymous
        // tenant-only requests fall through without applying idempotency. Tests exercising the
        // "no user" or "no tenant" branches pass explicit substitutes returning null.
        if (currentTenant is null)
        {
            currentTenant = Substitute.For<ICurrentTenant>();
            currentTenant.Id.Returns("t1");
        }

        if (currentUser is null)
        {
            currentUser = Substitute.For<ICurrentUser>();
            currentUser.UserId.Returns(new UserId("u1"));
        }

        problemDetailsCreator ??= Substitute.For<IProblemDetailsCreator>();

        if (clock is null)
        {
            clock = Substitute.For<IClock>();
            clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        }

        if (cancellationTokenProvider is null)
        {
            cancellationTokenProvider = Substitute.For<ICancellationTokenProvider>();
            cancellationTokenProvider.Token.Returns(CancellationToken.None);
        }

        logger ??= LoggerFactory.CreateLogger<IdempotencyMiddleware>();
        serviceProvider ??= new ServiceCollection().AddLogging().BuildServiceProvider();

        return new IdempotencyMiddleware(
            options,
            cache,
            currentTenant,
            currentUser,
            problemDetailsCreator,
            clock,
            cancellationTokenProvider,
            logger,
            serviceProvider
        );
    }

    protected static DefaultHttpContext CreateContext(
        string? idempotencyKey = null,
        string method = "POST",
        string path = "/v1/test",
        byte[]? body = null
    )
    {
        var ctx = new DefaultHttpContext();
        ctx.RequestServices = new ServiceCollection().AddLogging().AddProblemDetails().BuildServiceProvider();
        ctx.Request.Method = method;
        ctx.Request.Path = path;
        ctx.Request.Body = body is { Length: > 0 } ? new MemoryStream(body) : new MemoryStream();
        ctx.Response.Body = new MemoryStream();

        if (idempotencyKey is not null)
        {
            ctx.Request.Headers.Append(HttpHeaderNames.IdempotencyKey, idempotencyKey);
        }

        return ctx;
    }
}
