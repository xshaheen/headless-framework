// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Api.Idempotency;
using Headless.Constants;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

// CA2025: `_Post` builds an `HttpRequestMessage` under `using var` and awaits `SendAsync` inline,
// so the request disposes only after the SendAsync task completes. Concurrency tests store the
// returned Task to interleave with another request, which the analyzer cannot see is safe.
#pragma warning disable CA2025

namespace Tests;

public sealed class IdempotencyEndToEndTests
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

        var firstBody = await first.Content.ReadAsStringAsync();
        var secondBody = await second.Content.ReadAsStringAsync();

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

        using var request = new HttpRequestMessage(HttpMethod.Post, "/echo")
        {
            Content = new StringContent("hello"),
        };
        using var response = await client.SendAsync(request);

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

        async Task<HttpResponseMessage> postForTenant(string tenant) =>
            await _Post(client, "/echo", key: "k1", body: "same-body", extraHeaders: new(StringComparer.Ordinal) { [tenantHeader] = tenant });

        var tenantA = await postForTenant("TENANT-A");
        var tenantB = await postForTenant("TENANT-B");

        tenantA.StatusCode.Should().Be(HttpStatusCode.Created);
        tenantB.StatusCode.Should().Be(HttpStatusCode.Created);
        // Both should be fresh handler invocations
        tenantA.Headers.Contains(HttpHeaderNames.IdempotentReplayed).Should().BeFalse();
        tenantB.Headers.Contains(HttpHeaderNames.IdempotentReplayed).Should().BeFalse();

        // Per-invocation GUID in body proves each tenant ran the handler independently
        var bodyA = await tenantA.Content.ReadAsStringAsync();
        var bodyB = await tenantB.Content.ReadAsStringAsync();
        bodyA.Should().NotBe(bodyB);
    }

    // ── AE12: per-endpoint metadata ──────────────────────────────────────────

    [Fact]
    public async Task per_endpoint_with_idempotency_metadata_should_apply_overrides()
    {
        await using var app = await IdempotencyTestApp.CreateAsync(
            mapAdditionalEndpoints: a =>
            {
                a.MapPost("/strict", (HttpContext ctx) =>
                {
                    ctx.Response.StatusCode = StatusCodes.Status201Created;
                    return Task.CompletedTask;
                }).WithIdempotency(o => o.MismatchStatusCode = StatusCodes.Status409Conflict);
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
        var loserBody = await loser.Content.ReadAsStringAsync();
        loserBody.Should().Contain("g:idempotency_in_flight");

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
            o => { o.InFlightStrategy = InFlightStrategy.WaitAndReplay; o.InFlightLockTimeout = TimeSpan.FromSeconds(10); },
            withLockProvider: true,
            handlerGate: gate);
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

        var winnerBody = await winner.Content.ReadAsStringAsync();
        var loserBody = await loser.Content.ReadAsStringAsync();
        loserBody.Should().Be(winnerBody, "replay must be byte-equivalent to the original");

        gate.InvocationCount.Should().Be(1, "WaitAndReplay never invokes the handler twice for the same key");
    }

    [Fact]
    public async Task wait_and_replay_should_409_with_in_flight_timeout_when_winner_does_not_finish_before_acquire_timeout()
    {
        var gate = new IdempotencyTestApp.TestHandlerGate();
        await using var app = await IdempotencyTestApp.CreateAsync(
            o => { o.InFlightStrategy = InFlightStrategy.WaitAndReplay; o.InFlightLockTimeout = TimeSpan.FromMilliseconds(250); },
            withLockProvider: true,
            handlerGate: gate);
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

    private static async Task<HttpResponseMessage> _Post(
        HttpClient client,
        string path,
        string key,
        string body,
        Dictionary<string, string>? extraHeaders = null
    )
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(body),
        };
        request.Headers.Add(HttpHeaderNames.IdempotencyKey, key);

        if (extraHeaders is not null)
        {
            foreach (var (name, value) in extraHeaders)
            {
                request.Headers.Add(name, value);
            }
        }

        // Disposed by caller via using
        return await client.SendAsync(request);
    }
}
