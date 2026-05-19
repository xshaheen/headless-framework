// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Api.Idempotency;
using Headless.Constants;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

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
