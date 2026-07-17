// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Cryptography;
using Headless.Abstractions;
using Headless.Api.Idempotency;
using Headless.Caching;
using Headless.Constants;
using Headless.DistributedLocks;
using Headless.Primitives;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using IdempotencyMiddleware = Headless.Api.Idempotency.IdempotencyMiddleware;

namespace Tests;

public sealed class IdempotencyMiddlewareTests : IdempotencyMiddlewareTestBase
{
    // ── pass-through ──────────────────────────────────────────────────────────

    [Fact]
    public async Task should_pass_through_when_idempotency_key_header_is_missing()
    {
        var cache = Substitute.For<ICache>();
        var middleware = CreateMiddleware(cache: cache);
        var context = CreateContext(); // no header

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
    public async Task should_pass_through_when_method_is_not_in_methods_set()
    {
        var cache = Substitute.For<ICache>();
        var middleware = CreateMiddleware(cache: cache);
        var context = CreateContext(idempotencyKey: "k1", method: "GET");

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

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task should_pass_through_when_key_is_whitespace_or_empty(string key)
    {
        var cache = Substitute.For<ICache>();
        var middleware = CreateMiddleware(cache: cache);
        var context = CreateContext(idempotencyKey: key);

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

    // ── replay (AE1) ─────────────────────────────────────────────────────────

    [Fact]
    public async Task should_replay_cached_response_when_fingerprint_matches()
    {
        // given — a cached Complete record whose fingerprint matches the incoming body
        byte[] body = [1, 2, 3];
        var fingerprint = SHA256.HashData(body);
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
        cache
            .GetAsync<IdempotencyRecord>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CacheValue<IdempotencyRecord>(record, hasValue: true));

        var tenant = Substitute.For<ICurrentTenant>();
        tenant.Id.Returns("t1");

        var middleware = CreateMiddleware(cache: cache, currentTenant: tenant);
        var context = CreateContext(idempotencyKey: "k1", body: body);
        var nextCalled = false;

        // when
        await middleware.InvokeAsync(
            context,
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            }
        );

        // then
        nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be(201);
        context.Response.Headers[HttpHeaderNames.IdempotentReplayed].ToString().Should().Be("true");

        context.Response.Body.Position = 0;
        var responseBytes = new byte[context.Response.Body.Length];
        _ = await context.Response.Body.ReadAsync(responseBytes, AbortToken);
        responseBytes.Should().Equal(storedBody);
    }

    [Fact]
    public async Task should_set_content_length_header_on_replay()
    {
        // Replay must set Content-Length = Body.Length so HTTP/1.1 clients that buffer on it
        // do not hang waiting for the content boundary.
        byte[] body = [1, 2, 3];
        var fingerprint = SHA256.HashData(body);
        byte[] storedBody = [10, 20, 30];

        var record = new IdempotencyRecord
        {
            Kind = RecordKind.Complete,
            StatusCode = 200,
            Body = storedBody,
            Fingerprint = fingerprint,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var cache = Substitute.For<ICache>();
        cache
            .GetAsync<IdempotencyRecord>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CacheValue<IdempotencyRecord>(record, hasValue: true));

        var middleware = CreateMiddleware(cache: cache);
        var context = CreateContext(idempotencyKey: "k1", body: body);

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        context.Response.ContentLength.Should().Be(storedBody.Length);
    }

    [Fact]
    public async Task should_not_call_next_on_replay()
    {
        byte[] body = [1, 2, 3];
        var fingerprint = SHA256.HashData(body);

        var record = new IdempotencyRecord
        {
            Kind = RecordKind.Complete,
            StatusCode = 200,
            Body = [9],
            Fingerprint = fingerprint,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var cache = Substitute.For<ICache>();
        cache
            .GetAsync<IdempotencyRecord>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CacheValue<IdempotencyRecord>(record, hasValue: true));

        var middleware = CreateMiddleware(cache: cache);
        var context = CreateContext(idempotencyKey: "k1", body: body);

        var nextCalled = false;
        await middleware.InvokeAsync(
            context,
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            }
        );

        nextCalled.Should().BeFalse();
        await cache
            .DidNotReceive()
            .TryInsertAsync(
                Arg.Any<string>(),
                Arg.Any<IdempotencyRecord>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<CancellationToken>()
            );
    }

    // ── cache key composition ─────────────────────────────────────────────────

