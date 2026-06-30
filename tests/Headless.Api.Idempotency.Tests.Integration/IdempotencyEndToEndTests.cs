// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Api;
using Headless.Caching;
using Headless.Constants;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

// CA2025: `_Post` builds an `HttpRequestMessage` under `using var` and awaits `SendAsync` inline,
// so the request disposes only after the SendAsync task completes. Concurrency tests store the
// returned Task to interleave with another request, which the analyzer cannot see is safe.
#pragma warning disable CA2025

namespace Tests;

public sealed class IdempotencyEndToEndTests : TestBase
{
    // ── AE1: replay on identical retry ────────────────────────────────────────

    [Fact]
    public async Task should_replay_cached_response_on_identical_retry()
    {
        await using var app = await IdempotencyTestApp.CreateAsync();
        using var client = IdempotencyTestApp.CreateClient(app);

        var first = await _Post(client, "/echo", key: "k1", body: "hello");
        var second = await _Post(client, "/echo", key: "k1", body: "hello");

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        second.StatusCode.Should().Be(HttpStatusCode.Created);

        var firstBody = await first.Content.ReadAsStringAsync(AbortToken);
        var secondBody = await second.Content.ReadAsStringAsync(AbortToken);

        secondBody.Should().Be(firstBody);
        first.Headers.Contains(HttpHeaderNames.IdempotentReplayed).Should().BeFalse();
        second.Headers.GetValues(HttpHeaderNames.IdempotentReplayed).Should().ContainSingle().Which.Should().Be("true");
    }

    [Fact]
    public async Task replay_should_return_same_body_proving_handler_ran_exactly_once()
    {
        // The handler embeds a fresh GUID per invocation in the response body.
        // Replay returns the cached bytes, so identical bodies across two retries
        // means the handler ran exactly once.
        await using var app = await IdempotencyTestApp.CreateAsync();
        using var client = IdempotencyTestApp.CreateClient(app);

        var first = await _Post(client, "/echo", key: "k1", body: "abc");
        var second = await _Post(client, "/echo", key: "k1", body: "abc");

        var firstBody = await first.Content.ReadAsStringAsync();
        var secondBody = await second.Content.ReadAsStringAsync();

        secondBody.Should().Be(firstBody);
    }

    // ── AE2: mismatch (same key, different body) → 422 ────────────────────────

    [Fact]
    public async Task should_return_422_when_same_key_used_with_different_body()
    {
        await using var app = await IdempotencyTestApp.CreateAsync();
        using var client = IdempotencyTestApp.CreateClient(app);

        var first = await _Post(client, "/echo", key: "k1", body: "alpha");
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        var conflict = await _Post(client, "/echo", key: "k1", body: "beta");

        conflict.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var json = await conflict.Content.ReadAsStringAsync();
        json.Should().Contain("g:idempotency_key_reused");
    }

    [Fact]
    public async Task original_record_should_remain_replayable_after_mismatch_attempt()
    {
        await using var app = await IdempotencyTestApp.CreateAsync();
        using var client = IdempotencyTestApp.CreateClient(app);

        await _Post(client, "/echo", key: "k1", body: "alpha");
        await _Post(client, "/echo", key: "k1", body: "beta"); // 422, doesn't disturb the original

        var replay = await _Post(client, "/echo", key: "k1", body: "alpha");

        replay.StatusCode.Should().Be(HttpStatusCode.Created);
        replay.Headers.GetValues(HttpHeaderNames.IdempotentReplayed).Should().ContainSingle().Which.Should().Be("true");
    }

    // ── AE5: status predicate (default) ──────────────────────────────────────

    [Fact]
    public async Task should_not_cache_5xx_response()
    {
        await using var app = await IdempotencyTestApp.CreateAsync();
        using var client = IdempotencyTestApp.CreateClient(app);

        var first = await _Post(client, "/status?code=503", key: "k1", body: "");
        first.StatusCode.Should().Be((HttpStatusCode)503);
        first.Headers.Contains(HttpHeaderNames.IdempotentReplayed).Should().BeFalse();

        // Retry — handler should run fresh (cache empty)
        var second = await _Post(client, "/status?code=503", key: "k1", body: "");
        second.StatusCode.Should().Be((HttpStatusCode)503);
        second.Headers.Contains(HttpHeaderNames.IdempotentReplayed).Should().BeFalse();
    }

