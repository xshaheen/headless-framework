# Headless.Api.Idempotency

Stripe-style HTTP idempotency middleware for ASP.NET Core. Cache full HTTP responses (status, allowlisted headers, byte body) on first execution and replay them byte-equivalent on identical retries.

## Problem Solved

Legacy "idempotency as a uniqueness guard" (409 on any duplicate key) makes the receipt unrecoverable: a client whose first response was lost on the wire retries with the same key and gets 409 instead of the original 201. This package implements the standard contract (Stripe, AWS, PayPal, Square, IETF `draft-ietf-httpapi-idempotency-key-header`):

- **Same key + same body** → replay original response (status, allowlisted headers, body bytes) with `Idempotent-Replayed: true`
- **Same key + different body** → 422 Unprocessable Content (`g:idempotency_key_reused`)
- **Same key, original in-flight** → 409 Conflict (`g:idempotency_in_flight`), or `WaitAndReplay` with a distributed lock
- **Same key, lock acquisition timed out under `WaitAndReplay`** → 409 Conflict (`g:idempotency_in_flight_timeout`)
- **`Idempotency-Key` header malformed** (length over 255, control characters, multi-valued) → 400 Bad Request (`g:idempotency_key_malformed`)
- **New key** → execute fresh

## Key Features

- Byte-equivalent replay of cached responses
- Two in-flight strategies: `InFlightStrategy.Reject` (default, no extra dependencies) and `InFlightStrategy.WaitAndReplay` (requires `IDistributedLock`)
- Independent request-body memory threshold and fingerprinting cap, with `OversizeBehavior.Reject` (413) or `OversizeBehavior.PassThrough` behaviors
- Header allowlist filters `Set-Cookie`, `traceparent`, and other sensitive or per-request headers from cached responses
- Tenant-aware default cache key: `idem:{tenant}:{userId}:{METHOD}:{path}{?query}:{key}`
- Per-endpoint overrides via `.WithIdempotency(o => ...)`
- Custom hooks: `KeyDeriver`, `RequestFingerprint`, `ShouldApply`, `ShouldCacheResponse`
- Default cache predicate: 2xx + selected 4xx; never 5xx, 1xx, 3xx, or transient 4xx (408/425/429)
- Startup-time DI validation: `WaitAndReplay` without `IDistributedLock` fails fast with `OptionsValidationException`
- `IdempotencyErrorCodes` static class: `KeyReused`, `InFlight`, `InFlightTimeout`, `BodyTooLarge`, `KeyMalformed` as `public const string`

## Design Notes

The middleware uses lock-before-insert ordering under `WaitAndReplay`: the winner acquires the distributed lock **before** inserting the `InFlight` sentinel marker. Inserting the marker first creates a window where an arriving loser sees the marker, grabs the lock before the winner, then blocks — leaving the winner unlocked and the loser stuck until timeout. Lock-before-insert closes that window.

`HeaderName` per-endpoint overrides via `.WithIdempotency()` are silently ignored — the middleware reads the request header before resolving endpoint metadata. Change `HeaderName` globally via `AddIdempotency(o => o.HeaderName = ...)` only.

Place `UseResponseCompression()` **before** `UseIdempotency()` in the pipeline. Compression middleware registered inside idempotency records compressed bytes in the cache; replaying those bytes without re-encoding them produces garbled or double-encoded responses.

`RequestBodyBufferThreshold` controls when request buffering spills from memory to a temporary file; `MaxBodySizeForHashing` independently controls which bodies are eligible for idempotency. The default remains 1 MiB + 1 byte: corrected non-seekable request-body benchmarks showed that 30/64/128 KiB thresholds reduced managed allocations but missed the latency gate at concurrency 1/32/128. Lower it only after measuring the memory, temporary-file I/O, and latency trade-off under representative concurrency.

## Installation

```bash
dotnet add package Headless.Api.Idempotency
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHeadlessCaching(setup => setup.UseInMemory()); // or setup.UseRedis(...)
builder.Services.AddIdempotency(o =>
{
    o.IdempotencyKeyExpiration = TimeSpan.FromHours(24);
    o.InFlightStrategy = InFlightStrategy.Reject;
});

var app = builder.Build();

app.UseAuthentication();
app.UseHeadlessTenancy(); // tenant must be resolved before idempotency
app.UseAuthorization();
app.UseResponseCompression(); // must be OUTSIDE (before) UseIdempotency
app.UseIdempotency(); // place AFTER auth and tenancy

app.MapPost("/disbursements", CreateDisbursement);
app.Run();
```

