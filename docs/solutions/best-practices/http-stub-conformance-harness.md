---
title: HTTP-Stub Cross-Provider Conformance Harness
date: 2026-06-21
category: best-practices
module: headless-captcha
problem_type: best_practice
component: testing_framework
severity: medium
related_components:
  - tooling
  - service_class
applies_when:
  - "Building cross-provider conformance for a feature backed by an external HTTP API"
  - "The provider happy path needs a human-solved or credential-gated input CI cannot supply"
  - "Adding a second provider to an abstraction whose verification path is an outbound HTTP call"
tags:
  - testing
  - conformance-harness
  - http-stub
  - httpmessagehandler
  - captcha
  - cross-provider
---

# HTTP-Stub Cross-Provider Conformance Harness

## Context

The repo's `*.Tests.Harness` convention (see CLAUDE.md "When to create a `*.Tests.Harness` package") was written around storage/infra features whose backends are reachable in CI — every existing harness (Blobs, DistributedLocks, Orm, Messaging, Jobs.EF) spins a real container via Testcontainers and runs the same conformance contract against each provider.

The CAPTCHA feature triggered the same two-provider/one-contract rule (reCAPTCHA + Turnstile behind `ICaptchaVerifier`) but breaks the Testcontainers assumption: the providers verify a token by POSTing to a vendor `siteverify` endpoint, and a *successful* verification requires a token produced by a human solving a live widget against a real site key. There is no container to stand up and no way to manufacture a passing token in CI. The verification surface is otherwise a perfect conformance candidate — identical request/response shape, identical failure modes — so the contract is worth testing once across providers; only the transport needs faking.

The answer is a harness of the same *shape* (abstract conformance base + per-provider fixture) but driven by a stubbed `HttpMessageHandler` instead of a container. This doc captures that shape so the next external-HTTP-backed feature reaches for it instead of either skipping conformance or inventing a one-off.

## Guidance

Stub the transport, not the world. Keep the harness driving the **real** DI graph and HTTP pipeline; only the innermost `HttpMessageHandler` is fake.