    [Fact]
    public async Task should_pass_through_when_tenant_and_user_are_null()
    {
        // No tenant + no user + no KeyDeriver → middleware refuses to apply idempotency
        // (returning an empty sentinel key) rather than collapsing anonymous callers into
        // a single cache slot. Falls through to next.
        var cache = Substitute.For<ICache>();

        var tenant = Substitute.For<ICurrentTenant>();
        tenant.Id.Returns((string?)null);
        var user = Substitute.For<ICurrentUser>();
        user.UserId.Returns((UserId?)null);

        var middleware = CreateMiddleware(cache: cache, currentTenant: tenant, currentUser: user);
        var context = CreateContext(idempotencyKey: "k1", path: "/v1/x");
        var nextCalled = false;

        await middleware.InvokeAsync(
            context,
            ctx =>
            {
                nextCalled = true;
                ctx.Response.StatusCode = 200;
                return Task.CompletedTask;
            }
        );

        nextCalled.Should().BeTrue();
        await cache.DidNotReceive().GetAsync<IdempotencyRecord>(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await cache
            .DidNotReceive()
            .TryInsertAsync(
                Arg.Any<string>(),
                Arg.Any<IdempotencyRecord>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_use_tenant_and_user_in_cache_key()
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

        var tenant = Substitute.For<ICurrentTenant>();
        tenant.Id.Returns("T1");
        var user = Substitute.For<ICurrentUser>();
        user.UserId.Returns(new UserId("U7"));

        var middleware = CreateMiddleware(cache: cache, currentTenant: tenant, currentUser: user);
        var context = CreateContext(idempotencyKey: "k1", path: "/v1/x");

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
            .GetAsync<IdempotencyRecord>(
                Arg.Is<string>(k => k == "idem:T1:U7:POST:/v1/x:k1"),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_use_empty_user_segment_in_cache_key_when_only_tenant_is_present_and_require_user_identity_is_false()
    {
        // With RequireUserIdentity=false the middleware permits tenant-only anonymous traffic.
        // The user segment in the cache key is empty (rather than a literal "anon") so a real
        // UserId equal to "anon" cannot collide with the anonymous bucket.
        var opts = Substitute.For<IOptionsMonitor<IdempotencyOptions>>();
        opts.CurrentValue.Returns(new IdempotencyOptions { RequireUserIdentity = false });

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

        var tenant = Substitute.For<ICurrentTenant>();
        tenant.Id.Returns("T1");
        var user = Substitute.For<ICurrentUser>();
        user.UserId.Returns((UserId?)null);

        var middleware = CreateMiddleware(options: opts, cache: cache, currentTenant: tenant, currentUser: user);
        var context = CreateContext(idempotencyKey: "k1", path: "/v1/x");

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
            .GetAsync<IdempotencyRecord>(
                Arg.Is<string>(k => k == "idem:T1::POST:/v1/x:k1"),
                Arg.Any<CancellationToken>()
            );
    }

    // ── cache miss → execute + finalize ──────────────────────────────────────

    [Fact]
    public async Task should_execute_next_and_finalize_complete_record_on_cache_miss()
    {
        // given
        byte[] body = [5, 6, 7];
        var fingerprint = SHA256.HashData(body);

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
        cache
            .TryReplaceIfEqualAsync(
                Arg.Any<string>(),
                Arg.Any<IdempotencyRecord?>(),
                Arg.Any<IdempotencyRecord?>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(true);

        var middleware = CreateMiddleware(cache: cache);
        var context = CreateContext(idempotencyKey: "k1", body: body);
        var nextCalled = false;

        // when
        await middleware.InvokeAsync(
            context,
            ctx =>
            {
                nextCalled = true;
                ctx.Response.StatusCode = 201;
                return ctx.Response.Body.WriteAsync("def"u8.ToArray(), AbortToken).AsTask();
            }
        );

        // then
        nextCalled.Should().BeTrue();

        await cache
            .Received(1)
            .TryInsertAsync(
                Arg.Any<string>(),
                Arg.Is<IdempotencyRecord>(r => r.Kind == RecordKind.InFlight),
                Arg.Any<TimeSpan?>(),
                Arg.Any<CancellationToken>()
            );

        // Finalize uses TryReplaceIfEqualAsync (CAS) to swap the InFlight marker for the
        // Complete record in a single round trip. The `expected` argument is the marker we
        // inserted; the `value` argument is the Complete record built from the captured response.
        await cache
            .Received(1)
            .TryReplaceIfEqualAsync(
                Arg.Any<string>(),
                Arg.Is<IdempotencyRecord?>(r =>
                    r != null
                    && r.Kind == RecordKind.InFlight
                    && r.Fingerprint != null
                    && r.Fingerprint.SequenceEqual(fingerprint)
                ),
                Arg.Is<IdempotencyRecord?>(r =>
                    r != null
                    && r.Kind == RecordKind.Complete
                    && r.StatusCode == 201
                    && r.Fingerprint != null
                    && r.Fingerprint.SequenceEqual(fingerprint)
                    && r.Body.SequenceEqual(new byte[] { 100, 101, 102 })
                ),
                Arg.Any<TimeSpan?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_remove_inflight_marker_when_next_throws()
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

        var middleware = CreateMiddleware(cache: cache);
        var context = CreateContext(idempotencyKey: "k1", body: [1, 2]);

        var act = () => middleware.InvokeAsync(context, _ => throw new InvalidOperationException("boom"));

        await act.Should().ThrowAsync<InvalidOperationException>();

        await cache.Received(1).RemoveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await cache
            .DidNotReceive()
            .UpsertAsync(
                Arg.Any<string>(),
                Arg.Any<IdempotencyRecord>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<CancellationToken>()
            );
    }

    // ── mismatch (AE2) ───────────────────────────────────────────────────────

    [Fact]
    public async Task should_return_422_when_fingerprint_mismatches()
    {
        // given — cached Complete record whose fingerprint does NOT match the incoming body
        byte[] body = [1, 2, 3];
        var differentFingerprint = SHA256.HashData("\t\t\t"u8);

        var record = new IdempotencyRecord
        {
            Kind = RecordKind.Complete,
            StatusCode = 201,
            Body = [10],
            Fingerprint = differentFingerprint,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var cache = Substitute.For<ICache>();
        cache
            .GetAsync<IdempotencyRecord>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CacheValue<IdempotencyRecord>(record, hasValue: true));

        var problemDetailsCreator = Substitute.For<IProblemDetailsCreator>();
        problemDetailsCreator
            .UnprocessableEntity(Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<ErrorDescriptor>>>())
            .Returns(new ProblemDetails { Status = 422 });

        var middleware = CreateMiddleware(cache: cache, problemDetailsCreator: problemDetailsCreator);
        var context = CreateContext(idempotencyKey: "k1", body: body);
        var nextCalled = false;

        await middleware.InvokeAsync(
            context,
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            }
        );

        nextCalled.Should().BeFalse();
        problemDetailsCreator
            .Received(1)
            .UnprocessableEntity(
                Arg.Is<IReadOnlyDictionary<string, IReadOnlyList<ErrorDescriptor>>>(d =>
                    d.ContainsKey("idempotency_key")
                    && d["idempotency_key"].Any(e => e.Code == "g:idempotency_key_reused")
                )
            );
    }

    [Fact]
    public async Task should_return_409_when_mismatch_status_code_is_409()
    {
        byte[] body = [1, 2, 3];
        var differentFingerprint = SHA256.HashData("\t\t\t"u8);

        var record = new IdempotencyRecord
        {
            Kind = RecordKind.Complete,
            StatusCode = 201,
            Body = [10],
            Fingerprint = differentFingerprint,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var cache = Substitute.For<ICache>();
        cache
            .GetAsync<IdempotencyRecord>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CacheValue<IdempotencyRecord>(record, hasValue: true));

        var problemDetailsCreator = Substitute.For<IProblemDetailsCreator>();
        problemDetailsCreator
            .Conflict(Arg.Any<IReadOnlyCollection<ErrorDescriptor>>())
            .Returns(new ProblemDetails { Status = 409 });

        var options = Substitute.For<IOptionsMonitor<IdempotencyOptions>>();
        options.CurrentValue.Returns(new IdempotencyOptions { MismatchStatusCode = 409 });

        var middleware = CreateMiddleware(options: options, cache: cache, problemDetailsCreator: problemDetailsCreator);
        var context = CreateContext(idempotencyKey: "k1", body: body);

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        problemDetailsCreator
            .Received(1)
            .Conflict(
                Arg.Is<IReadOnlyCollection<ErrorDescriptor>>(es => es.Any(e => e.Code == "g:idempotency_key_reused"))
            );
        problemDetailsCreator
            .DidNotReceive()
            .UnprocessableEntity(Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<ErrorDescriptor>>>());
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
        cache
            .GetAsync<IdempotencyRecord>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CacheValue<IdempotencyRecord>(record, hasValue: true));

        var problemDetailsCreator = Substitute.For<IProblemDetailsCreator>();
        problemDetailsCreator
            .Conflict(Arg.Any<IReadOnlyCollection<ErrorDescriptor>>())
            .Returns(new ProblemDetails { Status = 409 });

        var middleware = CreateMiddleware(cache: cache, problemDetailsCreator: problemDetailsCreator);
        var context = CreateContext(idempotencyKey: "k1", body: [1, 2, 3]);
        var nextCalled = false;

        await middleware.InvokeAsync(
            context,
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            }
        );

        nextCalled.Should().BeFalse();
        problemDetailsCreator
            .Received(1)
            .Conflict(
                Arg.Is<IReadOnlyCollection<ErrorDescriptor>>(es => es.Any(e => e.Code == "g:idempotency_in_flight"))
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
        cache
            .GetAsync<IdempotencyRecord>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(
                CacheValue<IdempotencyRecord>.NoValue,
                new CacheValue<IdempotencyRecord>(inFlight, hasValue: true)
            );
        cache
            .TryInsertAsync(
                Arg.Any<string>(),
                Arg.Any<IdempotencyRecord>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(false);

        var problemDetailsCreator = Substitute.For<IProblemDetailsCreator>();
        problemDetailsCreator
            .Conflict(Arg.Any<IReadOnlyCollection<ErrorDescriptor>>())
            .Returns(new ProblemDetails { Status = 409 });

        var middleware = CreateMiddleware(cache: cache, problemDetailsCreator: problemDetailsCreator);
        var context = CreateContext(idempotencyKey: "k1", body: [1, 2, 3]);
        var nextCalled = false;

        await middleware.InvokeAsync(
            context,
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            }
        );

        nextCalled.Should().BeFalse();
        problemDetailsCreator
            .Received(1)
            .Conflict(
                Arg.Is<IReadOnlyCollection<ErrorDescriptor>>(es => es.Any(e => e.Code == "g:idempotency_in_flight"))
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
            Fingerprint = SHA256.HashData("\t\t\t"u8), // different fingerprint
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var cache = Substitute.For<ICache>();
        cache
            .GetAsync<IdempotencyRecord>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(
                CacheValue<IdempotencyRecord>.NoValue,
                new CacheValue<IdempotencyRecord>(complete, hasValue: true)
            );
        cache
            .TryInsertAsync(
                Arg.Any<string>(),
                Arg.Any<IdempotencyRecord>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(false);

        var problemDetailsCreator = Substitute.For<IProblemDetailsCreator>();
        problemDetailsCreator
            .UnprocessableEntity(Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<ErrorDescriptor>>>())
            .Returns(new ProblemDetails { Status = 422 });

        var middleware = CreateMiddleware(cache: cache, problemDetailsCreator: problemDetailsCreator);
        var context = CreateContext(idempotencyKey: "k1", body: body);

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        problemDetailsCreator
            .Received(1)
            .UnprocessableEntity(
                Arg.Is<IReadOnlyDictionary<string, IReadOnlyList<ErrorDescriptor>>>(d =>
                    d.ContainsKey("idempotency_key")
                    && d["idempotency_key"].Any(e => e.Code == "g:idempotency_key_reused")
                )
            );
    }

    // ── WaitAndReplay (AE4) ──────────────────────────────────────────────────

    private IdempotencyMiddleware _CreateMiddlewareWithLock(
        ICache cache,
        IDistributedLock lockProvider,
        IProblemDetailsCreator? problemDetailsCreator = null
    )
    {
        var sp = new ServiceCollection().AddLogging().AddSingleton(lockProvider).BuildServiceProvider();

        var options = Substitute.For<IOptionsMonitor<IdempotencyOptions>>();
        options.CurrentValue.Returns(new IdempotencyOptions { InFlightStrategy = InFlightStrategy.WaitAndReplay });

        return CreateMiddleware(
            options: options,
            cache: cache,
            problemDetailsCreator: problemDetailsCreator,
            serviceProvider: sp
        );
    }

    [Fact]
    public async Task should_acquire_lock_before_handler_under_wait_and_replay_winner_path()
    {
        // Winner path: TryInsert succeeds → middleware should acquire the lock BEFORE invoking
        // next so losers actually block on lock contention rather than racing on an unrelated mutex.
        byte[] body = [1, 2, 3];
        var marker = new IdempotencyRecord
        {
            Kind = RecordKind.InFlight,
            Fingerprint = SHA256.HashData(body),
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var cache = Substitute.For<ICache>();
        cache
            .GetAsync<IdempotencyRecord>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(CacheValue<IdempotencyRecord>.NoValue, new CacheValue<IdempotencyRecord>(marker, hasValue: true));
        cache
            .TryInsertAsync(
                Arg.Any<string>(),
                Arg.Any<IdempotencyRecord>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(true);

        var dlock = Substitute.For<IDistributedLease>();
        var lockProvider = Substitute.For<IDistributedLock>();
        var lockAcquired = false;
        lockProvider
            .TryAcquireAsync(Arg.Any<string>(), Arg.Any<DistributedLockAcquireOptions?>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                lockAcquired = true;
                return Task.FromResult<IDistributedLease?>(dlock);
            });

        var nextSawLock = false;
        var middleware = _CreateMiddlewareWithLock(cache, lockProvider);
        var context = CreateContext(idempotencyKey: "k1", body: body);

        await middleware.InvokeAsync(
            context,
            ctx =>
            {
                nextSawLock = lockAcquired;
                ctx.Response.StatusCode = 200;
                return Task.CompletedTask;
            }
        );

        nextSawLock.Should().BeTrue();
    }

    [Fact]
    public async Task should_replay_on_wait_and_replay_when_lock_acquired_and_record_completes()
    {
        byte[] body = [1, 2, 3];
        var fingerprint = SHA256.HashData(body);
        byte[] storedBody = [10, 20, 30];

        var inFlight = new IdempotencyRecord
        {
            Kind = RecordKind.InFlight,
            Fingerprint = fingerprint,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        var complete = new IdempotencyRecord
        {
            Kind = RecordKind.Complete,
            StatusCode = 200,
            Body = storedBody,
            Fingerprint = fingerprint,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var cache = Substitute.For<ICache>();
        cache
            .GetAsync<IdempotencyRecord>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(
                new CacheValue<IdempotencyRecord>(inFlight, hasValue: true),
                new CacheValue<IdempotencyRecord>(complete, hasValue: true)
            );

        var dlock = Substitute.For<IDistributedLease>();
        var lockProvider = Substitute.For<IDistributedLock>();
        lockProvider
            .TryAcquireAsync(Arg.Any<string>(), Arg.Any<DistributedLockAcquireOptions?>(), Arg.Any<CancellationToken>())
            .Returns(dlock);

        var middleware = _CreateMiddlewareWithLock(cache, lockProvider);
        var context = CreateContext(idempotencyKey: "k1", body: body);
        var nextCalled = false;

        await middleware.InvokeAsync(
            context,
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            }
        );

        nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be(200);
        context.Response.Headers[HttpHeaderNames.IdempotentReplayed].ToString().Should().Be("true");
    }

    [Fact]
    public async Task should_return_409_timeout_when_lock_acquisition_times_out()
    {
        byte[] body = [1, 2, 3];
        var inFlight = new IdempotencyRecord
        {
            Kind = RecordKind.InFlight,
            Fingerprint = SHA256.HashData(body),
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var cache = Substitute.For<ICache>();
        cache
            .GetAsync<IdempotencyRecord>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CacheValue<IdempotencyRecord>(inFlight, hasValue: true));

        var lockProvider = Substitute.For<IDistributedLock>();
        lockProvider
            .TryAcquireAsync(Arg.Any<string>(), Arg.Any<DistributedLockAcquireOptions?>(), Arg.Any<CancellationToken>())
            .Returns((IDistributedLease?)null);

        var problemDetailsCreator = Substitute.For<IProblemDetailsCreator>();
        problemDetailsCreator
            .Conflict(Arg.Any<IReadOnlyCollection<ErrorDescriptor>>())
            .Returns(new ProblemDetails { Status = 409 });

        var middleware = _CreateMiddlewareWithLock(cache, lockProvider, problemDetailsCreator);
        var context = CreateContext(idempotencyKey: "k1", body: body);

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        problemDetailsCreator
            .Received(1)
            .Conflict(
                Arg.Is<IReadOnlyCollection<ErrorDescriptor>>(es =>
                    es.Any(e => e.Code == "g:idempotency_in_flight_timeout")
                )
            );
    }

    [Fact]
    public async Task should_return_422_on_wait_and_replay_when_post_lock_record_has_mismatch()
    {
        byte[] body = [1, 2, 3];
        var inFlight = new IdempotencyRecord
        {
            Kind = RecordKind.InFlight,
            Fingerprint = SHA256.HashData(body),
            CreatedAt = DateTimeOffset.UtcNow,
        };
        var complete = new IdempotencyRecord
        {
            Kind = RecordKind.Complete,
            StatusCode = 200,
            Body = [9],
            Fingerprint = SHA256.HashData("\t\t\t"u8), // mismatch
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var cache = Substitute.For<ICache>();
        cache
            .GetAsync<IdempotencyRecord>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(
                new CacheValue<IdempotencyRecord>(inFlight, hasValue: true),
                new CacheValue<IdempotencyRecord>(complete, hasValue: true)
            );

        var dlock = Substitute.For<IDistributedLease>();
        var lockProvider = Substitute.For<IDistributedLock>();
        lockProvider
            .TryAcquireAsync(Arg.Any<string>(), Arg.Any<DistributedLockAcquireOptions?>(), Arg.Any<CancellationToken>())
            .Returns(dlock);

        var problemDetailsCreator = Substitute.For<IProblemDetailsCreator>();
        problemDetailsCreator
            .UnprocessableEntity(Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<ErrorDescriptor>>>())
            .Returns(new ProblemDetails { Status = 422 });

        var middleware = _CreateMiddlewareWithLock(cache, lockProvider, problemDetailsCreator);
        var context = CreateContext(idempotencyKey: "k1", body: body);

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        problemDetailsCreator
            .Received(1)
            .UnprocessableEntity(
                Arg.Is<IReadOnlyDictionary<string, IReadOnlyList<ErrorDescriptor>>>(d =>
                    d.ContainsKey("idempotency_key")
                    && d["idempotency_key"].Any(e => e.Code == "g:idempotency_key_reused")
                )
            );
    }

    [Fact]
    public async Task should_return_409_timeout_on_wait_and_replay_when_post_lock_record_is_inflight()
    {
        byte[] body = [1, 2, 3];
        var inFlight = new IdempotencyRecord
        {
            Kind = RecordKind.InFlight,
            Fingerprint = SHA256.HashData(body),
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var cache = Substitute.For<ICache>();
        cache
            .GetAsync<IdempotencyRecord>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(
                new CacheValue<IdempotencyRecord>(inFlight, hasValue: true),
                new CacheValue<IdempotencyRecord>(inFlight, hasValue: true) // still InFlight after lock
            );

        var dlock = Substitute.For<IDistributedLease>();
        var lockProvider = Substitute.For<IDistributedLock>();
        lockProvider
            .TryAcquireAsync(Arg.Any<string>(), Arg.Any<DistributedLockAcquireOptions?>(), Arg.Any<CancellationToken>())
            .Returns(dlock);

        var problemDetailsCreator = Substitute.For<IProblemDetailsCreator>();
        problemDetailsCreator
            .Conflict(Arg.Any<IReadOnlyCollection<ErrorDescriptor>>())
            .Returns(new ProblemDetails { Status = 409 });

        var middleware = _CreateMiddlewareWithLock(cache, lockProvider, problemDetailsCreator);
        var context = CreateContext(idempotencyKey: "k1", body: body);

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        problemDetailsCreator
            .Received(1)
            .Conflict(
                Arg.Is<IReadOnlyCollection<ErrorDescriptor>>(es =>
                    es.Any(e => e.Code == "g:idempotency_in_flight_timeout")
                )
            );
    }

    [Fact]
    public async Task should_return_409_timeout_on_wait_and_replay_when_post_lock_record_is_missing()
    {
        byte[] body = [1, 2, 3];
        var inFlight = new IdempotencyRecord
        {
            Kind = RecordKind.InFlight,
            Fingerprint = SHA256.HashData(body),
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var cache = Substitute.For<ICache>();
        cache
            .GetAsync<IdempotencyRecord>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(
                new CacheValue<IdempotencyRecord>(inFlight, hasValue: true),
                CacheValue<IdempotencyRecord>.NoValue // evicted after lock
            );

        var dlock = Substitute.For<IDistributedLease>();
        var lockProvider = Substitute.For<IDistributedLock>();
        lockProvider
            .TryAcquireAsync(Arg.Any<string>(), Arg.Any<DistributedLockAcquireOptions?>(), Arg.Any<CancellationToken>())
            .Returns(dlock);

        var problemDetailsCreator = Substitute.For<IProblemDetailsCreator>();
        problemDetailsCreator
            .Conflict(Arg.Any<IReadOnlyCollection<ErrorDescriptor>>())
            .Returns(new ProblemDetails { Status = 409 });

        var middleware = _CreateMiddlewareWithLock(cache, lockProvider, problemDetailsCreator);
        var context = CreateContext(idempotencyKey: "k1", body: body);

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        problemDetailsCreator
            .Received(1)
            .Conflict(
                Arg.Is<IReadOnlyCollection<ErrorDescriptor>>(es =>
                    es.Any(e => e.Code == "g:idempotency_in_flight_timeout")
                )
            );
    }

    // ── U8: oversize body (AE6) ───────────────────────────────────────────────

    private static IOptionsMonitor<IdempotencyOptions> _OptionsWithCap(
        int cap,
        OversizeBehavior behavior = OversizeBehavior.Reject
    )
    {
        var opts = Substitute.For<IOptionsMonitor<IdempotencyOptions>>();
        opts.CurrentValue.Returns(new IdempotencyOptions { MaxBodySizeForHashing = cap, OversizeBehavior = behavior });
        return opts;
    }

    [Theory]
    [InlineData(4, 3, true)]
    [InlineData(4, 4, true)]
    [InlineData(4, 5, false)]
    public async Task should_preserve_fingerprint_and_rewind_across_request_buffer_threshold(
        int bufferThreshold,
        int bodyLength,
        bool expectedInMemory
    )
    {
        var options = Substitute.For<IOptionsMonitor<IdempotencyOptions>>();
        options.CurrentValue.Returns(
            new IdempotencyOptions { MaxBodySizeForHashing = 16, RequestBodyBufferThreshold = bufferThreshold }
        );
        var cache = Substitute.For<ICache>();
        IdempotencyRecord? insertedMarker = null;
        cache
            .GetAsync<IdempotencyRecord>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(CacheValue<IdempotencyRecord>.NoValue);
        cache
            .TryInsertAsync(
                Arg.Any<string>(),
                Arg.Do<IdempotencyRecord>(record => insertedMarker = record),
                Arg.Any<TimeSpan?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(true);

        var body = Enumerable.Range(1, bodyLength).Select(static value => (byte)value).ToArray();
        var middleware = CreateMiddleware(options: options, cache: cache);
        var context = CreateContext(idempotencyKey: "k1", body: body);
        context.Request.Body = new NonSeekableRequestBodyStream(body);
        byte[]? handlerBody = null;

        await middleware.InvokeAsync(
            context,
            async ctx =>
            {
                var bufferingStream = ctx.Request.Body.Should().BeOfType<FileBufferingReadStream>().Which;
                bufferingStream.InMemory.Should().Be(expectedInMemory);
                (bufferingStream.TempFileName is null).Should().Be(expectedInMemory);
                bufferingStream.Position.Should().Be(0);
                using var buffer = new MemoryStream();
                await bufferingStream.CopyToAsync(buffer, AbortToken);
                handlerBody = buffer.ToArray();
            }
        );

        insertedMarker.Should().NotBeNull();
        insertedMarker!.Fingerprint.Should().Equal(SHA256.HashData(body));
        handlerBody.Should().Equal(body);
    }

    [Fact]
    public async Task should_reject_with_413_when_body_exceeds_cap_and_behavior_is_reject()
    {
        // given — cap of 5 bytes, body of 8 bytes
        var options = _OptionsWithCap(cap: 5, behavior: OversizeBehavior.Reject);
        var cache = Substitute.For<ICache>();

        var pdNormalized = false;
        var problemDetailsCreator = Substitute.For<IProblemDetailsCreator>();
        problemDetailsCreator.When(p => p.Normalize(Arg.Any<ProblemDetails>())).Do(_ => pdNormalized = true);

        var middleware = CreateMiddleware(options: options, cache: cache, problemDetailsCreator: problemDetailsCreator);
        var context = CreateContext(idempotencyKey: "k1", body: [1, 2, 3, 4, 5, 6, 7, 8]);
        var nextCalled = false;

        // when
        await middleware.InvokeAsync(
            context,
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            }
        );

        // then
        nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be(413);
        pdNormalized.Should().BeTrue();
        await cache.DidNotReceive().GetAsync<IdempotencyRecord>(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await cache
            .DidNotReceive()
            .TryInsertAsync(
                Arg.Any<string>(),
                Arg.Any<IdempotencyRecord>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_pass_through_when_body_exceeds_cap_and_behavior_is_pass_through()
    {
        // given — cap of 5 bytes, body of 8 bytes
        var options = _OptionsWithCap(cap: 5, behavior: OversizeBehavior.PassThrough);
        var cache = Substitute.For<ICache>();

        var middleware = CreateMiddleware(options: options, cache: cache);
        var context = CreateContext(idempotencyKey: "k1", body: [1, 2, 3, 4, 5, 6, 7, 8]);
        var nextCalled = false;

        // when
        await middleware.InvokeAsync(
            context,
            ctx =>
            {
                nextCalled = true;
                ctx.Response.StatusCode = 200;
                return Task.CompletedTask;
            }
        );

        // then
        nextCalled.Should().BeTrue();
        context.Response.Headers.Should().NotContainKey(HttpHeaderNames.IdempotentReplayed);
        await cache.DidNotReceive().GetAsync<IdempotencyRecord>(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await cache
            .DidNotReceive()
            .TryInsertAsync(
                Arg.Any<string>(),
                Arg.Any<IdempotencyRecord>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<CancellationToken>()
            );
        await cache
            .DidNotReceive()
            .UpsertAsync(
                Arg.Any<string>(),
                Arg.Any<IdempotencyRecord>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_accept_body_exactly_at_cap()
    {
        // given — cap == body length, accepted normally
        var options = _OptionsWithCap(cap: 5, behavior: OversizeBehavior.Reject);
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

        var middleware = CreateMiddleware(options: options, cache: cache);
        var context = CreateContext(idempotencyKey: "k1", body: [1, 2, 3, 4, 5]);
        var nextCalled = false;

        // when
        await middleware.InvokeAsync(
            context,
            ctx =>
            {
                nextCalled = true;
                ctx.Response.StatusCode = 200;
                return Task.CompletedTask;
            }
        );

        // then
        nextCalled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(200);
        await cache
            .Received(1)
            .TryInsertAsync(
                Arg.Any<string>(),
                Arg.Any<IdempotencyRecord>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<CancellationToken>()
            );
    }

    // ── U8: default cache predicate integration ───────────────────────────────

    [Fact]
    public async Task should_not_cache_5xx_response_using_default_predicate()
    {
        // given — no custom predicate, response is 503
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

        var middleware = CreateMiddleware(cache: cache);
        var context = CreateContext(idempotencyKey: "k1", body: [1, 2, 3]);

        // when — handler returns 503
        await middleware.InvokeAsync(
            context,
            ctx =>
            {
                ctx.Response.StatusCode = 503;
                return Task.CompletedTask;
            }
        );

        // then — should NOT cache (5xx is not in default predicate)
        await cache
            .DidNotReceive()
            .UpsertAsync(
                Arg.Any<string>(),
                Arg.Any<IdempotencyRecord>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<CancellationToken>()
            );
        // marker should be removed
        await cache.Received(1).RemoveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_cache_422_response_using_default_predicate()
    {
        // given — no custom predicate, response is 422 (cacheable per AE5)
        byte[] body = [1, 2, 3];

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
        cache
            .TryReplaceIfEqualAsync(
                Arg.Any<string>(),
                Arg.Any<IdempotencyRecord?>(),
                Arg.Any<IdempotencyRecord?>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(true);

        var middleware = CreateMiddleware(cache: cache);
        var context = CreateContext(idempotencyKey: "k1", body: body);

        // when — handler returns 422
        await middleware.InvokeAsync(
            context,
            ctx =>
            {
                ctx.Response.StatusCode = 422;
                return Task.CompletedTask;
            }
        );

        // then — finalize promotes the marker to a Complete 422 record via CAS
        await cache
            .Received(1)
            .TryReplaceIfEqualAsync(
                Arg.Any<string>(),
                Arg.Any<IdempotencyRecord?>(),
                Arg.Is<IdempotencyRecord?>(r => r != null && r.Kind == RecordKind.Complete && r.StatusCode == 422),
                Arg.Any<TimeSpan?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_honor_consumer_overridden_cache_predicate()
    {
        // given — consumer says _ => true (cache everything), response is 503
        var opts = Substitute.For<IOptionsMonitor<IdempotencyOptions>>();
        opts.CurrentValue.Returns(new IdempotencyOptions { ShouldCacheResponse = _ => true });

        byte[] body = [1, 2, 3];
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
        cache
            .TryReplaceIfEqualAsync(
                Arg.Any<string>(),
                Arg.Any<IdempotencyRecord?>(),
                Arg.Any<IdempotencyRecord?>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(true);

        var middleware = CreateMiddleware(options: opts, cache: cache);
        var context = CreateContext(idempotencyKey: "k1", body: body);

        // when — handler returns 503
        await middleware.InvokeAsync(
            context,
            ctx =>
            {
                ctx.Response.StatusCode = 503;
                return Task.CompletedTask;
            }
        );

        // then — consumer override wins: 503 IS cached via CAS
        await cache
            .Received(1)
            .TryReplaceIfEqualAsync(
                Arg.Any<string>(),
                Arg.Any<IdempotencyRecord?>(),
                Arg.Is<IdempotencyRecord?>(r => r != null && r.Kind == RecordKind.Complete && r.StatusCode == 503),
                Arg.Any<TimeSpan?>(),
                Arg.Any<CancellationToken>()
            );
    }

    // ── new behaviors introduced by middleware rewrite ───────────────────────

    [Fact]
    public async Task should_remove_marker_and_preserve_response_when_finalize_cas_throws_in_fail_open_mode()
    {
        // given — TryReplaceIfEqualAsync fails after successful next; middleware must remove the
        // marker and preserve the successful handler response in the default FailOpen mode.
        byte[] body = [1, 2, 3];

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
        cache
            .TryReplaceIfEqualAsync(
                Arg.Any<string>(),
                Arg.Any<IdempotencyRecord?>(),
                Arg.Any<IdempotencyRecord?>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns<ValueTask<bool>>(_ => throw new InvalidOperationException("cache down"));

        var middleware = CreateMiddleware(cache: cache);
        var context = CreateContext(idempotencyKey: "k1", body: body);

        await middleware.InvokeAsync(
            context,
            ctx =>
            {
                ctx.Response.StatusCode = 200;
                return Task.CompletedTask;
            }
        );

        context.Response.StatusCode.Should().Be(200);
        await cache.Received(1).RemoveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_remove_marker_and_throw_when_finalize_cas_throws_in_throw_mode()
    {
        // given
        byte[] body = [1, 2, 3];

        var options = Substitute.For<IOptionsMonitor<IdempotencyOptions>>();
        options.CurrentValue.Returns(new IdempotencyOptions { OnCacheError = OnCacheErrorBehavior.Throw });

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
        cache
            .TryReplaceIfEqualAsync(
                Arg.Any<string>(),
                Arg.Any<IdempotencyRecord?>(),
                Arg.Any<IdempotencyRecord?>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns<ValueTask<bool>>(_ => throw new InvalidOperationException("cache down"));

        var middleware = CreateMiddleware(options: options, cache: cache);
        var context = CreateContext(idempotencyKey: "k1", body: body);

        var act = () =>
            middleware.InvokeAsync(
                context,
                ctx =>
                {
                    ctx.Response.StatusCode = 200;
                    return Task.CompletedTask;
                }
            );

        await act.Should().ThrowAsync<InvalidOperationException>();
        await cache.Received(1).RemoveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_attempt_finalize_cas_after_successful_handler()
    {
        // Verifies that finalize uses CAS (TryReplaceIfEqualAsync) — not GET+UPSERT — to swap the
        // marker for the Complete record. When the CAS returns false (marker no longer matches
        // because it was evicted or overwritten), the middleware logs and does not retry.
        byte[] body = [1, 2, 3];
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
        cache
            .TryReplaceIfEqualAsync(
                Arg.Any<string>(),
                Arg.Any<IdempotencyRecord?>(),
                Arg.Any<IdempotencyRecord?>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(false);

        var middleware = CreateMiddleware(cache: cache);
        var context = CreateContext(idempotencyKey: "k1", body: body);

        await middleware.InvokeAsync(
            context,
            ctx =>
            {
                ctx.Response.StatusCode = 200;
                return Task.CompletedTask;
            }
        );

        await cache
            .Received(1)
            .TryReplaceIfEqualAsync(
                Arg.Any<string>(),
                Arg.Is<IdempotencyRecord?>(r => r != null && r.Kind == RecordKind.InFlight),
                Arg.Is<IdempotencyRecord?>(r => r != null && r.Kind == RecordKind.Complete && r.StatusCode == 200),
                Arg.Any<TimeSpan?>(),
                Arg.Any<CancellationToken>()
            );
        // CAS returning false is the "marker no longer ours" path — middleware logs and exits.
        await cache
            .DidNotReceive()
            .UpsertAsync(
                Arg.Any<string>(),
                Arg.Any<IdempotencyRecord>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<CancellationToken>()
            );
        await cache.DidNotReceive().RemoveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_return_422_when_inflight_marker_has_different_fingerprint()
    {
        // given — cached InFlight marker exists with a fingerprint that does NOT match the incoming body
        byte[] body = [1, 2, 3];
        var inFlight = new IdempotencyRecord
        {
            Kind = RecordKind.InFlight,
            Fingerprint = SHA256.HashData("\t\t\t"u8),
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var cache = Substitute.For<ICache>();
        cache
            .GetAsync<IdempotencyRecord>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CacheValue<IdempotencyRecord>(inFlight, hasValue: true));

        var problemDetailsCreator = Substitute.For<IProblemDetailsCreator>();
        problemDetailsCreator
            .UnprocessableEntity(Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<ErrorDescriptor>>>())
            .Returns(new ProblemDetails { Status = 422 });

        var middleware = CreateMiddleware(cache: cache, problemDetailsCreator: problemDetailsCreator);
        var context = CreateContext(idempotencyKey: "k1", body: body);

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        // mismatch wins over in-flight 409
        problemDetailsCreator
            .Received(1)
            .UnprocessableEntity(
                Arg.Is<IReadOnlyDictionary<string, IReadOnlyList<ErrorDescriptor>>>(d =>
                    d["idempotency_key"].Any(e => e.Code == "g:idempotency_key_reused")
                )
            );
    }

    [Theory]
    [InlineData("abcd")] // SOH control character
    [InlineData("abcd")] // DEL
    [InlineData("ab\ncd")] // newline
    public async Task should_reject_with_400_when_key_contains_control_characters(string malformedKey)
    {
        var cache = Substitute.For<ICache>();
        var problemDetailsCreator = Substitute.For<IProblemDetailsCreator>();
        problemDetailsCreator
            .BadRequest(Arg.Any<string?>(), Arg.Any<ErrorDescriptor?>())
            .Returns(new ProblemDetails { Status = 400 });

        var middleware = CreateMiddleware(cache: cache, problemDetailsCreator: problemDetailsCreator);
        var context = CreateContext(idempotencyKey: malformedKey, body: [1, 2, 3]);

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        problemDetailsCreator
            .Received(1)
            .BadRequest(
                Arg.Any<string?>(),
                Arg.Is<ErrorDescriptor?>(e => e != null && e.Code == "g:idempotency_key_malformed")
            );
        await cache.DidNotReceive().GetAsync<IdempotencyRecord>(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_reject_with_400_when_key_exceeds_255_characters()
    {
        var longKey = new string('a', 256);

        var cache = Substitute.For<ICache>();
        var problemDetailsCreator = Substitute.For<IProblemDetailsCreator>();
        problemDetailsCreator
            .BadRequest(Arg.Any<string?>(), Arg.Any<ErrorDescriptor?>())
            .Returns(new ProblemDetails { Status = 400 });

        var middleware = CreateMiddleware(cache: cache, problemDetailsCreator: problemDetailsCreator);
        var context = CreateContext(idempotencyKey: longKey, body: [1, 2, 3]);

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        problemDetailsCreator
            .Received(1)
            .BadRequest(
                Arg.Any<string?>(),
                Arg.Is<ErrorDescriptor?>(e => e != null && e.Code == "g:idempotency_key_malformed")
            );
    }

    [Fact]
    public async Task should_reject_with_400_when_key_is_multi_valued()
    {
        var cache = Substitute.For<ICache>();
        var problemDetailsCreator = Substitute.For<IProblemDetailsCreator>();
        problemDetailsCreator
            .BadRequest(Arg.Any<string?>(), Arg.Any<ErrorDescriptor?>())
            .Returns(new ProblemDetails { Status = 400 });

        var middleware = CreateMiddleware(cache: cache, problemDetailsCreator: problemDetailsCreator);
        var context = CreateContext(idempotencyKey: "k1", body: [1, 2, 3]);
        context.Request.Headers.Append(HttpHeaderNames.IdempotencyKey, "k2");

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        problemDetailsCreator
            .Received(1)
            .BadRequest(
                Arg.Any<string?>(),
                Arg.Is<ErrorDescriptor?>(e => e != null && e.Code == "g:idempotency_key_malformed")
            );
    }

    [Fact]
    public async Task should_clear_preexisting_allowlist_header_before_writing_replay()
    {
        // given — Content-Type set upstream BEFORE idempotency middleware runs replay
        byte[] body = [1, 2, 3];
        var fingerprint = SHA256.HashData(body);
        var record = new IdempotencyRecord
        {
            Kind = RecordKind.Complete,
            StatusCode = 200,
            Headers = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["Content-Type"] = ["application/json"],
            },
            Body = [10],
            Fingerprint = fingerprint,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var cache = Substitute.For<ICache>();
        cache
            .GetAsync<IdempotencyRecord>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CacheValue<IdempotencyRecord>(record, hasValue: true));

        var middleware = CreateMiddleware(cache: cache);
        var context = CreateContext(idempotencyKey: "k1", body: body);
        context.Response.ContentType = "text/plain"; // upstream value

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        // Captured value (application/json) wins; upstream text/plain is cleared.
        context.Response.ContentType.Should().Be("application/json");
    }

    [Fact]
    public async Task should_short_circuit_sha256_when_custom_request_fingerprint_provided()
    {
        // given — RequestFingerprint delegate returns a fixed value WITHOUT touching the body
        byte[] body = [1, 2, 3];
        byte[] fakeFingerprint = [0xDE, 0xAD, 0xBE, 0xEF];

        var opts = Substitute.For<IOptionsMonitor<IdempotencyOptions>>();
        opts.CurrentValue.Returns(
            new IdempotencyOptions { RequestFingerprint = _ => new ValueTask<byte[]>(fakeFingerprint) }
        );

        var cache = Substitute.For<ICache>();
        cache
            .GetAsync<IdempotencyRecord>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(
                CacheValue<IdempotencyRecord>.NoValue,
                new CacheValue<IdempotencyRecord>(
                    new IdempotencyRecord
                    {
                        Kind = RecordKind.InFlight,
                        Fingerprint = fakeFingerprint,
                        CreatedAt = DateTimeOffset.UtcNow,
                    },
                    hasValue: true
                )
            );
        cache
            .TryInsertAsync(
                Arg.Any<string>(),
                Arg.Any<IdempotencyRecord>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(true);

        var middleware = CreateMiddleware(options: opts, cache: cache);
        var context = CreateContext(idempotencyKey: "k1", body: body);

        await middleware.InvokeAsync(
            context,
            ctx =>
            {
                ctx.Response.StatusCode = 200;
                return Task.CompletedTask;
            }
        );

        // Marker carries the custom fingerprint — SHA-256 path was skipped.
        await cache
            .Received(1)
            .TryInsertAsync(
                Arg.Any<string>(),
                Arg.Is<IdempotencyRecord>(r => r.Fingerprint != null && r.Fingerprint.SequenceEqual(fakeFingerprint)),
                Arg.Any<TimeSpan?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_pass_inflight_marker_ttl_equal_to_key_expiration()
    {
        var opts = Substitute.For<IOptionsMonitor<IdempotencyOptions>>();
        opts.CurrentValue.Returns(new IdempotencyOptions { IdempotencyKeyExpiration = TimeSpan.FromHours(7) });

        TimeSpan? capturedMarkerTtl = null;
        var cache = Substitute.For<ICache>();
        cache
            .GetAsync<IdempotencyRecord>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(CacheValue<IdempotencyRecord>.NoValue);
        cache
            .TryInsertAsync(
                Arg.Any<string>(),
                Arg.Any<IdempotencyRecord>(),
                Arg.Do<TimeSpan?>(t => capturedMarkerTtl = t),
                Arg.Any<CancellationToken>()
            )
            .Returns(true);

        var middleware = CreateMiddleware(options: opts, cache: cache);
        var context = CreateContext(idempotencyKey: "k1", body: [1, 2, 3]);

        await middleware.InvokeAsync(
            context,
            ctx =>
            {
                ctx.Response.StatusCode = 200;
                return Task.CompletedTask;
            }
        );

        capturedMarkerTtl.Should().Be(TimeSpan.FromHours(7));
    }

    [Fact]
    public async Task should_use_cancellation_token_none_for_marker_cleanup()
    {
        // given — cts is cancelled by the handler before it throws; cleanup must still run
        // with CancellationToken.None (i.e., a non-cancelled token).
        using var cts = new CancellationTokenSource();
        var ctp = Substitute.For<ICancellationTokenProvider>();
        ctp.Token.Returns(cts.Token);

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

        var middleware = CreateMiddleware(cache: cache, cancellationTokenProvider: ctp);
        var context = CreateContext(idempotencyKey: "k1", body: [1, 2]);

        var act = () =>
            middleware.InvokeAsync(
                context,
                async _ =>
                {
                    await cts.CancelAsync();
                    throw new InvalidOperationException("boom");
                }
            );

        await act.Should().ThrowAsync<InvalidOperationException>();
        await cache
            .Received(1)
            .RemoveAsync(Arg.Any<string>(), Arg.Is<CancellationToken>(c => !c.IsCancellationRequested));
    }
}

file sealed class NonSeekableRequestBodyStream(byte[] bytes) : MemoryStream(bytes)
{
    public override bool CanSeek => false;
}