Per-endpoint overrides:

```csharp
app.MapPost("/webhooks", HandleWebhook)
    .WithIdempotency(o =>
    {
        o.IdempotencyKeyExpiration = TimeSpan.FromDays(7);
        o.MismatchStatusCode = StatusCodes.Status409Conflict;
    });
```

## Configuration

| Property | Default | Purpose |
|----------|---------|---------|
| `IdempotencyKeyExpiration` | 24 hours | TTL for completed records. |
| `HeaderName` | `Idempotency-Key` | Request header carrying the key. |
| `Methods` | POST, PUT, PATCH, DELETE | HTTP methods that participate in idempotency. GET is never valid. |
| `InFlightStrategy` | `Reject` | `Reject` returns 409 on concurrent same-key requests. `WaitAndReplay` blocks on a distributed lock and replays the winner. |
| `InFlightLockTimeout` | 30s | Lock-acquisition timeout for `WaitAndReplay`. Validator-capped at 1 minute. |
| `WinnerLockLease` | 5 minutes | Lease duration for the winner's distributed lock under `WaitAndReplay`. Must be >= `InFlightLockTimeout`. Capped at 1 hour. |
| `MaxBodySizeForHashing` | 1 MiB | Maximum body size eligible for fingerprinting. Capped at 64 MiB. |
| `RequestBodyBufferThreshold` | 1 MiB + 1 byte | Request bytes retained in memory before buffering spills to a temporary file. Capped at 64 MiB + 1 byte. |
| `OversizeBehavior` | `Reject` | `Reject` returns 413 (`g:idempotency_body_too_large`). `PassThrough` runs the handler without idempotency guarantees. |
| `OnCacheError` | `FailOpen` | `FailOpen` logs a warning and bypasses idempotency for pre-handler cache failures; post-handler finalize failures remove the marker and preserve the handler response. `Throw` propagates the exception as 5xx. |
| `RequireUserIdentity` | `true` | When `true`, the default cache key requires an authenticated user; tenant-only anonymous requests pass through. Set `false` for webhook receivers / OAuth callbacks. |
| `MismatchStatusCode` | 422 | Status code for fingerprint mismatch. Must be 409 or 422. |
| `ReplayHeaderAllowlist` | Content-Type, Content-Language, Content-Encoding, Content-Disposition, Location, Link, ETag, Last-Modified, Cache-Control, Vary | Response headers copied into the cached record. `Set-Cookie` and `traceparent` are excluded by design. |
| `ShouldCacheResponse` | `DefaultCachePredicate.Instance` | Predicate deciding whether a completed response is cached. |
| `ShouldApply` | `null` | Per-request opt-out hook. |
| `KeyDeriver` | `null` (uses `idem:{tenant}:{userId}:{METHOD}:{path}{?query}:{key}`) | Custom cache-key derivation. |
| `RequestFingerprint` | `null` (uses SHA-256 of buffered body) | Custom fingerprint computation. Must return non-empty bytes. |

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

- Reads `ICurrentTenant.Id` and authenticated `ICurrentUser.UserId` for cache-key composition; when both are absent and no `KeyDeriver` is configured, the middleware passes through without applying idempotency.
- Buffers the request body via `HttpRequest.EnableBuffering`; bytes beyond `RequestBodyBufferThreshold` spill to a temporary file.
- On replay, writes `Idempotent-Replayed: true` to the response. Pre-existing allowlisted response headers set by upstream middleware are removed before captured headers are written for byte-equivalent replay.
- On cache miss, inserts an `InFlight` sentinel marker before invoking the handler, then promotes it to the `Complete` record afterward using compare-and-swap (`TryReplaceIfEqualAsync`). The marker uses the same TTL as `IdempotencyKeyExpiration`.
- When the **response** body exceeds `MaxBodySizeForHashing` (`captureStream.TruncatedCapture`), the completed record is not stored and replay does not apply. `OversizeBehavior` controls **request**-body handling only.
