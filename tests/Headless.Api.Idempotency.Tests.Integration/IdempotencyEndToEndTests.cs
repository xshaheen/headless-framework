// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Api.Idempotency;
using Headless.Constants;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;

// CA2025: `_Post` builds an `HttpRequestMessage` under `using var` and awaits `SendAsync` inline,
// so the request disposes only after the SendAsync task completes. Concurrency tests store the
// returned Task to interleave with another request, which the analyzer cannot see is safe.
#pragma warning disable CA2025

namespace Tests;

/// <summary>
/// End-to-end coverage of the idempotency middleware against a real PostgreSQL-backed durable store: admission,
/// replay, in-flight handling, response-status release, and the handler-visible admission context. The provider
/// conformance suites (<c>Headless.Idempotency.PostgreSql.Tests.Integration</c>) already cover the store's own
/// semantics in depth; this suite proves the HTTP adapter composes correctly with a real store instead of a mock.
/// </summary>
[Collection<ApiIdempotencyPostgreSqlFixture>]
public sealed class IdempotencyEndToEndTests(ApiIdempotencyPostgreSqlFixture fixture) : TestBase
{
    private Task<WebApplication> _CreateAppAsync(
        Action<IdempotencyOptions>? configure = null,
        IdempotencyTestApp.TestHandlerGate? handlerGate = null
    )
    {
        return IdempotencyTestApp.CreateAsync(fixture.ConfigureStore, configure, handlerGate: handlerGate);
    }

    // ── store + replay ─────────────────────────────────────────────────────────

    [Fact]
    public async Task should_replay_cached_response_on_identical_retry()
    {
        var key = _UniqueKey();
        await using var app = await _CreateAppAsync();
        using var client = IdempotencyTestApp.CreateClient(app);

        var first = await _Post(client, "/echo", key: key, body: "hello");
        var second = await _Post(client, "/echo", key: key, body: "hello");

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        second.StatusCode.Should().Be(HttpStatusCode.Created);

        var firstBody = await first.Content.ReadAsStringAsync(AbortToken);
        var secondBody = await second.Content.ReadAsStringAsync(AbortToken);

        // The handler embeds a fresh GUID per invocation; identical bodies across two retries
        // means the handler ran exactly once and the second request replayed the stored bytes.
        secondBody.Should().Be(firstBody);
        first.Headers.Contains(HttpHeaderNames.IdempotentReplayed).Should().BeFalse();
        second.Headers.GetValues(HttpHeaderNames.IdempotentReplayed).Should().ContainSingle().Which.Should().Be("true");
    }

    // ── concurrent in-flight: Reject ────────────────────────────────────────────

    [Fact]
    public async Task should_invoke_handler_once_and_409_the_loser_when_concurrent_requests_with_reject_strategy()
    {
        var key = _UniqueKey();
        var gate = new IdempotencyTestApp.TestHandlerGate();
        await using var app = await _CreateAppAsync(handlerGate: gate);
        using var client = IdempotencyTestApp.CreateClient(app);

        // Fire the winner first so it holds the lease, then wait for it to enter the handler
        // before firing the loser. This guarantees the loser observes InFlight.
        var winnerTask = _Post(client, "/echo", key: key, body: "hello");
        await gate.WaitForInvocationsAsync(1, TimeSpan.FromSeconds(5));
        var loserTask = _Post(client, "/echo", key: key, body: "hello");

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

        gate.Release();
        var winner = await winnerTask;
        winner.StatusCode.Should().Be(HttpStatusCode.Created);

        gate.InvocationCount.Should().Be(1, "Reject rejects the loser without invoking the handler");
    }

    // ── concurrent in-flight: WaitAndReplay ─────────────────────────────────────

    [Fact]
    public async Task should_block_loser_until_winner_completes_then_replay_when_concurrent_requests_with_wait_and_replay()
    {
        var key = _UniqueKey();
        var gate = new IdempotencyTestApp.TestHandlerGate();
        await using var app = await _CreateAppAsync(
            o =>
            {
                o.InFlightStrategy = InFlightStrategy.WaitAndReplay;
                o.InFlightLockTimeout = TimeSpan.FromSeconds(15);
            },
            handlerGate: gate
        );
        using var client = IdempotencyTestApp.CreateClient(app);

        var winnerTask = _Post(client, "/echo", key: key, body: "hello");
        await gate.WaitForInvocationsAsync(1, TimeSpan.FromSeconds(5));

        var loserTask = _Post(client, "/echo", key: key, body: "hello");
        // The loser must NOT complete while the winner is gated — it polls the store instead.
        await Task.Delay(500, AbortToken);
        loserTask.IsCompleted.Should().BeFalse("loser is waiting on the winner's admission");

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

    // ── 5xx release + retry ─────────────────────────────────────────────────────

    [Fact]
    public async Task should_release_and_rerun_handler_when_response_is_5xx()
    {
        var key = _UniqueKey();
        await using var app = await _CreateAppAsync();
        using var client = IdempotencyTestApp.CreateClient(app);

        var first = await _Post(client, "/status?code=503", key: key, body: "");
        first.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        first.Headers.Contains(HttpHeaderNames.IdempotentReplayed).Should().BeFalse();

        // A 5xx releases the admitted lease, so an immediate retry is admitted fresh (not InFlight, not Replay).
        var second = await _Post(client, "/status?code=503", key: key, body: "");
        second.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        second.Headers.Contains(HttpHeaderNames.IdempotentReplayed).Should().BeFalse();
    }

    // ── handler sees IIdempotencyContext ────────────────────────────────────────

    [Fact]
    public async Task should_expose_key_and_lease_to_the_handler_through_idempotency_context()
    {
        var key = _UniqueKey();
        await using var app = await _CreateAppAsync();
        using var client = IdempotencyTestApp.CreateClient(app);

        var response = await _Post(client, "/context", key: key, body: "");

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var storeKey = response.Headers.GetValues("X-Idempotency-Key").Should().ContainSingle().Which;
        storeKey.Should().HaveLength(64).And.MatchRegex("^[0-9a-f]{64}$");
        response.Headers.GetValues("X-Idempotency-Lease-Resource").Should().ContainSingle().Which.Should().Be(storeKey);
        response
            .Headers.GetValues("X-Idempotency-Takeover")
            .Should()
            .ContainSingle()
            .Which.Should()
            .Be("False", "the first admission of a key is never a takeover");
    }

    /// <summary>A fresh key per test so tests sharing the fixture's database never collide on the same admission.</summary>
    private static string _UniqueKey()
    {
        return $"k-{Guid.NewGuid():N}";
    }

    private static async Task<HttpResponseMessage> _Post(HttpClient client, string path, string key, string body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);

        request.Content = new StringContent(body);
        request.Headers.Add(HttpHeaderNames.IdempotencyKey, key);

        // Disposed by caller via using
        return await client.SendAsync(request, cancellationToken: AbortToken);
    }
}
