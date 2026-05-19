# Headless.Api.Idempotency

Stripe-style HTTP idempotency middleware for ASP.NET Core. Cache full HTTP responses (status, allowlisted headers, byte body) on first execution and replay them byte-equivalent on identical retries.

## Problem Solved

Legacy "idempotency as a uniqueness guard" (409 on any duplicate key) makes the receipt unrecoverable: a client whose first response was lost on the wire retries with the same key and gets 409 instead of the original 201. This package implements the standard contract (Stripe, AWS, PayPal, Square, IETF `draft-ietf-httpapi-idempotency-key-header`):

- **Same key + same body** → replay original response (status, allowlisted headers, body bytes).
- **Same key + different body** → 422 Unprocessable Content (`g:idempotency-key-reused`).
- **Same key, original in flight** → 409 Conflict (`g:idempotency-in-flight`), or `WaitAndReplay` with a distributed lock.
- **New key** → execute fresh.

## Key Features

- Byte-equivalent replay of cached responses with `Idempotent-Replayed: true`.
- Two in-flight strategies: `Reject` (default, no extra dependencies) and `WaitAndReplay` (requires `IDistributedLockProvider`).
- Configurable body cap with `Reject` (413) or `PassThrough` behaviors.
- Header allowlist filters `Set-Cookie`, `traceparent`, and other sensitive or per-request headers from cached responses.
- Tenant-aware default cache key: `idem:{tenant}:{METHOD}:{path}:{key}`.
- Per-endpoint overrides via `.WithIdempotency(o => ...)`.
- Custom hooks: `KeyDeriver`, `RequestFingerprint`, `ShouldApply`, `ShouldCacheResponse`.
- Default cache predicate: 2xx + selected 4xx; never 5xx, 1xx, 3xx, or transient 4xx (408/425/429).
- Startup-time DI validation: `WaitAndReplay` without `IDistributedLockProvider` fails fast.

## Installation

```bash
dotnet add package Headless.Api.Idempotency
```

## Quick Start

```csharp
using Headless.Api.Idempotency;
using Microsoft.AspNetCore.Builder;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInMemoryCache();           // or AddRedisCache(...)
builder.Services.AddIdempotency(o =>
{
    o.IdempotencyKeyExpiration = TimeSpan.FromHours(24);
    o.InFlightStrategy = InFlightStrategy.Reject;
});

var app = builder.Build();

app.UseAuthentication();
app.UseHeadlessTenancy();   // tenant must be resolved before idempotency
app.UseAuthorization();
app.UseIdempotency();       // place AFTER auth and tenancy

app.MapPost("/disbursements", CreateDisbursement);
app.Run();
```

## Configuration

| Property | Default | Purpose |
| --- | --- | --- |
| `IdempotencyKeyExpiration` | 24 hours | TTL for completed records. |
| `HeaderName` | `X-Idempotency-Key` | Request header carrying the key. |
| `Methods` | POST, PUT, PATCH, DELETE | HTTP methods that participate in idempotency. |
| `InFlightStrategy` | `Reject` | `Reject` returns 409 on concurrent same-key requests. `WaitAndReplay` blocks on a distributed lock and replays the winner. |
| `InFlightLockTimeout` | 30s | Lock-acquisition timeout for `WaitAndReplay`. |
| `MaxBodySizeForHashing` | 1 MiB | Maximum body size eligible for fingerprinting. |
| `OversizeBehavior` | `Reject` | `Reject` returns 413 (`g:idempotency-body-too-large`). `PassThrough` runs the handler without idempotency guarantees. |
| `MismatchStatusCode` | 422 | Status code for fingerprint mismatch. Must be 409 or 422. |
| `ReplayHeaderAllowlist` | Content-Type, Content-Language, Content-Encoding, Content-Disposition, Location, Link, ETag, Last-Modified, Cache-Control, Vary | Response headers copied into the cached record. |
| `ShouldCacheResponse` | `DefaultCachePredicate.Instance` | Predicate deciding whether a completed response is cached. |
| `ShouldApply` | `null` | Per-request opt-out hook. |
| `KeyDeriver` | tenant + method + path | Custom cache-key derivation. |
| `RequestFingerprint` | SHA-256 of buffered body | Custom fingerprint computation. |

### Per-endpoint overrides

```csharp
app.MapPost("/webhooks", HandleWebhook)
   .WithIdempotency(o =>
   {
       o.IdempotencyKeyExpiration = TimeSpan.FromDays(7);
       o.MismatchStatusCode = StatusCodes.Status409Conflict;
   });
```

`HeaderName` is the structural exception — the middleware reads the request header before resolving endpoint metadata, so per-endpoint `HeaderName` overrides are ignored.

## Dependencies

- `Headless.Api.Abstractions`
- `Headless.Api.Core`
- `Headless.Caching.Abstractions` (you supply the implementation: in-memory, Redis, etc.)
- `Headless.DistributedLocks.Abstractions` (required only when using `InFlightStrategy.WaitAndReplay`)
- `Headless.Core`
- `Headless.FluentValidation`
- `Headless.Hosting`
- `Microsoft.AspNetCore.App` (framework reference)

## Side Effects

- Reads `ICurrentTenant.Id` for cache-key composition; register `UseIdempotency()` AFTER tenant resolution and authorization so unauthenticated requests do not allocate cache slots.
- Buffers the request body up to `MaxBodySizeForHashing + 1` bytes via `HttpRequest.EnableBuffering`.
- On replay, writes `Idempotent-Replayed: true` to the response.
- On cache miss, inserts an `InFlight` marker before invoking the handler, then upserts the `Complete` record afterward; the marker TTL is `InFlightLockTimeout + 5s` so a crashed handler unblocks retries.

## Boundary Doctrine

This is HTTP middleware, not a Mediator pipeline behavior. For the four structural reasons an `IdempotencyBehavior<,>` is rejected, see [`docs/llms/mediator.md`](../../docs/llms/mediator.md).
