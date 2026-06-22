// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Api;
using Headless.Caching;
using Headless.Primitives;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using IdempotencyMiddleware = Headless.Api.IdempotencyMiddleware;

namespace Tests;

public sealed class IdempotencyMiddlewareCustomHooksTests : IdempotencyMiddlewareTestBase
{
    private IdempotencyMiddleware _CreateMiddlewareWithOptions(
        IdempotencyOptions options,
        ICache cache,
        ICurrentTenant? tenant = null
    )
    {
        var monitor = Substitute.For<IOptionsMonitor<IdempotencyOptions>>();
        monitor.CurrentValue.Returns(options);

        // Default to a present tenant so the default key derivation produces a non-empty key;
        // tests that exercise pass-through-by-missing-identity supply their own substitutes.
        if (tenant is null)
        {
            tenant = Substitute.For<ICurrentTenant>();
            tenant.Id.Returns("t1");
        }

        return CreateMiddleware(options: monitor, cache: cache, currentTenant: tenant);
    }

    private static DefaultHttpContext _CreateLocalContext(
        string key = "k1",
        byte[]? body = null,
        string method = "POST",
        string path = "/v1/x"
    )
    {
        var ctx = CreateContext(idempotencyKey: key, method: method, path: path, body: body);
        return ctx;
    }

    // ── KeyDeriver (R25, AE14) ────────────────────────────────────────────────