    [Fact]
    public async Task should_cache_422_response()
    {
        await using var app = await IdempotencyTestApp.CreateAsync();
        using var client = IdempotencyTestApp.CreateClient(app);

        var first = await _Post(client, "/status?code=422", key: "k1", body: "");
        first.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var second = await _Post(client, "/status?code=422", key: "k1", body: "");
        second.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        second.Headers.GetValues(HttpHeaderNames.IdempotentReplayed).Should().ContainSingle().Which.Should().Be("true");
    }

    // ── AE6: oversize body ────────────────────────────────────────────────────

    [Fact]
    public async Task should_reject_oversize_body_with_413()
    {
        await using var app = await IdempotencyTestApp.CreateAsync(o =>
        {
            o.MaxBodySizeForHashing = 32;
            o.OversizeBehavior = OversizeBehavior.Reject;
        });
        using var client = IdempotencyTestApp.CreateClient(app);

        var oversize = new string('A', 1024); // 1 KiB > 32 B cap
        var response = await _Post(client, "/echo", key: "k1", body: oversize);

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        var json = await response.Content.ReadAsStringAsync();
        json.Should().Contain("g:idempotency_body_too_large");

        // 413 is not in ASP.NET Core's default ApiBehaviorOptions.ClientErrorMapping, so the
        // middleware constructs the ProblemDetails inline and runs it through Normalize. The
        // shape must match other status codes (404/408/500/501) — title and type must be set.
        json.Should().Contain($"\"title\":\"{HeadlessProblemDetailsConstants.Titles.PayloadTooLarge}\"");
        json.Should().Contain($"\"type\":\"{HeadlessProblemDetailsConstants.Types.PayloadTooLarge}\"");
    }

    [Fact]
    public async Task should_pass_through_oversize_body_without_caching_when_configured()
    {
        await using var app = await IdempotencyTestApp.CreateAsync(o =>
        {
            o.MaxBodySizeForHashing = 32;
            o.OversizeBehavior = OversizeBehavior.PassThrough;
        });
        using var client = IdempotencyTestApp.CreateClient(app);

        var oversize = new string('A', 1024);
        var first = await _Post(client, "/echo", key: "k1", body: oversize);
        var second = await _Post(client, "/echo", key: "k1", body: oversize);

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        second.StatusCode.Should().Be(HttpStatusCode.Created);
        // No replay header → handler ran each time
        first.Headers.Contains(HttpHeaderNames.IdempotentReplayed).Should().BeFalse();
        second.Headers.Contains(HttpHeaderNames.IdempotentReplayed).Should().BeFalse();

        // Per-invocation GUID in body proves handler ran each time (not replay)
        var firstBody = await first.Content.ReadAsStringAsync();
        var secondBody = await second.Content.ReadAsStringAsync();
        secondBody.Should().NotBe(firstBody);
    }

    // ── AE9: header allowlist filters Set-Cookie / traceparent ───────────────

    [Fact]
    public async Task replay_response_should_drop_set_cookie_and_traceparent_by_default()
    {
        await using var app = await IdempotencyTestApp.CreateAsync();
        using var client = IdempotencyTestApp.CreateClient(app);

        await _Post(client, "/echo", key: "k1", body: "x");
        var replay = await _Post(client, "/echo", key: "k1", body: "x");

        replay.Headers.Contains(HttpHeaderNames.IdempotentReplayed).Should().BeTrue();
        replay.Headers.Contains("Set-Cookie").Should().BeFalse("Set-Cookie is not in the default allowlist");
        replay.Headers.Contains("traceparent").Should().BeFalse("traceparent is not in the default allowlist");
        replay.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
    }

    // ── Pass-through: no key / unsupported method ────────────────────────────

