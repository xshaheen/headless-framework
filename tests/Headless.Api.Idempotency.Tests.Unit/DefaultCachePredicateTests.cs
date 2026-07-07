// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api.Idempotency;
using Microsoft.AspNetCore.Http;

namespace Tests;

public sealed class DefaultCachePredicateTests
{
    private static DefaultHttpContext _Context(int statusCode)
    {
        var ctx = new DefaultHttpContext();
        ctx.Response.StatusCode = statusCode;
        return ctx;
    }

    // ── 2xx: all cacheable ────────────────────────────────────────────────

    [Theory]
    [InlineData(200)]
    [InlineData(201)]
    [InlineData(202)]
    [InlineData(204)]
    [InlineData(206)]
    [InlineData(299)]
    public void should_return_true_for_2xx(int status)
    {
        DefaultCachePredicate.Instance(_Context(status)).Should().BeTrue();
    }

    // ── 4xx cacheable: 400, 405, 409, 410, 411, 412, 413, 414, 415, 416, 422, 451 ──

    [Theory]
    [InlineData(400)]
    [InlineData(405)]
    [InlineData(409)]
    [InlineData(410)]
    [InlineData(411)]
    [InlineData(412)]
    [InlineData(413)]
    [InlineData(414)]
    [InlineData(415)]
    [InlineData(416)]
    [InlineData(422)]
    [InlineData(451)]
    public void should_return_true_for_cacheable_4xx(int status)
    {
        DefaultCachePredicate.Instance(_Context(status)).Should().BeTrue();
    }

    // ── 4xx transient: 408, 425, 429 → not cached ─────────────────────────

    [Theory]
    [InlineData(408)]
    [InlineData(425)]
    [InlineData(429)]
    public void should_return_false_for_transient_4xx(int status)
    {
        DefaultCachePredicate.Instance(_Context(status)).Should().BeFalse();
    }

    // ── 4xx auth/not-found: 401, 403, 404 → not cached (transient w.r.t. idempotency) ──

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    public void should_return_false_for_auth_and_not_found_4xx(int status)
    {
        DefaultCachePredicate.Instance(_Context(status)).Should().BeFalse();
    }

    // ── 4xx other (conservative): not cached ──────────────────────────────

    [Theory]
    [InlineData(402)]
    [InlineData(406)]
    [InlineData(407)]
    [InlineData(417)]
    [InlineData(418)]
    [InlineData(420)]
    [InlineData(421)]
    [InlineData(423)]
    [InlineData(424)]
    [InlineData(426)]
    [InlineData(428)]
    [InlineData(431)]
    [InlineData(444)]
    [InlineData(449)]
    [InlineData(450)]
    [InlineData(499)]
    public void should_return_false_for_other_4xx(int status)
    {
        DefaultCachePredicate.Instance(_Context(status)).Should().BeFalse();
    }

    // ── 5xx: not cached ───────────────────────────────────────────────────

    [Theory]
    [InlineData(500)]
    [InlineData(501)]
    [InlineData(502)]
    [InlineData(503)]
    [InlineData(504)]
    [InlineData(599)]
    public void should_return_false_for_5xx(int status)
    {
        DefaultCachePredicate.Instance(_Context(status)).Should().BeFalse();
    }

    // ── 1xx and 3xx: not cached ───────────────────────────────────────────

    [Theory]
    [InlineData(100)]
    [InlineData(101)]
    [InlineData(102)]
    [InlineData(199)]
    public void should_return_false_for_1xx(int status)
    {
        DefaultCachePredicate.Instance(_Context(status)).Should().BeFalse();
    }

    [Theory]
    [InlineData(300)]
    [InlineData(301)]
    [InlineData(302)]
    [InlineData(304)]
    [InlineData(307)]
    [InlineData(308)]
    [InlineData(399)]
    public void should_return_false_for_3xx(int status)
    {
        DefaultCachePredicate.Instance(_Context(status)).Should().BeFalse();
    }
}