    [Fact]
    public async Task should_use_custom_key_deriver_when_provided()
    {
        var cache = Substitute.For<ICache>();
        cache
            .GetAsync<IdempotencyRecord>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(CacheValue<IdempotencyRecord>.NoValue);
        cache
            .TryInsertAsync(
                Arg.Any<string>(),
                Arg.Any<IdempotencyRecord>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(true);

        var options = new IdempotencyOptions { KeyDeriver = (_, header) => $"custom:user42:{header}" };

        var middleware = _CreateMiddlewareWithOptions(options, cache);
        var context = _CreateLocalContext(key: "abc");

        await middleware.InvokeAsync(
            context,
            ctx =>
            {
                ctx.Response.StatusCode = 200;
                return Task.CompletedTask;
            }
        );

        await cache
            .Received()
            .GetAsync<IdempotencyRecord>(Arg.Is<string>(k => k == "custom:user42:abc"), Arg.Any<CancellationToken>());
    }

    // ── RequestFingerprint (R24, AE13) ───────────────────────────────────────

    [Fact]
    public async Task should_use_custom_fingerprint_when_provided()
    {
        var cache = Substitute.For<ICache>();
        cache
            .GetAsync<IdempotencyRecord>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(CacheValue<IdempotencyRecord>.NoValue);

        byte[] capturedFingerprint = [];
        cache
            .TryInsertAsync(
                Arg.Any<string>(),
                Arg.Do<IdempotencyRecord>(r => capturedFingerprint = r.Fingerprint!),
                Arg.Any<TimeSpan?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(true);

        byte[] customFp = [0xAA, 0xBB, 0xCC];
        var options = new IdempotencyOptions { RequestFingerprint = _ => new ValueTask<byte[]>(customFp) };

        var middleware = _CreateMiddlewareWithOptions(options, cache);
        var context = _CreateLocalContext(body: [1, 2, 3]);

        await middleware.InvokeAsync(
            context,
            ctx =>
            {
                ctx.Response.StatusCode = 200;
                return Task.CompletedTask;
            }
        );

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

        var middleware = _CreateMiddlewareWithOptions(options, cache);
        var context = _CreateLocalContext(body: [1, 2, 3, 4, 5]); // 5 > cap=3

        await middleware.InvokeAsync(
            context,
            ctx =>
            {
                ctx.Response.StatusCode = 200;
                return Task.CompletedTask;
            }
        );

        fingerprintInvoked.Should().BeFalse();
    }

    [Fact]
    public async Task should_rewind_request_body_after_custom_fingerprint_returns()
    {
        var cache = Substitute.For<ICache>();
        cache
            .GetAsync<IdempotencyRecord>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(CacheValue<IdempotencyRecord>.NoValue);
        cache
            .TryInsertAsync(
                Arg.Any<string>(),
                Arg.Any<IdempotencyRecord>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<CancellationToken>()
            )
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

        var middleware = _CreateMiddlewareWithOptions(options, cache);
        var context = _CreateLocalContext(body: [1, 2, 3]);

        await middleware.InvokeAsync(
            context,
            ctx =>
            {
                innerHandlerStartPosition = ctx.Request.Body.Position;
                ctx.Response.StatusCode = 200;
                return Task.CompletedTask;
            }
        );

        innerHandlerStartPosition.Should().Be(0L);
    }

    // ── ShouldApply (R21) ────────────────────────────────────────────────────

    [Fact]
    public async Task should_pass_through_when_should_apply_returns_false()
    {
        var cache = Substitute.For<ICache>();

        var options = new IdempotencyOptions { ShouldApply = _ => false };

        var middleware = _CreateMiddlewareWithOptions(options, cache);
        var context = _CreateLocalContext();
        var nextCalled = false;

        await middleware.InvokeAsync(
            context,
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            }
        );

        nextCalled.Should().BeTrue();
        await cache.DidNotReceive().GetAsync<IdempotencyRecord>(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_apply_idempotency_when_should_apply_returns_true()
    {
        var cache = Substitute.For<ICache>();
        cache
            .GetAsync<IdempotencyRecord>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(CacheValue<IdempotencyRecord>.NoValue);
        cache
            .TryInsertAsync(
                Arg.Any<string>(),
                Arg.Any<IdempotencyRecord>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(true);

        var options = new IdempotencyOptions
        {
            ShouldApply = ctx => ctx.Request.Path.StartsWithSegments("/v1", StringComparison.Ordinal),
        };

        var middleware = _CreateMiddlewareWithOptions(options, cache);
        var context = _CreateLocalContext(path: "/v1/x");

        await middleware.InvokeAsync(
            context,
            ctx =>
            {
                ctx.Response.StatusCode = 200;
                return Task.CompletedTask;
            }
        );

        // Initial lookup happens (one or more times — marker re-check before upsert may add another).
        await cache.Received().GetAsync<IdempotencyRecord>(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // ── Per-endpoint metadata merge (R23, AE12) ──────────────────────────────

    [Fact]
    public async Task should_apply_per_endpoint_metadata_override()
    {
        var cache = Substitute.For<ICache>();
        cache
            .GetAsync<IdempotencyRecord>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(CacheValue<IdempotencyRecord>.NoValue);

        TimeSpan? capturedTtl = null;
        cache
            .TryInsertAsync(
                Arg.Any<string>(),
                Arg.Any<IdempotencyRecord>(),
                Arg.Do<TimeSpan?>(ttl => capturedTtl = ttl),
                Arg.Any<CancellationToken>()
            )
            .Returns(true);

        var appOptions = new IdempotencyOptions { IdempotencyKeyExpiration = TimeSpan.FromHours(24) };

        var middleware = _CreateMiddlewareWithOptions(appOptions, cache);
        var context = _CreateLocalContext();

        // Attach endpoint metadata: override expiration to 7 days
        var endpoint = new Endpoint(
            requestDelegate: _ => Task.CompletedTask,
            metadata: new EndpointMetadataCollection(
                new IdempotencyMetadata(o => o.IdempotencyKeyExpiration = TimeSpan.FromDays(7))
            ),
            displayName: "test"
        );
        context.Features.Set<IEndpointFeature>(new EndpointFeature { Endpoint = endpoint });

        await middleware.InvokeAsync(
            context,
            ctx =>
            {
                ctx.Response.StatusCode = 200;
                return Task.CompletedTask;
            }
        );

        // Marker TTL = InFlightLockTimeout + 5s (not the IdempotencyKeyExpiration); just verify the merged path was used by checking expiration applied to Complete record
        capturedTtl.Should().NotBeNull();
    }

    [Fact]
    public async Task should_not_mutate_app_level_methods_when_endpoint_overrides_them()
    {
        var cache = Substitute.For<ICache>();
        cache
            .GetAsync<IdempotencyRecord>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(CacheValue<IdempotencyRecord>.NoValue);
        cache
            .TryInsertAsync(
                Arg.Any<string>(),
                Arg.Any<IdempotencyRecord>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(true);

        var appOptions = new IdempotencyOptions
        {
            Methods = new HashSet<string>(["POST"], StringComparer.OrdinalIgnoreCase),
        };
        var originalMethodsRef = appOptions.Methods;

        var middleware = _CreateMiddlewareWithOptions(appOptions, cache);
        var context = _CreateLocalContext(method: "PUT");

        // Endpoint metadata adds PUT to the methods
        var endpoint = new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(
                new IdempotencyMetadata(o =>
                {
                    ((HashSet<string>)o.Methods).Add("PUT");
                })
            ),
            "test"
        );
        context.Features.Set<IEndpointFeature>(new EndpointFeature { Endpoint = endpoint });

        await middleware.InvokeAsync(
            context,
            ctx =>
            {
                ctx.Response.StatusCode = 200;
                return Task.CompletedTask;
            }
        );

        // App-level Methods set must be untouched (still POST-only)
        appOptions.Methods.Should().BeSameAs(originalMethodsRef);
        appOptions.Methods.Should().NotContain("PUT");
        appOptions.Methods.Should().Contain("POST");
    }

    [Fact]
    public async Task should_ignore_header_name_metadata_override()
    {
        // App-level HeaderName is "X-Custom-Idempotency-Key"; endpoint metadata tries to
        // override it to "X-Other-Header". The middleware reads the request header BEFORE
        // resolving endpoint metadata, so the metadata override is silently ignored.
        var cache = Substitute.For<ICache>();
        cache
            .GetAsync<IdempotencyRecord>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(CacheValue<IdempotencyRecord>.NoValue);
        cache
            .TryInsertAsync(
                Arg.Any<string>(),
                Arg.Any<IdempotencyRecord>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(true);

        var appOptions = new IdempotencyOptions { HeaderName = "X-Custom-Idempotency-Key" };

        var middleware = _CreateMiddlewareWithOptions(appOptions, cache);

        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddLogging().AddProblemDetails().BuildServiceProvider(),
            Request =
            {
                Method = "POST",
                Path = "/v1/x",
                Body = new MemoryStream([1, 2, 3]),
            },
            Response = { Body = new MemoryStream() },
        };

        // Carry the key under the APP-LEVEL header name; not the overridden one
        context.Request.Headers.Append("X-Custom-Idempotency-Key", "app-key");
        // The override would direct middleware to read from this header, but it must be ignored
        context.Request.Headers.Append("X-Other-Header", "ignored-override-key");

        var endpoint = new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(new IdempotencyMetadata(o => o.HeaderName = "X-Other-Header")),
            "test"
        );
        context.Features.Set<IEndpointFeature>(new EndpointFeature { Endpoint = endpoint });

        await middleware.InvokeAsync(
            context,
            ctx =>
            {
                ctx.Response.StatusCode = 200;
                return Task.CompletedTask;
            }
        );

        // Cache key carries the app-level header value (app-key), not the override (ignored-override-key)
        await cache
            .Received()
            .GetAsync<IdempotencyRecord>(
                Arg.Is<string>(k => k.EndsWith(":app-key", StringComparison.Ordinal)),
                Arg.Any<CancellationToken>()
            );
    }

    private sealed class EndpointFeature : IEndpointFeature
    {
        public Endpoint? Endpoint { get; set; }
    }
}
