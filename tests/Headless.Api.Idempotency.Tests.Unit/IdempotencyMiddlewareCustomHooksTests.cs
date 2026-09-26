// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api.Idempotency;
using Headless.Idempotency;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using IdempotencyMiddleware = Headless.Api.Idempotency.IdempotencyMiddleware;

namespace Tests;

public sealed class IdempotencyMiddlewareCustomHooksTests : IdempotencyMiddlewareTestBase
{
    private IdempotencyMiddleware _CreateMiddlewareWithOptions(
        IdempotencyOptions options,
        IIdempotentOperations operations
    )
    {
        return CreateMiddleware(options: Monitor(options), operations: operations);
    }

    private static DefaultHttpContext _CreateLocalContext(
        string key = "k1",
        byte[]? body = null,
        string method = "POST",
        string path = "/v1/x"
    )
    {
        return CreateContext(idempotencyKey: key, method: method, path: path, body: body);
    }

    // ── KeyDeriver ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task should_use_custom_key_deriver_when_provided()
    {
        var operations = CreateAdmittingOperations();
        var options = new IdempotencyOptions { KeyDeriver = (_, header) => $"custom:user42:{header}" };
        var middleware = _CreateMiddlewareWithOptions(options, operations);
        var context = _CreateLocalContext(key: "abc");

        await middleware.InvokeAsync(
            context,
            ctx =>
            {
                ctx.Response.StatusCode = 200;
                return Task.CompletedTask;
            }
        );

        var expectedKey = IdempotencyMiddleware.HashScope("custom:user42:abc");
        AdmittedKeys(operations).Should().ContainSingle().Which.Should().Be(expectedKey);
    }

    // ── RequestFingerprint ───────────────────────────────────────────────────

    [Fact]
    public async Task should_use_custom_fingerprint_when_provided()
    {
        var operations = CreateAdmittingOperations();
        byte[] customFp = [0xAA, 0xBB, 0xCC];
        var options = new IdempotencyOptions { RequestFingerprint = _ => new ValueTask<byte[]>(customFp) };
        var middleware = _CreateMiddlewareWithOptions(options, operations);
        var context = _CreateLocalContext(body: [1, 2, 3]);

        await middleware.InvokeAsync(
            context,
            ctx =>
            {
                ctx.Response.StatusCode = 200;
                return Task.CompletedTask;
            }
        );

        await operations
            .Received(1)
            .AdmitAsync(
                Arg.Any<string>(),
                Arg.Is(IdempotencyFingerprint.Compute(customFp)),
                Arg.Any<string?>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_not_invoke_custom_fingerprint_when_body_exceeds_cap()
    {
        var operations = CreateAdmittingOperations();
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

        var middleware = _CreateMiddlewareWithOptions(options, operations);
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
        operations.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task should_rewind_request_body_after_custom_fingerprint_returns()
    {
        var operations = CreateAdmittingOperations();

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

        var middleware = _CreateMiddlewareWithOptions(options, operations);
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

    // ── ShouldApply ────────────────────────────────────────────────────────────

    [Fact]
    public async Task should_pass_through_when_should_apply_returns_false()
    {
        var operations = CreateAdmittingOperations();
        var options = new IdempotencyOptions { ShouldApply = _ => false };
        var middleware = _CreateMiddlewareWithOptions(options, operations);
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
        operations.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task should_apply_idempotency_when_should_apply_returns_true()
    {
        var operations = CreateAdmittingOperations();
        var options = new IdempotencyOptions
        {
            ShouldApply = ctx => ctx.Request.Path.StartsWithSegments("/v1", StringComparison.Ordinal),
        };
        var middleware = _CreateMiddlewareWithOptions(options, operations);
        var context = _CreateLocalContext(path: "/v1/x");

        await middleware.InvokeAsync(
            context,
            ctx =>
            {
                ctx.Response.StatusCode = 200;
                return Task.CompletedTask;
            }
        );

        AdmittedKeys(operations).Should().ContainSingle();
    }

    // ── Per-endpoint metadata merge ────────────────────────────────────────────

    [Fact]
    public async Task should_apply_per_endpoint_metadata_override()
    {
        var operations = CreateAdmittingOperations();
        var appOptions = new IdempotencyOptions { Retention = TimeSpan.FromHours(24) };
        var middleware = _CreateMiddlewareWithOptions(appOptions, operations);
        var context = _CreateLocalContext();

        // Attach endpoint metadata: override retention to 7 days
        var endpoint = new Endpoint(
            requestDelegate: _ => Task.CompletedTask,
            metadata: new EndpointMetadataCollection(new IdempotencyMetadata(o => o.Retention = TimeSpan.FromDays(7))),
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

        await operations
            .Received(1)
            .AdmitAsync(
                Arg.Any<string>(),
                Arg.Any<IdempotencyFingerprint>(),
                Arg.Any<string?>(),
                Arg.Any<TimeSpan?>(),
                Arg.Is<TimeSpan?>(TimeSpan.FromDays(7)),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_not_mutate_app_level_methods_when_endpoint_overrides_them()
    {
        var operations = CreateAdmittingOperations();
        var appOptions = new IdempotencyOptions
        {
            Methods = new HashSet<string>(["POST"], StringComparer.OrdinalIgnoreCase),
        };
        var originalMethodsRef = appOptions.Methods;

        var middleware = _CreateMiddlewareWithOptions(appOptions, operations);
        var context = _CreateLocalContext(method: "PUT");

        // Endpoint metadata adds PUT to the methods
        var endpoint = new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(new IdempotencyMetadata(o => ((HashSet<string>)o.Methods).Add("PUT"))),
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
        var operations = CreateAdmittingOperations();
        var appOptions = new IdempotencyOptions { HeaderName = "X-Custom-Idempotency-Key" };
        var middleware = _CreateMiddlewareWithOptions(appOptions, operations);

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

        // The admitted key carries the app-level header value (app-key), not the override (ignored-override-key).
        var expectedKey = IdempotencyMiddleware.HashScope("idem:u1:POST:/v1/x:app-key");
        var ignoredKey = IdempotencyMiddleware.HashScope("idem:u1:POST:/v1/x:ignored-override-key");
        var admittedKeys = AdmittedKeys(operations);
        admittedKeys.Should().ContainSingle().Which.Should().Be(expectedKey);
        admittedKeys.Should().NotContain(ignoredKey);
    }

    private sealed class EndpointFeature : IEndpointFeature
    {
        public Endpoint? Endpoint { get; set; }
    }
}
