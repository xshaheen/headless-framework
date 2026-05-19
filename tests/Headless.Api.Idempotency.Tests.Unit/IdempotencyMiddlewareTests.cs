// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Cryptography;
using Headless.Abstractions;
using Headless.Api.Abstractions;
using Headless.Api.Idempotency;
using Headless.Caching;
using Headless.Constants;
using Headless.DistributedLocks;
using Headless.Primitives;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class IdempotencyMiddlewareTests : TestBase
{
    private IdempotencyMiddleware _CreateMiddleware(
        IOptionsSnapshot<IdempotencyOptions>? options = null,
        ICache? cache = null,
        ICurrentTenant? currentTenant = null,
        IProblemDetailsCreator? problemDetailsCreator = null,
        IClock? clock = null,
        ICancellationTokenProvider? cancellationTokenProvider = null,
        ILogger<IdempotencyMiddleware>? logger = null,
        IServiceProvider? serviceProvider = null
    )
    {
        if (options is null)
        {
            var snapshot = Substitute.For<IOptionsSnapshot<IdempotencyOptions>>();
            snapshot.Value.Returns(new IdempotencyOptions());
            options = snapshot;
        }

        cache ??= Substitute.For<ICache>();

        if (currentTenant is null)
        {
            currentTenant = Substitute.For<ICurrentTenant>();
            currentTenant.Id.Returns((string?)null);
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

        return new IdempotencyMiddleware(options, cache, currentTenant, problemDetailsCreator, clock, cancellationTokenProvider, logger, serviceProvider);
    }

    private static DefaultHttpContext _CreateContext(
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

    // ── pass-through ──────────────────────────────────────────────────────────

    [Fact]
    public async Task should_pass_through_when_idempotency_key_header_is_missing()
    {
        var cache = Substitute.For<ICache>();
        var middleware = _CreateMiddleware(cache: cache);
        var context = _CreateContext(); // no header

        var nextCalled = false;
        await middleware.InvokeAsync(context, _ => { nextCalled = true; return Task.CompletedTask; });

        nextCalled.Should().BeTrue();
        await cache.DidNotReceive().GetAsync<IdempotencyRecord>(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_pass_through_when_method_is_not_in_methods_set()
    {
        var cache = Substitute.For<ICache>();
        var middleware = _CreateMiddleware(cache: cache);
        var context = _CreateContext(idempotencyKey: "k1", method: "GET");

        var nextCalled = false;
        await middleware.InvokeAsync(context, _ => { nextCalled = true; return Task.CompletedTask; });

        nextCalled.Should().BeTrue();
        await cache.DidNotReceive().GetAsync<IdempotencyRecord>(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task should_pass_through_when_key_is_whitespace_or_empty(string key)
    {
        var cache = Substitute.For<ICache>();
        var middleware = _CreateMiddleware(cache: cache);
        var context = _CreateContext(idempotencyKey: key);

        var nextCalled = false;
        await middleware.InvokeAsync(context, _ => { nextCalled = true; return Task.CompletedTask; });

        nextCalled.Should().BeTrue();
        await cache.DidNotReceive().GetAsync<IdempotencyRecord>(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // ── replay (AE1) ─────────────────────────────────────────────────────────

    [Fact]
    public async Task should_replay_cached_response_when_fingerprint_matches()
    {
        // given — a cached Complete record whose fingerprint matches the incoming body
        byte[] body = [1, 2, 3];
        byte[] fingerprint = SHA256.HashData(body);
        byte[] storedBody = [10, 20, 30];

        var record = new IdempotencyRecord
        {
            Kind = RecordKind.Complete,
            StatusCode = 201,
            Headers = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["Content-Type"] = ["application/json"],
            },
            Body = storedBody,
            Fingerprint = fingerprint,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var cache = Substitute.For<ICache>();
        cache.GetAsync<IdempotencyRecord>(Arg.Any<string>(), Arg.Any<CancellationToken>())
             .Returns(new CacheValue<IdempotencyRecord>(record, hasValue: true));

        var tenant = Substitute.For<ICurrentTenant>();
        tenant.Id.Returns("t1");

        var middleware = _CreateMiddleware(cache: cache, currentTenant: tenant);
        var context = _CreateContext(idempotencyKey: "k1", body: body);
        var nextCalled = false;

        // when
        await middleware.InvokeAsync(context, _ => { nextCalled = true; return Task.CompletedTask; });

        // then
        nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be(201);
        context.Response.Headers[HttpHeaderNames.IdempotentReplayed].ToString().Should().Be("true");

        context.Response.Body.Position = 0;
        var responseBytes = new byte[context.Response.Body.Length];
        _ = await context.Response.Body.ReadAsync(responseBytes);
        responseBytes.Should().Equal(storedBody);
    }

    [Fact]
    public async Task should_not_call_next_on_replay()
    {
        byte[] body = [1, 2, 3];
        byte[] fingerprint = SHA256.HashData(body);

        var record = new IdempotencyRecord
        {
            Kind = RecordKind.Complete,
            StatusCode = 200,
            Body = [9],
            Fingerprint = fingerprint,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var cache = Substitute.For<ICache>();
        cache.GetAsync<IdempotencyRecord>(Arg.Any<string>(), Arg.Any<CancellationToken>())
             .Returns(new CacheValue<IdempotencyRecord>(record, hasValue: true));

        var middleware = _CreateMiddleware(cache: cache);
        var context = _CreateContext(idempotencyKey: "k1", body: body);

        var nextCalled = false;
        await middleware.InvokeAsync(context, _ => { nextCalled = true; return Task.CompletedTask; });

        nextCalled.Should().BeFalse();
        await cache.DidNotReceive().TryInsertAsync(Arg.Any<string>(), Arg.Any<IdempotencyRecord>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>());
    }

    // ── cache key composition ─────────────────────────────────────────────────

    [Fact]
    public async Task should_use_empty_string_for_null_tenant_in_cache_key()
    {
        // null tenant → double-colon in key (idem::POST:/v1/x:k1)
        var cache = Substitute.For<ICache>();
        cache.GetAsync<IdempotencyRecord>(Arg.Any<string>(), Arg.Any<CancellationToken>())
             .Returns(CacheValue<IdempotencyRecord>.NoValue);
        cache.TryInsertAsync(Arg.Any<string>(), Arg.Any<IdempotencyRecord>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
             .Returns(true);

        var tenant = Substitute.For<ICurrentTenant>();
        tenant.Id.Returns((string?)null);

        var middleware = _CreateMiddleware(cache: cache, currentTenant: tenant);
        var context = _CreateContext(idempotencyKey: "k1", path: "/v1/x");

        await middleware.InvokeAsync(context, ctx => { ctx.Response.StatusCode = 200; return Task.CompletedTask; });

        await cache.Received().GetAsync<IdempotencyRecord>(
            Arg.Is<string>(k => k == "idem::POST:/v1/x:k1"),
            Arg.Any<CancellationToken>()
        );
    }

    [Fact]
    public async Task should_use_tenant_id_in_cache_key()
    {
        var cache = Substitute.For<ICache>();
        cache.GetAsync<IdempotencyRecord>(Arg.Any<string>(), Arg.Any<CancellationToken>())
             .Returns(CacheValue<IdempotencyRecord>.NoValue);
        cache.TryInsertAsync(Arg.Any<string>(), Arg.Any<IdempotencyRecord>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
             .Returns(true);

        var tenant = Substitute.For<ICurrentTenant>();
        tenant.Id.Returns("T1");

        var middleware = _CreateMiddleware(cache: cache, currentTenant: tenant);
        var context = _CreateContext(idempotencyKey: "k1", path: "/v1/x");

        await middleware.InvokeAsync(context, ctx => { ctx.Response.StatusCode = 200; return Task.CompletedTask; });

        await cache.Received().GetAsync<IdempotencyRecord>(
            Arg.Is<string>(k => k == "idem:T1:POST:/v1/x:k1"),
            Arg.Any<CancellationToken>()
        );
    }

    // ── cache miss → execute + finalize ──────────────────────────────────────

    [Fact]
    public async Task should_execute_next_and_finalize_complete_record_on_cache_miss()
    {
        // given
        byte[] body = [5, 6, 7];
        byte[] fingerprint = SHA256.HashData(body);

        var cache = Substitute.For<ICache>();
        cache.GetAsync<IdempotencyRecord>(Arg.Any<string>(), Arg.Any<CancellationToken>())
             .Returns(CacheValue<IdempotencyRecord>.NoValue);
        cache.TryInsertAsync(Arg.Any<string>(), Arg.Any<IdempotencyRecord>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
             .Returns(true);

        var middleware = _CreateMiddleware(cache: cache);
        var context = _CreateContext(idempotencyKey: "k1", body: body);
        var nextCalled = false;

        // when
        await middleware.InvokeAsync(context, ctx =>
        {
            nextCalled = true;
            ctx.Response.StatusCode = 201;
            return ctx.Response.Body.WriteAsync(new byte[] { 100, 101, 102 }).AsTask();
        });

        // then
        nextCalled.Should().BeTrue();

        await cache.Received(1).TryInsertAsync(
            Arg.Any<string>(),
            Arg.Is<IdempotencyRecord>(r => r.Kind == RecordKind.InFlight),
            Arg.Any<TimeSpan?>(),
            Arg.Any<CancellationToken>()
        );

        await cache.Received(1).UpsertAsync(
            Arg.Any<string>(),
            Arg.Is<IdempotencyRecord>(r =>
                r.Kind == RecordKind.Complete &&
                r.StatusCode == 201 &&
                r.Fingerprint != null &&
                r.Fingerprint.SequenceEqual(fingerprint) &&
                r.Body.SequenceEqual(new byte[] { 100, 101, 102 })
            ),
            Arg.Any<TimeSpan?>(),
            Arg.Any<CancellationToken>()
        );
    }

    [Fact]
    public async Task should_remove_inflight_marker_when_next_throws()
    {
        var cache = Substitute.For<ICache>();
        cache.GetAsync<IdempotencyRecord>(Arg.Any<string>(), Arg.Any<CancellationToken>())
             .Returns(CacheValue<IdempotencyRecord>.NoValue);
        cache.TryInsertAsync(Arg.Any<string>(), Arg.Any<IdempotencyRecord>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
             .Returns(true);

        var middleware = _CreateMiddleware(cache: cache);
        var context = _CreateContext(idempotencyKey: "k1", body: [1, 2]);

        var act = () => middleware.InvokeAsync(context, _ => throw new InvalidOperationException("boom"));

        await act.Should().ThrowAsync<InvalidOperationException>();

        await cache.Received(1).RemoveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await cache.DidNotReceive().UpsertAsync(Arg.Any<string>(), Arg.Any<IdempotencyRecord>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>());
    }

    // ── mismatch (AE2) ───────────────────────────────────────────────────────

    [Fact]
    public async Task should_return_422_when_fingerprint_mismatches()
    {
        // given — cached Complete record whose fingerprint does NOT match the incoming body
        byte[] body = [1, 2, 3];
        byte[] differentFingerprint = SHA256.HashData([9, 9, 9]);

        var record = new IdempotencyRecord
        {
            Kind = RecordKind.Complete,
            StatusCode = 201,
            Body = [10],
            Fingerprint = differentFingerprint,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var cache = Substitute.For<ICache>();
        cache.GetAsync<IdempotencyRecord>(Arg.Any<string>(), Arg.Any<CancellationToken>())
             .Returns(new CacheValue<IdempotencyRecord>(record, hasValue: true));

        var problemDetailsCreator = Substitute.For<IProblemDetailsCreator>();
        problemDetailsCreator.UnprocessableEntity(Arg.Any<Dictionary<string, List<ErrorDescriptor>>>())
            .Returns(new ProblemDetails { Status = 422 });

        var middleware = _CreateMiddleware(cache: cache, problemDetailsCreator: problemDetailsCreator);
        var context = _CreateContext(idempotencyKey: "k1", body: body);
        var nextCalled = false;

        await middleware.InvokeAsync(context, _ => { nextCalled = true; return Task.CompletedTask; });

        nextCalled.Should().BeFalse();
        problemDetailsCreator.Received(1).UnprocessableEntity(
            Arg.Is<Dictionary<string, List<ErrorDescriptor>>>(d =>
                d.ContainsKey("idempotency_key") &&
                d["idempotency_key"].Any(e => e.Code == "g:idempotency-key-reused")
            )
        );
    }

    [Fact]
    public async Task should_return_409_when_mismatch_status_code_is_409()
    {
        byte[] body = [1, 2, 3];
        byte[] differentFingerprint = SHA256.HashData([9, 9, 9]);

        var record = new IdempotencyRecord
        {
            Kind = RecordKind.Complete,
            StatusCode = 201,
            Body = [10],
            Fingerprint = differentFingerprint,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var cache = Substitute.For<ICache>();
        cache.GetAsync<IdempotencyRecord>(Arg.Any<string>(), Arg.Any<CancellationToken>())
             .Returns(new CacheValue<IdempotencyRecord>(record, hasValue: true));

        var problemDetailsCreator = Substitute.For<IProblemDetailsCreator>();
        problemDetailsCreator.Conflict(Arg.Any<IReadOnlyCollection<ErrorDescriptor>>())
            .Returns(new ProblemDetails { Status = 409 });

        var options = Substitute.For<IOptionsSnapshot<IdempotencyOptions>>();
        options.Value.Returns(new IdempotencyOptions { MismatchStatusCode = 409 });

        var middleware = _CreateMiddleware(options: options, cache: cache, problemDetailsCreator: problemDetailsCreator);
        var context = _CreateContext(idempotencyKey: "k1", body: body);

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        problemDetailsCreator.Received(1).Conflict(
            Arg.Is<IReadOnlyCollection<ErrorDescriptor>>(es => es.Any(e => e.Code == "g:idempotency-key-reused"))
        );
        problemDetailsCreator.DidNotReceive().UnprocessableEntity(Arg.Any<Dictionary<string, List<ErrorDescriptor>>>());
    }

    // ── in-flight Reject (AE3) ────────────────────────────────────────────────

    [Fact]
    public async Task should_return_409_when_record_is_in_flight_and_strategy_is_reject()
    {
        var record = new IdempotencyRecord
        {
            Kind = RecordKind.InFlight,
            Fingerprint = SHA256.HashData([1, 2, 3]),
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var cache = Substitute.For<ICache>();
        cache.GetAsync<IdempotencyRecord>(Arg.Any<string>(), Arg.Any<CancellationToken>())
             .Returns(new CacheValue<IdempotencyRecord>(record, hasValue: true));

        var problemDetailsCreator = Substitute.For<IProblemDetailsCreator>();
        problemDetailsCreator.Conflict(Arg.Any<IReadOnlyCollection<ErrorDescriptor>>())
            .Returns(new ProblemDetails { Status = 409 });

        var middleware = _CreateMiddleware(cache: cache, problemDetailsCreator: problemDetailsCreator);
        var context = _CreateContext(idempotencyKey: "k1", body: [1, 2, 3]);
        var nextCalled = false;

        await middleware.InvokeAsync(context, _ => { nextCalled = true; return Task.CompletedTask; });

        nextCalled.Should().BeFalse();
        problemDetailsCreator.Received(1).Conflict(
            Arg.Is<IReadOnlyCollection<ErrorDescriptor>>(es => es.Any(e => e.Code == "g:idempotency-in-flight"))
        );
    }

    [Fact]
    public async Task should_return_409_on_race_loss_when_recheck_shows_inflight()
    {
        var inFlight = new IdempotencyRecord
        {
            Kind = RecordKind.InFlight,
            Fingerprint = SHA256.HashData([1, 2, 3]),
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var cache = Substitute.For<ICache>();
        cache.GetAsync<IdempotencyRecord>(Arg.Any<string>(), Arg.Any<CancellationToken>())
             .Returns(CacheValue<IdempotencyRecord>.NoValue, new CacheValue<IdempotencyRecord>(inFlight, hasValue: true));
        cache.TryInsertAsync(Arg.Any<string>(), Arg.Any<IdempotencyRecord>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
             .Returns(false);

        var problemDetailsCreator = Substitute.For<IProblemDetailsCreator>();
        problemDetailsCreator.Conflict(Arg.Any<IReadOnlyCollection<ErrorDescriptor>>())
            .Returns(new ProblemDetails { Status = 409 });

        var middleware = _CreateMiddleware(cache: cache, problemDetailsCreator: problemDetailsCreator);
        var context = _CreateContext(idempotencyKey: "k1", body: [1, 2, 3]);
        var nextCalled = false;

        await middleware.InvokeAsync(context, _ => { nextCalled = true; return Task.CompletedTask; });

        nextCalled.Should().BeFalse();
        problemDetailsCreator.Received(1).Conflict(
            Arg.Is<IReadOnlyCollection<ErrorDescriptor>>(es => es.Any(e => e.Code == "g:idempotency-in-flight"))
        );
    }

    [Fact]
    public async Task should_return_422_on_race_loss_when_recheck_shows_complete_with_mismatch()
    {
        byte[] body = [1, 2, 3];
        var complete = new IdempotencyRecord
        {
            Kind = RecordKind.Complete,
            StatusCode = 200,
            Body = [9],
            Fingerprint = SHA256.HashData([9, 9, 9]), // different fingerprint
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var cache = Substitute.For<ICache>();
        cache.GetAsync<IdempotencyRecord>(Arg.Any<string>(), Arg.Any<CancellationToken>())
             .Returns(CacheValue<IdempotencyRecord>.NoValue, new CacheValue<IdempotencyRecord>(complete, hasValue: true));
        cache.TryInsertAsync(Arg.Any<string>(), Arg.Any<IdempotencyRecord>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
             .Returns(false);

        var problemDetailsCreator = Substitute.For<IProblemDetailsCreator>();
        problemDetailsCreator.UnprocessableEntity(Arg.Any<Dictionary<string, List<ErrorDescriptor>>>())
            .Returns(new ProblemDetails { Status = 422 });

        var middleware = _CreateMiddleware(cache: cache, problemDetailsCreator: problemDetailsCreator);
        var context = _CreateContext(idempotencyKey: "k1", body: body);

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        problemDetailsCreator.Received(1).UnprocessableEntity(
            Arg.Is<Dictionary<string, List<ErrorDescriptor>>>(d =>
                d.ContainsKey("idempotency_key") &&
                d["idempotency_key"].Any(e => e.Code == "g:idempotency-key-reused")
            )
        );
    }

    // ── WaitAndReplay (AE4) ──────────────────────────────────────────────────

    private IdempotencyMiddleware _CreateMiddlewareWithLock(ICache cache, IDistributedLockProvider lockProvider, IProblemDetailsCreator? problemDetailsCreator = null)
    {
        var sp = new ServiceCollection()
            .AddLogging()
            .AddSingleton(lockProvider)
            .BuildServiceProvider();

        var options = Substitute.For<IOptionsSnapshot<IdempotencyOptions>>();
        options.Value.Returns(new IdempotencyOptions { InFlightStrategy = InFlightStrategy.WaitAndReplay });

        return _CreateMiddleware(options: options, cache: cache, problemDetailsCreator: problemDetailsCreator, serviceProvider: sp);
    }

    [Fact]
    public async Task should_replay_on_wait_and_replay_when_lock_acquired_and_record_completes()
    {
        byte[] body = [1, 2, 3];
        byte[] fingerprint = SHA256.HashData(body);
        byte[] storedBody = [10, 20, 30];

        var inFlight = new IdempotencyRecord { Kind = RecordKind.InFlight, Fingerprint = fingerprint, CreatedAt = DateTimeOffset.UtcNow };
        var complete = new IdempotencyRecord
        {
            Kind = RecordKind.Complete,
            StatusCode = 200,
            Body = storedBody,
            Fingerprint = fingerprint,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var cache = Substitute.For<ICache>();
        cache.GetAsync<IdempotencyRecord>(Arg.Any<string>(), Arg.Any<CancellationToken>())
             .Returns(
                 new CacheValue<IdempotencyRecord>(inFlight, hasValue: true),
                 new CacheValue<IdempotencyRecord>(complete, hasValue: true)
             );

        var dlock = Substitute.For<IDistributedLock>();
        var lockProvider = Substitute.For<IDistributedLockProvider>();
        lockProvider.TryAcquireAsync(Arg.Any<string>(), Arg.Any<TimeSpan?>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
                    .Returns(dlock);

        var middleware = _CreateMiddlewareWithLock(cache, lockProvider);
        var context = _CreateContext(idempotencyKey: "k1", body: body);
        var nextCalled = false;

        await middleware.InvokeAsync(context, _ => { nextCalled = true; return Task.CompletedTask; });

        nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be(200);
        context.Response.Headers[HttpHeaderNames.IdempotentReplayed].ToString().Should().Be("true");
    }

    [Fact]
    public async Task should_return_409_timeout_when_lock_acquisition_times_out()
    {
        var inFlight = new IdempotencyRecord { Kind = RecordKind.InFlight, Fingerprint = [1], CreatedAt = DateTimeOffset.UtcNow };

        var cache = Substitute.For<ICache>();
        cache.GetAsync<IdempotencyRecord>(Arg.Any<string>(), Arg.Any<CancellationToken>())
             .Returns(new CacheValue<IdempotencyRecord>(inFlight, hasValue: true));

        var lockProvider = Substitute.For<IDistributedLockProvider>();
        lockProvider.TryAcquireAsync(Arg.Any<string>(), Arg.Any<TimeSpan?>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
                    .Returns((IDistributedLock?)null);

        var problemDetailsCreator = Substitute.For<IProblemDetailsCreator>();
        problemDetailsCreator.Conflict(Arg.Any<IReadOnlyCollection<ErrorDescriptor>>())
            .Returns(new ProblemDetails { Status = 409 });

        var middleware = _CreateMiddlewareWithLock(cache, lockProvider, problemDetailsCreator);
        var context = _CreateContext(idempotencyKey: "k1", body: [1, 2, 3]);

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        problemDetailsCreator.Received(1).Conflict(
            Arg.Is<IReadOnlyCollection<ErrorDescriptor>>(es => es.Any(e => e.Code == "g:idempotency-in-flight-timeout"))
        );
    }

    [Fact]
    public async Task should_return_422_on_wait_and_replay_when_post_lock_record_has_mismatch()
    {
        byte[] body = [1, 2, 3];
        var inFlight = new IdempotencyRecord { Kind = RecordKind.InFlight, Fingerprint = SHA256.HashData(body), CreatedAt = DateTimeOffset.UtcNow };
        var complete = new IdempotencyRecord
        {
            Kind = RecordKind.Complete,
            StatusCode = 200,
            Body = [9],
            Fingerprint = SHA256.HashData([9, 9, 9]), // mismatch
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var cache = Substitute.For<ICache>();
        cache.GetAsync<IdempotencyRecord>(Arg.Any<string>(), Arg.Any<CancellationToken>())
             .Returns(
                 new CacheValue<IdempotencyRecord>(inFlight, hasValue: true),
                 new CacheValue<IdempotencyRecord>(complete, hasValue: true)
             );

        var dlock = Substitute.For<IDistributedLock>();
        var lockProvider = Substitute.For<IDistributedLockProvider>();
        lockProvider.TryAcquireAsync(Arg.Any<string>(), Arg.Any<TimeSpan?>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
                    .Returns(dlock);

        var problemDetailsCreator = Substitute.For<IProblemDetailsCreator>();
        problemDetailsCreator.UnprocessableEntity(Arg.Any<Dictionary<string, List<ErrorDescriptor>>>())
            .Returns(new ProblemDetails { Status = 422 });

        var middleware = _CreateMiddlewareWithLock(cache, lockProvider, problemDetailsCreator);
        var context = _CreateContext(idempotencyKey: "k1", body: body);

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        problemDetailsCreator.Received(1).UnprocessableEntity(
            Arg.Is<Dictionary<string, List<ErrorDescriptor>>>(d =>
                d.ContainsKey("idempotency_key") &&
                d["idempotency_key"].Any(e => e.Code == "g:idempotency-key-reused")
            )
        );
    }

    [Fact]
    public async Task should_return_409_timeout_on_wait_and_replay_when_post_lock_record_is_inflight()
    {
        var inFlight = new IdempotencyRecord { Kind = RecordKind.InFlight, Fingerprint = [1], CreatedAt = DateTimeOffset.UtcNow };

        var cache = Substitute.For<ICache>();
        cache.GetAsync<IdempotencyRecord>(Arg.Any<string>(), Arg.Any<CancellationToken>())
             .Returns(
                 new CacheValue<IdempotencyRecord>(inFlight, hasValue: true),
                 new CacheValue<IdempotencyRecord>(inFlight, hasValue: true) // still InFlight after lock
             );

        var dlock = Substitute.For<IDistributedLock>();
        var lockProvider = Substitute.For<IDistributedLockProvider>();
        lockProvider.TryAcquireAsync(Arg.Any<string>(), Arg.Any<TimeSpan?>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
                    .Returns(dlock);

        var problemDetailsCreator = Substitute.For<IProblemDetailsCreator>();
        problemDetailsCreator.Conflict(Arg.Any<IReadOnlyCollection<ErrorDescriptor>>())
            .Returns(new ProblemDetails { Status = 409 });

        var middleware = _CreateMiddlewareWithLock(cache, lockProvider, problemDetailsCreator);
        var context = _CreateContext(idempotencyKey: "k1", body: [1, 2, 3]);

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        problemDetailsCreator.Received(1).Conflict(
            Arg.Is<IReadOnlyCollection<ErrorDescriptor>>(es => es.Any(e => e.Code == "g:idempotency-in-flight-timeout"))
        );
    }

    [Fact]
    public async Task should_return_409_timeout_on_wait_and_replay_when_post_lock_record_is_missing()
    {
        var inFlight = new IdempotencyRecord { Kind = RecordKind.InFlight, Fingerprint = [1], CreatedAt = DateTimeOffset.UtcNow };

        var cache = Substitute.For<ICache>();
        cache.GetAsync<IdempotencyRecord>(Arg.Any<string>(), Arg.Any<CancellationToken>())
             .Returns(
                 new CacheValue<IdempotencyRecord>(inFlight, hasValue: true),
                 CacheValue<IdempotencyRecord>.NoValue // evicted after lock
             );

        var dlock = Substitute.For<IDistributedLock>();
        var lockProvider = Substitute.For<IDistributedLockProvider>();
        lockProvider.TryAcquireAsync(Arg.Any<string>(), Arg.Any<TimeSpan?>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
                    .Returns(dlock);

        var problemDetailsCreator = Substitute.For<IProblemDetailsCreator>();
        problemDetailsCreator.Conflict(Arg.Any<IReadOnlyCollection<ErrorDescriptor>>())
            .Returns(new ProblemDetails { Status = 409 });

        var middleware = _CreateMiddlewareWithLock(cache, lockProvider, problemDetailsCreator);
        var context = _CreateContext(idempotencyKey: "k1", body: [1, 2, 3]);

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        problemDetailsCreator.Received(1).Conflict(
            Arg.Is<IReadOnlyCollection<ErrorDescriptor>>(es => es.Any(e => e.Code == "g:idempotency-in-flight-timeout"))
        );
    }
}