    [Fact]
    public async Task should_pass_through_when_idempotency_key_header_missing()
    {
        await using var app = await IdempotencyTestApp.CreateAsync();
        using var client = IdempotencyTestApp.CreateClient(app);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/echo");

        request.Content = new StringContent("hello");
        using var response = await client.SendAsync(request, cancellationToken: AbortToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Contains(HttpHeaderNames.IdempotentReplayed).Should().BeFalse();
    }

    // ── AE7: tenant isolation ────────────────────────────────────────────────

    [Fact]
    public async Task different_tenants_with_same_key_should_not_collide()
    {
        const string tenantHeader = "X-Test-Tenant";
        await using var app = await IdempotencyTestApp.CreateAsync(tenantHeaderName: tenantHeader);
        using var client = IdempotencyTestApp.CreateClient(app);

        async Task<HttpResponseMessage> postForTenant(string tenant)
        {
            return await _Post(
                client,
                "/echo",
                key: "k1",
                body: "same-body",
                extraHeaders: new(StringComparer.Ordinal) { [tenantHeader] = tenant }
            );
        }

        var tenantA = await postForTenant("TENANT-A");
        var tenantB = await postForTenant("TENANT-B");

        tenantA.StatusCode.Should().Be(HttpStatusCode.Created);
        tenantB.StatusCode.Should().Be(HttpStatusCode.Created);
        // Both should be fresh handler invocations
        tenantA.Headers.Contains(HttpHeaderNames.IdempotentReplayed).Should().BeFalse();
        tenantB.Headers.Contains(HttpHeaderNames.IdempotentReplayed).Should().BeFalse();

        // Per-invocation GUID in body proves each tenant ran the handler independently
        var bodyA = await tenantA.Content.ReadAsStringAsync(AbortToken);
        var bodyB = await tenantB.Content.ReadAsStringAsync(AbortToken);
        bodyA.Should().NotBe(bodyB);
    }

    // ── AE12: per-endpoint metadata ──────────────────────────────────────────

    [Fact]
    public async Task per_endpoint_with_idempotency_metadata_should_apply_overrides()
    {
        await using var app = await IdempotencyTestApp.CreateAsync(mapAdditionalEndpoints: a =>
        {
            a.MapPost(
                    "/strict",
                    ctx =>
                    {
                        ctx.Response.StatusCode = StatusCodes.Status201Created;
                        return Task.CompletedTask;
                    }
                )
                .WithIdempotency(o => o.MismatchStatusCode = StatusCodes.Status409Conflict);
        });

        using var client = IdempotencyTestApp.CreateClient(app);

        await _Post(client, "/strict", key: "k1", body: "one");
        var mismatch = await _Post(client, "/strict", key: "k1", body: "two");

        mismatch.StatusCode.Should().Be(HttpStatusCode.Conflict, "endpoint override changes mismatch status to 409");
    }

    // ── AE3: concurrent in-flight with Reject strategy ───────────────────────

    [Fact]
    public async Task concurrent_requests_with_reject_strategy_should_invoke_handler_once_and_409_the_loser()
    {
        var gate = new IdempotencyTestApp.TestHandlerGate();
        await using var app = await IdempotencyTestApp.CreateAsync(handlerGate: gate);
        using var client = IdempotencyTestApp.CreateClient(app);

        // Fire the winner first so it inserts the InFlight marker, then wait for it to enter
        // the handler before firing the loser. This guarantees the loser observes InFlight.
        var winnerTask = _Post(client, "/echo", key: "k1", body: "hello");
        await gate.WaitForInvocationsAsync(1, TimeSpan.FromSeconds(2));
        var loserTask = _Post(client, "/echo", key: "k1", body: "hello");

        // The loser should reject immediately on the in-flight marker without invoking the handler.
        var loser = await loserTask;
        loser.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var loserBody = await loser.Content.ReadAsStringAsync(AbortToken);
        loserBody.Should().Contain("g:idempotency_in_flight");
        loserBody
            .Should()
            .NotContain(
                "g:idempotency_in_flight_timeout",
                "Reject must surface g:idempotency_in_flight, not the WaitAndReplay timeout code"
            );

        // Now release the winner and verify it completed normally.
        gate.Release();
        var winner = await winnerTask;
        winner.StatusCode.Should().Be(HttpStatusCode.Created);

        // The handler must have been entered exactly once (the loser never reached it).
        gate.InvocationCount.Should().Be(1, "Reject strategy short-circuits before invoking the handler");
    }

    // ── AE4: concurrent in-flight with WaitAndReplay strategy ────────────────

    [Fact]
    public async Task concurrent_requests_with_wait_and_replay_should_block_loser_until_winner_completes_then_replay()
    {
        var gate = new IdempotencyTestApp.TestHandlerGate();
        await using var app = await IdempotencyTestApp.CreateAsync(
            o =>
            {
                o.InFlightStrategy = InFlightStrategy.WaitAndReplay;
                o.InFlightLockTimeout = TimeSpan.FromSeconds(10);
            },
            withLockProvider: true,
            handlerGate: gate
        );
        using var client = IdempotencyTestApp.CreateClient(app);

        var winnerTask = _Post(client, "/echo", key: "k1", body: "hello");
        await gate.WaitForInvocationsAsync(1, TimeSpan.FromSeconds(2));

        var loserTask = _Post(client, "/echo", key: "k1", body: "hello");
        // The loser must NOT complete while the winner is gated — it should be blocked on the
        // distributed lock. Give it half a second to confirm it's still pending.
        await Task.Delay(500);
        loserTask.IsCompleted.Should().BeFalse("loser is blocked on the WaitAndReplay lock");

        // Release the winner; both should now succeed, with the loser observing a byte-for-byte
        // replay of the winner's response (same invocation GUID, Idempotent-Replayed: true).
        gate.Release();
        var winner = await winnerTask;
        var loser = await loserTask;

        winner.StatusCode.Should().Be(HttpStatusCode.Created);
        loser.StatusCode.Should().Be(HttpStatusCode.Created);

        winner.Headers.Contains(HttpHeaderNames.IdempotentReplayed).Should().BeFalse("winner ran the handler");
        loser.Headers.Should().Contain(h => h.Key == HttpHeaderNames.IdempotentReplayed, "loser observed the replay");

        var winnerBody = await winner.Content.ReadAsStringAsync(AbortToken);
        var loserBody = await loser.Content.ReadAsStringAsync(AbortToken);
        loserBody.Should().Be(winnerBody, "replay must be byte-equivalent to the original");

        gate.InvocationCount.Should().Be(1, "WaitAndReplay never invokes the handler twice for the same key");
    }

    [Fact]
    public async Task wait_and_replay_must_not_let_loser_steal_lock_during_winner_marker_insertion_window()
    {
        // Pin the WaitAndReplay TryInsert→TryAcquire race surfaced by the re-review (P1).
        //
        // The bug: the winner's path is
        //   1. cache.TryInsertAsync(InFlight marker) → true   (marker is now visible)
        //   2. lockProvider.TryAcquireAsync(acquireTimeout: Zero)
        // A loser arriving in the window between (1) and (2) sees the marker via the
        // existing-record fast path, falls through to _WaitAndReplayAsync, and acquires the
        // lock first (semaphore is free because the winner has not reached step 2). The winner
        // then gets null from step 2, proceeds unlocked, and the loser — holding the lock —
        // observes the InFlight marker in postLock and returns 409 g:idempotency_in_flight_timeout
        // despite the winner being healthy.
        //
        // The lock-provider hook below widens that window deterministically: when the winner's
        // first TryAcquireAsync(Zero) fires AND the cache already contains a marker for the
        // resource, hold the winner until the test signals release. Under the fix
        // (lock-before-insert), the cache is empty at hook time, the hook returns immediately,
        // and the existing post-fix flow (loser blocks on the winner's lock, then replays)
        // executes via TestHandlerGate.
        var winnerInRaceWindow = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRaceWindow = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstZeroFired = 0;
        ICache? cacheRef = null;

        var lockProvider = new IdempotencyTestApp.InMemoryDistributedLockDouble(TimeProvider.System)
        {
            BeforeAcquireAsync = async (resource, _, acquireTimeout, ct) =>
            {
                if (acquireTimeout != TimeSpan.Zero)
                {
                    return;
                }

                if (Interlocked.CompareExchange(ref firstZeroFired, 1, 0) != 0)
                {
                    return;
                }

                var cache = cacheRef;
                if (cache is null)
                {
                    return;
                }

                // Resource shape from the middleware: "lock:{cacheKey}".
                var cacheKey = resource.StartsWith("lock:", StringComparison.Ordinal) ? resource[5..] : resource;

                if (await cache.ExistsAsync(cacheKey, ct).ConfigureAwait(false))
                {
                    winnerInRaceWindow.TrySetResult();
                    await releaseRaceWindow.Task.WaitAsync(ct).ConfigureAwait(false);
                }
            },
        };

        var gate = new IdempotencyTestApp.TestHandlerGate();
        await using var app = await IdempotencyTestApp.CreateAsync(
            o =>
            {
                o.InFlightStrategy = InFlightStrategy.WaitAndReplay;
                o.InFlightLockTimeout = TimeSpan.FromSeconds(5);
            },
            handlerGate: gate,
            lockProvider: lockProvider
        );
        cacheRef = app.Services.GetRequiredService<ICache>();

        using var client = IdempotencyTestApp.CreateClient(app);

        var winnerTask = _Post(client, "/echo", key: "race-k", body: "hello");

        // Winner advances to one of two states: gated inside the race window (pre-fix) or
        // gated inside the handler (post-fix). Whichever fires first is enough to send the loser.
        await Task.WhenAny(winnerInRaceWindow.Task, gate.WaitForInvocationsAsync(1, TimeSpan.FromSeconds(5)));

        var loserTask = _Post(client, "/echo", key: "race-k", body: "hello");

        // Release both gates so the winner can finalize regardless of which path we hit.
        // releaseRaceWindow is harmless under post-fix (no waiter); gate.Release() is required
        // to unblock the handler so the winner finishes and the loser observes the Complete record.
        await Task.Delay(200);
        releaseRaceWindow.TrySetResult();
        gate.Release();

        var winner = await winnerTask;
        var loser = await loserTask;

        winner.StatusCode.Should().Be(HttpStatusCode.Created);
        loser
            .StatusCode.Should()
            .Be(
                HttpStatusCode.Created,
                "loser must replay the winner's response — not return 409 InFlightTimeout — when the winner is healthy"
            );

        gate.InvocationCount.Should().Be(1, "WaitAndReplay never invokes the handler twice for the same key");
    }

    [Fact]
    public async Task wait_and_replay_should_409_with_in_flight_timeout_when_winner_does_not_finish_before_acquire_timeout()
    {
        var gate = new IdempotencyTestApp.TestHandlerGate();
        await using var app = await IdempotencyTestApp.CreateAsync(
            o =>
            {
                o.InFlightStrategy = InFlightStrategy.WaitAndReplay;
                o.InFlightLockTimeout = TimeSpan.FromMilliseconds(250);
            },
            withLockProvider: true,
            handlerGate: gate
        );
        using var client = IdempotencyTestApp.CreateClient(app);

        var winnerTask = _Post(client, "/echo", key: "k1", body: "hello");
        await gate.WaitForInvocationsAsync(1, TimeSpan.FromSeconds(2));

        // Loser's acquire timeout is shorter than the winner's hold — it should give up and
        // return 409 g:idempotency_in_flight_timeout rather than block indefinitely.
        var loser = await _Post(client, "/echo", key: "k1", body: "hello");

        loser.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var loserBody = await loser.Content.ReadAsStringAsync();
        loserBody.Should().Contain("g:idempotency_in_flight_timeout");

        gate.Release();
        await winnerTask;
    }

    // ── AE8: null-tenant + anonymous user → pass-through (no shared bucket) ──

    [Fact]
    public async Task anonymous_requests_with_no_tenant_and_no_user_identity_should_pass_through_without_replay()
    {
        await using var app = await IdempotencyTestApp.CreateAsync();
        var userState = app.Services.GetRequiredService<IdempotencyTestApp.TestCurrentUserState>();
        userState.SetAnonymous();

        using var client = IdempotencyTestApp.CreateClient(app);

        var first = await _Post(client, "/echo", key: "k1", body: "same");
        var second = await _Post(client, "/echo", key: "k1", body: "same");

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        second.StatusCode.Should().Be(HttpStatusCode.Created);

        // Pass-through path must not emit the replay header, and must invoke the handler each time
        // (proven by per-invocation GUID in the body — two distinct GUIDs prove the cache slot
        // was never used).
        first.Headers.Contains(HttpHeaderNames.IdempotentReplayed).Should().BeFalse();
        second.Headers.Contains(HttpHeaderNames.IdempotentReplayed).Should().BeFalse();

        var firstBody = await first.Content.ReadAsStringAsync();
        var secondBody = await second.Content.ReadAsStringAsync();
        firstBody.Should().NotBe(secondBody, "no idempotency means each call runs the handler independently");
    }

    // ── RequireUserIdentity: anon-within-tenant cross-replay prevention ──────

    [Fact]
    public async Task tenant_only_anonymous_requests_should_pass_through_when_RequireUserIdentity_is_true()
    {
        // Default RequireUserIdentity=true: a tenant-resolved but user-anonymous request must
        // NOT use the default cache key (which would compose idem:{tenant}::POST:/path:{key}
        // and let two anonymous callers in the same tenant cross-replay each other).
        await using var app = await IdempotencyTestApp.CreateAsync(tenantHeaderName: "X-Tenant");
        var userState = app.Services.GetRequiredService<IdempotencyTestApp.TestCurrentUserState>();
        userState.SetAnonymous();

        using var client = IdempotencyTestApp.CreateClient(app);

        var extraHeaders = new Dictionary<string, string>(StringComparer.Ordinal) { ["X-Tenant"] = "acme" };
        var first = await _Post(client, "/echo", key: "shared-k", body: "same", extraHeaders: extraHeaders);
        var second = await _Post(client, "/echo", key: "shared-k", body: "same", extraHeaders: extraHeaders);

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        second.StatusCode.Should().Be(HttpStatusCode.Created);

        first.Headers.Contains(HttpHeaderNames.IdempotentReplayed).Should().BeFalse();
        second.Headers.Contains(HttpHeaderNames.IdempotentReplayed).Should().BeFalse();

        var firstBody = await first.Content.ReadAsStringAsync();
        var secondBody = await second.Content.ReadAsStringAsync();
        firstBody
            .Should()
            .NotBe(
                secondBody,
                "tenant-anon pass-through under default RequireUserIdentity must not share a cache slot"
            );
    }

    [Fact]
    public async Task tenant_only_anonymous_requests_should_replay_when_RequireUserIdentity_is_false()
    {
        // Operators with webhook/OAuth-callback flows opt in: tenant-anon requests participate in
        // idempotency. Two retries sharing tenant + key + body replay byte-equivalently.
        await using var app = await IdempotencyTestApp.CreateAsync(
            o => o.RequireUserIdentity = false,
            tenantHeaderName: "X-Tenant"
        );
        var userState = app.Services.GetRequiredService<IdempotencyTestApp.TestCurrentUserState>();
        userState.SetAnonymous();

        using var client = IdempotencyTestApp.CreateClient(app);

        var extraHeaders = new Dictionary<string, string>(StringComparer.Ordinal) { ["X-Tenant"] = "acme" };
        var first = await _Post(client, "/echo", key: "webhook-k", body: "same", extraHeaders: extraHeaders);
        var second = await _Post(client, "/echo", key: "webhook-k", body: "same", extraHeaders: extraHeaders);

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        second.StatusCode.Should().Be(HttpStatusCode.Created);
        second
            .Headers.GetValues(HttpHeaderNames.IdempotentReplayed)
            .Should()
            .ContainSingle()
            .Which.Should()
            .Be("true", "opt-in tenant-anon idempotency replays the original response");

        var firstBody = await first.Content.ReadAsStringAsync();
        var secondBody = await second.Content.ReadAsStringAsync();
        secondBody.Should().Be(firstBody, "replay must be byte-equivalent");
    }

    // ── Cache outage: OnCacheError = FailOpen (default) vs Throw ─────────────

    [Fact]
    public async Task should_pass_through_to_handler_when_cache_throws_and_OnCacheError_is_FailOpen()
    {
        // Default OnCacheError.FailOpen: a hard cache outage (Redis down, ElastiCache failover)
        // must not produce a 5xx storm on every idempotent endpoint. The middleware logs a
        // warning and bypasses idempotency for this request, letting the handler run unguarded.
        // The trade-off — a single retry may execute its handler twice if the outage straddles
        // attempts — is the explicit Stripe/AWS default.
        await using var app = await IdempotencyTestApp.CreateAsync(configureServices: services =>
        {
            var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(ICache));
            if (descriptor is not null)
            {
                services.Remove(descriptor);
            }
            services.AddSingleton<ICache, IdempotencyTestApp.ThrowingCache>();
        });
        using var client = IdempotencyTestApp.CreateClient(app);

        var response = await _Post(client, "/echo", key: "cache-fail-open", body: "hello");

        response.StatusCode.Should().Be(HttpStatusCode.Created, "FailOpen bypasses idempotency and runs the handler");
        response
            .Headers.Contains(HttpHeaderNames.IdempotentReplayed)
            .Should()
            .BeFalse("no replay attempted under cache outage");
    }