**1. A scripted `HttpMessageHandler`** owns the canned responses and captures the request. Queue responses in order, repeat the last one when the queue drains (so a resilience handler's retries see a stable answer), and honor cancellation *before* doing any work so a pre-cancelled token throws without "completing" a call.

```csharp
public sealed class StubSiteVerifyHandler : HttpMessageHandler
{
    private readonly Queue<(HttpStatusCode Status, string Body)> _responses = new();
    private (HttpStatusCode Status, string Body)? _last;

    public string? LastRequestBody { get; private set; }
    public int InvocationCount { get; private set; }

    public StubSiteVerifyHandler EnqueueJson(HttpStatusCode status, string body)
    {
        _responses.Enqueue((status, body));
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();          // pre-cancellation: assert InvocationCount == 0
        InvocationCount++;
        if (request.Content is not null)
            LastRequestBody = await request.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var chosen = _responses.Count > 0 ? _responses.Dequeue() : _last
            ?? throw new InvalidOperationException("No stubbed response enqueued.");
        _last = chosen;
        return new HttpResponseMessage(chosen.Status) { Content = new StringContent(chosen.Body, Encoding.UTF8, "application/json") };
    }
}
```

**2. Wire the stub through the real builder**, not a hand-constructed verifier. Call the production `AddHeadless{Feature}(...)`, then override only the named client's primary handler. This exercises the actual options binding, keyed registration, and `AddStandardResilienceHandler` pipeline:

```csharp
services.AddHeadlessCaptcha(b => b.UseTurnstile(o => { o.SiteKey = "k"; o.SiteSecret = "s"; }));
services.AddHttpClient(CaptchaConstants.TurnstileProvider).ConfigurePrimaryHttpMessageHandler(() => stub);
```

**3. An abstract generic conformance base** carries the cross-provider scenarios and satisfies xUnit v3's `IClassFixture<TFixture>`. Put `[Fact]` directly on the base methods — xUnit v3 discovers inherited facts on a concrete derived class, so each provider's suite is a two-line subclass (no per-method `override` boilerplate):

```csharp
public abstract class CaptchaVerifierConformanceTests<TFixture>(TFixture fixture)
    : TestBase, IClassFixture<TFixture> where TFixture : class, ICaptchaVerifierFixture
{
    [Fact] public async Task valid_token_returns_success_with_common_fields() { ... }
    // rejection, HTTP failure, null-body, pre-cancellation, conditional remoteip ...
}

// per provider — two lines, inherits all scenarios:
public sealed class TurnstileConformanceTests(TurnstileVerifierFixture fixture)
    : CaptchaVerifierConformanceTests<TurnstileVerifierFixture>(fixture);
```

**4. A per-provider fixture** supplies a verifier wired over the stub plus that vendor's JSON shapes (the base asserts on the *normalized* result, so each provider feeds its own wire body):

```csharp
public interface ICaptchaVerifierFixture
{
    ICaptchaVerifier CreateVerifier(StubSiteVerifyHandler handler);
    string SuccessResponseBody { get; }   // vendor-shaped, includes hostname + challenge_ts
    string RejectedResponseBody { get; }  // vendor-shaped, includes error codes
}
```

**The contract the base enforces** (the portable scenarios): valid token → success with common fields; well-formed rejection → `Success == false` with error codes, no throw; non-success HTTP status → `HttpRequestException`; null/`"null"` body → `InvalidOperationException`; pre-cancelled token → `OperationCanceledException` with no HTTP call; optional fields (e.g. `remoteip`) present in the form only when supplied. Provider-specific behavior (Turnstile `idempotency_key`/`cdata`, reCAPTCHA v3 score) stays in that provider's own `*.Tests.Unit`, not the base.

## Why This Matters

- **Conformance without reachability.** The cross-provider contract (round-trip, rejection, failure, cancellation, field encoding) is the part most worth testing once and reusing; it does not need a live backend. Faking only the transport keeps that value while sidestepping the human-solved-token wall.
- **Tests the wiring, not a mock of it.** Routing through `AddHeadless{Feature}` + `ConfigurePrimaryHttpMessageHandler` means options binding, keyed resolution, and the resilience pipeline are all under test. A hand-built `new Verifier(...)` would pass while the real DI graph is broken.
- **Resilience-handler gotcha.** `AddStandardResilienceHandler` retries transient 5xx with backoff. For the "non-success HTTP status throws" scenario, enqueue a **400** (not retried) so it is single-call and fast; a 500 would retry several times with backoff and slow the suite (the sticky-last-response stub keeps it correct either way).
- **Low subclass cost.** `[Fact]` on the abstract base + inherited discovery means a new provider gets the entire conformance suite for two lines, which keeps the marginal cost of the *next* provider near zero — the whole point of a harness.

## When to Apply

- A feature has 2+ providers behind one abstraction whose operation is an **outbound HTTP call** to a vendor endpoint.
- The provider happy path needs an input CI cannot manufacture (human-solved challenge, credential-gated handshake, paid live call).
- You want the cross-provider contract verified deterministically and offline.

Prefer the Testcontainers harness shape instead when the backend *is* reachable in CI (databases, brokers, object stores). This HTTP-stub shape **complements** the CLAUDE.md harness trigger; it does not replace it. Skip a harness entirely when there is only one provider and no portable contract to share.

## Examples

reCAPTCHA and Turnstile both derive the same base with a two-line subclass and their own vendor JSON:

```csharp
// Turnstile success body (Cloudflare shape)
"""{"success":true,"challenge_ts":"2026-06-21T10:00:00Z","hostname":"example.com","cdata":"s-1"}"""

// reCAPTCHA v3 success body (Google shape) — same normalized result, extra score
"""{"success":true,"challenge_ts":"2026-06-21T10:00:00Z","hostname":"example.com","score":0.9,"action":"login"}"""
```

Both produce a `CaptchaVerifyResult` the base asserts on identically; the v3 score lives on the provider's own `ReCaptchaV3ScoreTests`. Source: `tests/Headless.Captcha.Tests.Harness` (`StubSiteVerifyHandler`, `ICaptchaVerifierFixture`, `CaptchaVerifierConformanceTests<TFixture>`) consumed by `tests/Headless.Captcha.{Turnstile,ReCaptcha}.Tests.Unit`.

## Related

- [Unified Provider Setup Builder Pattern](../architecture-patterns/unified-provider-setup-builder-pattern.md) — the provider topology this harness conforms across (captcha is its per-slot named-instance instance)
- CLAUDE.md "When to create a `*.Tests.Harness` package" — the Testcontainers-shaped trigger this complements; existing contrast harnesses live in `tests/Headless.Blobs.Tests.Harness` and `tests/Headless.DistributedLocks.Tests.Harness`
- Source-of-truth brainstorm and plan:
  - `docs/brainstorms/2026-06-21-captcha-provider-split-turnstile-requirements.md`
  - `docs/plans/2026-06-21-001-feat-captcha-provider-turnstile-plan.md`
