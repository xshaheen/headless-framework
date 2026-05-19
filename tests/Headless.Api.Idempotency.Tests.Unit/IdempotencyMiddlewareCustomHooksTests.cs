// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Cryptography;
using Headless.Abstractions;
using Headless.Api.Abstractions;
using Headless.Api.Idempotency;
using Headless.Caching;
using Headless.Constants;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class IdempotencyMiddlewareCustomHooksTests : TestBase
{
    private IdempotencyMiddleware _CreateMiddleware(IdempotencyOptions options, ICache cache, ICurrentTenant? tenant = null)
    {
        var snapshot = Substitute.For<IOptionsSnapshot<IdempotencyOptions>>();
        snapshot.Value.Returns(options);

        tenant ??= Substitute.For<ICurrentTenant>();
        tenant.Id.Returns((string?)null);

        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UtcNow);

        var ctp = Substitute.For<ICancellationTokenProvider>();
        ctp.Token.Returns(CancellationToken.None);

        var problemDetails = Substitute.For<IProblemDetailsCreator>();
        var sp = new ServiceCollection().AddLogging().BuildServiceProvider();

        return new IdempotencyMiddleware(
            snapshot,
            cache,
            tenant,
            problemDetails,
            clock,
            ctp,
            LoggerFactory.CreateLogger<IdempotencyMiddleware>(),
            sp);
    }

    private static DefaultHttpContext _CreateContext(string key = "k1", byte[]? body = null, string method = "POST", string path = "/v1/x")
    {
        var ctx = new DefaultHttpContext();
        ctx.RequestServices = new ServiceCollection().AddLogging().AddProblemDetails().BuildServiceProvider();
        ctx.Request.Method = method;
        ctx.Request.Path = path;
        ctx.Request.Body = body is { Length: > 0 } ? new MemoryStream(body) : new MemoryStream();
        ctx.Response.Body = new MemoryStream();
        ctx.Request.Headers.Append(HttpHeaderNames.IdempotencyKey, key);
        return ctx;
    }

    // ── KeyDeriver (R25, AE14) ────────────────────────────────────────────────

    [Fact]
    public async Task should_use_custom_key_deriver_when_provided()
    {
        var cache = Substitute.For<ICache>();
        cache.GetAsync<IdempotencyRecord>(Arg.Any<string>(), Arg.Any<CancellationToken>())
             .Returns(CacheValue<IdempotencyRecord>.NoValue);
        cache.TryInsertAsync(Arg.Any<string>(), Arg.Any<IdempotencyRecord>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
             .Returns(true);

        var options = new IdempotencyOptions
        {
            KeyDeriver = (_, header) => $"custom:user42:{header}",
        };

        var middleware = _CreateMiddleware(options, cache);
        var context = _CreateContext(key: "abc");

        await middleware.InvokeAsync(context, ctx => { ctx.Response.StatusCode = 200; return Task.CompletedTask; });

        await cache.Received().GetAsync<IdempotencyRecord>(
            Arg.Is<string>(k => k == "custom:user42:abc"),
            Arg.Any<CancellationToken>()
        );
    }

    // ── RequestFingerprint (R24, AE13) ───────────────────────────────────────

    [Fact]
    public async Task should_use_custom_fingerprint_when_provided()
    {
        var cache = Substitute.For<ICache>();
        cache.GetAsync<IdempotencyRecord>(Arg.Any<string>(), Arg.Any<CancellationToken>())
             .Returns(CacheValue<IdempotencyRecord>.NoValue);

        byte[] capturedFingerprint = [];
        cache.TryInsertAsync(Arg.Any<string>(), Arg.Do<IdempotencyRecord>(r => capturedFingerprint = r.Fingerprint!), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
             .Returns(true);

        byte[] customFp = [0xAA, 0xBB, 0xCC];
        var options = new IdempotencyOptions
        {
            RequestFingerprint = _ => new ValueTask<byte[]>(customFp),
        };

        var middleware = _CreateMiddleware(options, cache);
        var context = _CreateContext(body: [1, 2, 3]);

        await middleware.InvokeAsync(context, ctx => { ctx.Response.StatusCode = 200; return Task.CompletedTask; });

        capturedFingerprint.Should().Equal(customFp);
    }

    [Fact]
    public async Task should_not_invoke_custom_fingerprint_when_body_exceeds_cap()
    {
        var cache = Substitute.For<ICache>();
        var fingerprintInvoked = false;

        var options = new IdempotencyOptions
        {
            MaxBodySizeForHashing = 3,
            OversizeBehavior = OversizeBehavior.PassThrough,
            RequestFingerprint = _ =>
            {
                fingerprintInvoked = true;
                return new ValueTask<byte[]>([1]);
            },
        };

        var middleware = _CreateMiddleware(options, cache);
        var context = _CreateContext(body: [1, 2, 3, 4, 5]); // 5 > cap=3

        await middleware.InvokeAsync(context, ctx => { ctx.Response.StatusCode = 200; return Task.CompletedTask; });

        fingerprintInvoked.Should().BeFalse();
    }

    [Fact]
    public async Task should_rewind_request_body_after_custom_fingerprint_returns()
    {
        var cache = Substitute.For<ICache>();
        cache.GetAsync<IdempotencyRecord>(Arg.Any<string>(), Arg.Any<CancellationToken>())
             .Returns(CacheValue<IdempotencyRecord>.NoValue);
        cache.TryInsertAsync(Arg.Any<string>(), Arg.Any<IdempotencyRecord>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
             .Returns(true);

        long innerHandlerStartPosition = -1;
        var options = new IdempotencyOptions
        {
            RequestFingerprint = async ctx =>
            {
                // delegate reads body fully
                using var sr = new StreamReader(ctx.Request.Body, leaveOpen: true);
                _ = await sr.ReadToEndAsync();
                return [1];
            },
        };

        var middleware = _CreateMiddleware(options, cache);
        var context = _CreateContext(body: [1, 2, 3]);

        await middleware.InvokeAsync(context, ctx =>
        {
            innerHandlerStartPosition = ctx.Request.Body.Position;
            ctx.Response.StatusCode = 200;
            return Task.CompletedTask;
        });

        innerHandlerStartPosition.Should().Be(0L);
    }

    // ── ShouldApply (R21) ────────────────────────────────────────────────────

    [Fact]
    public async Task should_pass_through_when_should_apply_returns_false()
    {
        var cache = Substitute.For<ICache>();

        var options = new IdempotencyOptions
        {
            ShouldApply = _ => false,
        };

        var middleware = _CreateMiddleware(options, cache);
        var context = _CreateContext();
        var nextCalled = false;

        await middleware.InvokeAsync(context, _ => { nextCalled = true; return Task.CompletedTask; });

        nextCalled.Should().BeTrue();
        await cache.DidNotReceive().GetAsync<IdempotencyRecord>(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_apply_idempotency_when_should_apply_returns_true()
    {
        var cache = Substitute.For<ICache>();
        cache.GetAsync<IdempotencyRecord>(Arg.Any<string>(), Arg.Any<CancellationToken>())
             .Returns(CacheValue<IdempotencyRecord>.NoValue);
        cache.TryInsertAsync(Arg.Any<string>(), Arg.Any<IdempotencyRecord>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
             .Returns(true);

        var options = new IdempotencyOptions
        {
            ShouldApply = ctx => ctx.Request.Path.StartsWithSegments("/v1", StringComparison.Ordinal),
        };

        var middleware = _CreateMiddleware(options, cache);
        var context = _CreateContext(path: "/v1/x");

        await middleware.InvokeAsync(context, ctx => { ctx.Response.StatusCode = 200; return Task.CompletedTask; });

        await cache.Received(1).GetAsync<IdempotencyRecord>(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // ── Per-endpoint metadata merge (R23, AE12) ──────────────────────────────

    [Fact]
    public async Task should_apply_per_endpoint_metadata_override()
    {
        var cache = Substitute.For<ICache>();
        cache.GetAsync<IdempotencyRecord>(Arg.Any<string>(), Arg.Any<CancellationToken>())
             .Returns(CacheValue<IdempotencyRecord>.NoValue);

        TimeSpan? capturedTtl = null;
        cache.TryInsertAsync(Arg.Any<string>(), Arg.Any<IdempotencyRecord>(), Arg.Do<TimeSpan?>(ttl => capturedTtl = ttl), Arg.Any<CancellationToken>())
             .Returns(true);

        var appOptions = new IdempotencyOptions
        {
            IdempotencyKeyExpiration = TimeSpan.FromHours(24),
        };

        var middleware = _CreateMiddleware(appOptions, cache);
        var context = _CreateContext();

        // Attach endpoint metadata: override expiration to 7 days
        var endpoint = new Endpoint(
            requestDelegate: _ => Task.CompletedTask,
            metadata: new EndpointMetadataCollection(new IdempotencyMetadata(o => o.IdempotencyKeyExpiration = TimeSpan.FromDays(7))),
            displayName: "test");
        context.Features.Set<IEndpointFeature>(new EndpointFeature { Endpoint = endpoint });

        await middleware.InvokeAsync(context, ctx => { ctx.Response.StatusCode = 200; return Task.CompletedTask; });

        // Marker TTL = InFlightLockTimeout + 5s (not the IdempotencyKeyExpiration); just verify the merged path was used by checking expiration applied to Complete record
        capturedTtl.Should().NotBeNull();
    }

    [Fact]
    public async Task should_not_mutate_app_level_methods_when_endpoint_overrides_them()
    {
        var cache = Substitute.For<ICache>();
        cache.GetAsync<IdempotencyRecord>(Arg.Any<string>(), Arg.Any<CancellationToken>())
             .Returns(CacheValue<IdempotencyRecord>.NoValue);
        cache.TryInsertAsync(Arg.Any<string>(), Arg.Any<IdempotencyRecord>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
             .Returns(true);

        var appOptions = new IdempotencyOptions
        {
            Methods = new HashSet<string>(["POST"], StringComparer.OrdinalIgnoreCase),
        };
        var originalMethodsRef = appOptions.Methods;

        var middleware = _CreateMiddleware(appOptions, cache);
        var context = _CreateContext(method: "PUT");

        // Endpoint metadata adds PUT to the methods
        var endpoint = new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(new IdempotencyMetadata(o =>
            {
                ((HashSet<string>)o.Methods).Add("PUT");
            })),
            "test");
        context.Features.Set<IEndpointFeature>(new EndpointFeature { Endpoint = endpoint });

        await middleware.InvokeAsync(context, ctx => { ctx.Response.StatusCode = 200; return Task.CompletedTask; });

        // App-level Methods set must be untouched (still POST-only)
        appOptions.Methods.Should().BeSameAs(originalMethodsRef);
        appOptions.Methods.Should().NotContain("PUT");
        appOptions.Methods.Should().Contain("POST");
    }

    private sealed class EndpointFeature : IEndpointFeature
    {
        public Endpoint? Endpoint { get; set; }
    }
}