    [Fact]
    public async Task should_propagate_cache_exception_when_OnCacheError_is_Throw()
    {
        // Opt-in strict mode: cache exceptions surface as 5xx so operators see the outage
        // directly instead of silently losing the idempotency guarantee.
        await using var app = await IdempotencyTestApp.CreateAsync(
            o => o.OnCacheError = OnCacheErrorBehavior.Throw,
            configureServices: services =>
            {
                var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(ICache));
                if (descriptor is not null)
                {
                    services.Remove(descriptor);
                }
                services.AddSingleton<ICache, IdempotencyTestApp.ThrowingCache>();
            }
        );
        using var client = IdempotencyTestApp.CreateClient(app);

        var response = await _Post(client, "/echo", key: "cache-throw", body: "hello");

        response
            .StatusCode.Should()
            .Be(HttpStatusCode.InternalServerError, "Throw mode rethrows cache exceptions to the host pipeline");
    }

    // ── Default cache key includes query string ──────────────────────────────

    [Fact]
    public async Task different_query_strings_with_same_key_and_body_should_not_cross_replay()
    {
        // Real-world endpoints branch on query parameters (?action=void vs ?action=capture,
        // ?dry_run=true vs ?dry_run=false). The default cache key omitted the query string, so
        // a client that reused an idempotency key across these would cross-replay the wrong
        // response. Query string is now part of the cache-key composition.
        await using var app = await IdempotencyTestApp.CreateAsync();
        using var client = IdempotencyTestApp.CreateClient(app);

        var first = await _Post(client, "/echo?action=void", key: "k-q", body: "hello");
        var second = await _Post(client, "/echo?action=capture", key: "k-q", body: "hello");

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        second.StatusCode.Should().Be(HttpStatusCode.Created);

        var firstBody = await first.Content.ReadAsStringAsync();
        var secondBody = await second.Content.ReadAsStringAsync();

        secondBody
            .Should()
            .NotBe(
                firstBody,
                "different query strings yield distinct cache slots; each invocation produces its own response"
            );
        second
            .Headers.Contains(HttpHeaderNames.IdempotentReplayed)
            .Should()
            .BeFalse("the second request must execute the handler, not replay the first");
    }

    // ── Winner lock lease decoupled from InFlightLockTimeout ─────────────────

    [Fact]
    public async Task winner_lock_lease_should_use_WinnerLockLease_option_not_InFlightLockTimeout_plus_5s()
    {
        // Regression: prior to this commit, the winner's lock lease was `InFlightLockTimeout + 5s`
        // (35s with defaults). Handlers running longer than that lost mutual exclusion when the
        // lease expired mid-handler. The lease is now an explicit option (WinnerLockLease,
        // default 5 min) decoupled from the loser's acquire timeout. This test asserts the
        // option value flows into TryAcquireAsync's timeUntilExpires argument.
        TimeSpan? winnerLeaseSeen = null;
        var configuredLease = TimeSpan.FromMinutes(10);
        var configuredAcquireTimeout = TimeSpan.FromSeconds(1);

        var lockProvider = new IdempotencyTestApp.InMemoryDistributedLockDouble(TimeProvider.System)
        {
            BeforeAcquireAsync = (_, timeUntilExpires, acquireTimeout, _) =>
            {
                // Winner's signature: acquireTimeout == TimeSpan.Zero. Capture the first.
                if (acquireTimeout == TimeSpan.Zero && winnerLeaseSeen is null)
                {
                    winnerLeaseSeen = timeUntilExpires;
                }
                return Task.CompletedTask;
            },
        };

        await using var app = await IdempotencyTestApp.CreateAsync(
            o =>
            {
                o.InFlightStrategy = InFlightStrategy.WaitAndReplay;
                o.InFlightLockTimeout = configuredAcquireTimeout;
                o.WinnerLockLease = configuredLease;
            },
            lockProvider: lockProvider
        );
        using var client = IdempotencyTestApp.CreateClient(app);

        var response = await _Post(client, "/echo", key: "lease-1", body: "hello");

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        winnerLeaseSeen
            .Should()
            .Be(
                configuredLease,
                "the winner's lock lease must come from WinnerLockLease, not InFlightLockTimeout + 5s"
            );
        // Sanity: old formula would have produced 6 seconds, which is observably different.
        winnerLeaseSeen.Should().NotBe(configuredAcquireTimeout + TimeSpan.FromSeconds(5));
    }

    // ── Lock-provider outage: same OnCacheError semantics as cache exceptions ──

    [Fact]
    public async Task should_pass_through_to_handler_when_lock_provider_throws_on_winner_path_and_OnCacheError_is_FailOpen()
    {
        // Winner-path TryAcquireAsync throws. Because the fix acquires the lock BEFORE inserting
        // the sentinel marker, no orphan record is left behind — FailOpen just bypasses
        // idempotency for this request.
        var lockProvider = new IdempotencyTestApp.InMemoryDistributedLockDouble(TimeProvider.System)
        {
            BeforeAcquireAsync = (_, _, acquireTimeout, _) =>
            {
                // Winner's signature: acquireTimeout == TimeSpan.Zero.
                if (acquireTimeout == TimeSpan.Zero)
                {
                    throw new InvalidOperationException("simulated lock-provider outage");
                }
                return Task.CompletedTask;
            },
        };

        await using var app = await IdempotencyTestApp.CreateAsync(
            o => o.InFlightStrategy = InFlightStrategy.WaitAndReplay,
            lockProvider: lockProvider
        );
        using var client = IdempotencyTestApp.CreateClient(app);

        var response = await _Post(client, "/echo", key: "lock-fail-open", body: "hello");

        response
            .StatusCode.Should()
            .Be(
                HttpStatusCode.Created,
                "FailOpen bypasses idempotency and runs the handler when the lock provider is down"
            );
    }

    [Fact]
    public async Task should_return_409_in_flight_timeout_when_lock_provider_throws_on_loser_path_and_OnCacheError_is_FailOpen()
    {
        // Loser-path TryAcquireAsync throws. We cannot wait for the winner and cannot call
        // next() (would re-invoke the handler). Return a recoverable 409 so the client retries.
        var gate = new IdempotencyTestApp.TestHandlerGate();
        var lockProvider = new IdempotencyTestApp.InMemoryDistributedLockDouble(TimeProvider.System)
        {
            BeforeAcquireAsync = (_, _, acquireTimeout, _) =>
            {
                // Throw only on the loser's call (acquireTimeout > 0).
                if (acquireTimeout is not null && acquireTimeout != TimeSpan.Zero)
                {
                    throw new InvalidOperationException("simulated lock-provider outage");
                }
                return Task.CompletedTask;
            },
        };

        await using var app = await IdempotencyTestApp.CreateAsync(
            o =>
            {
                o.InFlightStrategy = InFlightStrategy.WaitAndReplay;
                o.InFlightLockTimeout = TimeSpan.FromSeconds(5);
            },
            handlerGate: gate,
            lockProvider: lockProvider
        );
        using var client = IdempotencyTestApp.CreateClient(app);

        // Winner enters the handler (gated) holding the lock.
        var winnerTask = _Post(client, "/echo", key: "lock-loser-fail", body: "hello");
        await gate.WaitForInvocationsAsync(1, TimeSpan.FromSeconds(2));

        // Loser tries to acquire the lock — the hook throws.
        var loser = await _Post(client, "/echo", key: "lock-loser-fail", body: "hello");

        loser.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var loserBody = await loser.Content.ReadAsStringAsync();
        loserBody.Should().Contain("g:idempotency_in_flight_timeout");

        gate.Release();
        var winner = await winnerTask;
        winner.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    private static async Task<HttpResponseMessage> _Post(
        HttpClient client,
        string path,
        string key,
        string body,
        Dictionary<string, string>? extraHeaders = null
    )
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);

        request.Content = new StringContent(body);
        request.Headers.Add(HttpHeaderNames.IdempotencyKey, key);

        if (extraHeaders is not null)
        {
            foreach (var (name, value) in extraHeaders)
            {
                request.Headers.Add(name, value);
            }
        }

        // Disposed by caller via using
        return await client.SendAsync(request, cancellationToken: AbortToken);
    }
}
