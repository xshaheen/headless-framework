---
date: 2026-05-19
topic: idempotency-middleware-rewrite
issue: https://github.com/xshaheen/headless-framework/issues/280
---

# API: Rewrite `IdempotencyMiddleware` as Stripe-style replay (in new `Headless.Api.Idempotency` package)

## Summary

Replace the current guard-style `IdempotencyMiddleware` (returns 409 on any duplicate key) with Stripe-style replay semantics: cache the full HTTP response on first execution, replay it byte-equivalent on retry with the same key and body, return a fingerprint-mismatch error only when the same key arrives with a different body. The implementation moves out of `Headless.Api.Core` into a new dedicated `Headless.Api.Idempotency` package — matching the framework's existing `Headless.Api.*` factoring discipline (`Versioning`, `FluentValidation`). Backed directly by `ICache` — no new `IIdempotencyStore` abstraction, no EF storage package in v1; the extraction-vs-abstraction calls are independent and we defer the seam until a second backend forces it. Concurrent in-flight returns 409 by default with an opt-in wait-and-replay strategy. Status-code and header replay are governed by configurable allowlists with safe defaults. Cache key composition, request fingerprinting, and per-endpoint option overrides are all consumer-customizable through delegate hooks and endpoint metadata. Cache key is composed of `(tenant, method, path, idempotency-key)` by default. Response carries `Idempotent-Replayed: true` on cache hits. The Mediator `IdempotencyBehavior<,>` pattern is explicitly rejected for the same structural reasons as the deprecated `TenantRequiredBehavior` (#279).

---

## Problem Frame

`src/Headless.Api.Core/Middlewares/IdempotencyMiddleware.cs` (current implementation, lines 40–73) reads `X-Idempotency-Key`, calls `ICache.TryInsertAsync(...)`, executes the request on insert-success, and returns **409 Conflict** on insert-failure. This is a uniqueness guard, not idempotency.

The standard idempotency contract — codified by Stripe, AWS, PayPal, Square, GitHub, and IETF `draft-ietf-httpapi-idempotency-key-header-07` — is:

- **Same key + same body** → replay the original response (status, safe headers, body bytes).
- **Same key + different body** → 422 Unprocessable Content (IETF SHOULD).
- **Same key, original still in flight** → 409 Conflict (IETF SHOULD).
- **New key** → execute as a fresh request.

The current behavior breaks the exact failure mode idempotency exists to handle: a client whose first response was lost on the wire retries with the same key and gets 409 instead of the original 201. The receipt is unrecoverable.

Prior art research (Stripe / Brandur Leach Postgres reference, AWS Lambda Powertools, velmie/idempo Go, idempo Ruby, IETF draft-07, IdempotentAPI .NET) confirms strong consensus on contract surface — POST + PATCH primary methods, 409 for in-flight, 422 for mismatch, per-entity scope, ~24h TTL — and divergence on three knobs: body-size handling (reject vs pass-through), header replay (allowlist vs copy-all), and in-flight handling (block vs reject). This RFC picks the more principled side of each split.

The Mediator alternative (`IdempotencyBehavior<TRequest, TResponse>`) was considered and rejected. It runs post-model-bind (abusive payloads fully deserialized before dedupe), fires on every internal `Send()` (false positives on handler-to-handler composition), cannot cache HTTP status/headers/byte body (only the typed response), and requires `IHttpContextAccessor` (defeating the layer it's trying to abstract). Same structural defects as the removed `AuthBehavior` and slated-for-removal `TenantRequiredBehavior` (#279).

---

## Actors

- A1. **Middleware consumer (framework user).** References the new `Headless.Api.Idempotency` package, wires `services.AddIdempotency(...)` and `app.UseIdempotency()` in `Program.cs`. Expects safe defaults that match the Stripe/IETF contract without per-endpoint configuration, plus the ability to override per-endpoint when needed (e.g., one endpoint with a 7d TTL while the app default stays 24h).
- A2. **API client (downstream).** Sends `X-Idempotency-Key` on unsafe-method requests. Expects: identical retry returns the original response with `Idempotent-Replayed: true`; modified retry returns 422; in-flight retry returns 409.
- A3. **Operator / SRE.** Reads logs and traces. Needs to distinguish first-execution from replay (via the response header), see structured logs on each branch (hit/miss/mismatch/in-flight/oversize), and tune TTL or body cap per app.
- A4. **#279 tenancy refactor.** This RFC reuses `ICurrentTenant` populated by `UseHeadlessTenancy()`; the canonical pipeline order docs updated by #279 must be extended to place `UseIdempotency()` correctly.
- A5. **Consumer apps (zad-ngo and successors).** Currently planning local `IdempotencyBehavior` placeholders; will delete those and adopt `UseIdempotency()` once this RFC ships.

---

## Key Flows

- F1. **Retry of a lost response.**
  - **Trigger:** A2 sends `POST /v1/disbursements` with `X-Idempotency-Key: abc`. The 201 response is lost on the wire. A2 retries with the same key and identical body.
  - **Actors:** A2, middleware.
  - **Steps:** Middleware computes SHA-256 of buffered body, looks up `(tenant, POST, /v1/disbursements, abc)` in `ICache`, finds a hit with matching hash, writes back the stored status code, copies the allowlisted headers, writes the stored body bytes, adds `Idempotent-Replayed: true`, short-circuits without calling `next`.
  - **Outcome:** A2 receives the original 201 with `Location` header intact. The handler is not re-invoked. The downstream payment system is not re-charged.
  - **Covered by:** R1, R3, R5, R7, R12.

- F2. **Modified retry (mismatched body).**
  - **Trigger:** A2 sends `POST /v1/disbursements` with `X-Idempotency-Key: abc` and amount=500, then resends with `X-Idempotency-Key: abc` and amount=600.
  - **Actors:** A2, middleware.
  - **Steps:** Middleware looks up the entry, finds a hit with a different SHA-256, returns 422 Unprocessable Content with `g:idempotency-key-reused` problem details (key, expected hash prefix, observed hash prefix in the extension fields).
  - **Outcome:** A2 cannot accidentally re-purpose a key. The original 500-disbursement record is not overwritten and not double-charged.
  - **Covered by:** R3, R10.

- F3. **Concurrent in-flight (default 409).**
  - **Trigger:** A2 sends two `POST /v1/disbursements` with the same key and body simultaneously (e.g., client double-submit, mobile reconnect).
  - **Actors:** A2, middleware.
  - **Steps:** Request A acquires the in-flight marker atomically (`TryInsertAsync` on `(tenant, method, path, key)` with `INPROGRESS` sentinel). Request B's `TryInsertAsync` fails. With default in-flight strategy (`Reject`), B returns 409 with `g:idempotency-in-flight`. A completes, stores the response, and the marker becomes the cache entry.
  - **Outcome:** Handler runs exactly once. B does not block A's load-balancer slot. B can retry after a backoff and will see the cached response.
  - **Covered by:** R6, R8, R10.

- F4. **Concurrent in-flight (opt-in wait-and-replay).**
  - **Trigger:** Same as F3, but A1 configured `options.InFlightStrategy = WaitAndReplay`.
  - **Actors:** A2, middleware.
  - **Steps:** B acquires a distributed lock on `(tenant, method, path, key)` via `IDistributedLockProvider` (from `Headless.DistributedLocks.Abstractions`), waits up to `options.InFlightLockTimeout` for A to complete and populate the cache entry, then replays the cached response with `Idempotent-Replayed: true`. If the timeout elapses with no cache entry, B returns 409 with `g:idempotency-in-flight-timeout`.
  - **Outcome:** Single client retry gets the real response without an explicit backoff. LB slot is held for at most the configured timeout.
  - **Covered by:** R8, R9.

- F5. **Oversize body (default 413).**
  - **Trigger:** A2 sends a POST with a 2 MiB body and `X-Idempotency-Key: abc`. Default `options.MaxBodySizeForHashing = 1 MiB`.
  - **Actors:** A2, middleware.
  - **Steps:** Middleware buffers up to the cap, observes overflow before reaching EOF, returns 413 Payload Too Large with `g:idempotency-body-too-large` problem details (limit, observed-prefix-length).
  - **Outcome:** Client is told explicitly that idempotency could not be guaranteed for this payload. No silent contract violation. Client can split the request or drop the key.
  - **Covered by:** R4, R11.

- F6. **Oversize body (opt-in pass-through).**
  - **Trigger:** Same as F5, but A1 configured `options.OversizeBehavior = PassThrough`.
  - **Actors:** A2, middleware.
  - **Steps:** Middleware logs a warning at `Warning` severity with the key and observed-prefix-length, executes `next(context)` without storing the response, adds no `Idempotent-Replayed` header.
  - **Outcome:** Legacy clients that ship oversized bodies with keys continue to work; operator sees the warning and can decide whether to flip back to 413.
  - **Covered by:** R4, R11.

- F7. **Per-endpoint override via metadata.**
  - **Trigger:** A1 has a webhook receiver endpoint that wants a 7-day idempotency window (matches the partner's retry policy) while the rest of the API uses the default 24h.
  - **Actors:** A1, middleware.
  - **Steps:** A1 maps the endpoint with `.WithIdempotency(o => o.IdempotencyKeyExpiration = TimeSpan.FromDays(7))`. At request time, the middleware reads the endpoint's `IdempotencyMetadata` (`HttpContext.GetEndpoint().Metadata.GetMetadata<IdempotencyMetadata>()`), merges the override over the app-level `IdempotencyOptions`, and applies the merged options for that request.
  - **Outcome:** One endpoint runs with the longer window; the rest of the API is unchanged. The override is local to the endpoint definition, visible in the route map output, and discoverable from the endpoint metadata at runtime.
  - **Covered by:** R22, R23.

- F8. **Custom fingerprint for canonical-JSON.**
  - **Trigger:** A consumer's API accepts JSON bodies that contain client-generated timestamps which are not part of the operation's semantic identity. The default raw-body SHA-256 would treat two semantically-equivalent retries as a fingerprint mismatch.
  - **Actors:** A1, middleware.
  - **Steps:** A1 supplies `options.RequestFingerprint = async (ctx) => CanonicalJson.Hash(await ctx.Request.ReadJsonAsync())`. The middleware uses the consumer's fingerprint instead of raw-body SHA-256 for the mismatch comparison.
  - **Outcome:** A retry with the same semantic payload but a refreshed client timestamp matches the original entry and replays. The Stripe canonical-JSON pattern is supportable without the framework owning a JSON canonicalizer.
  - **Covered by:** R24.

- F9. **Custom key derivation for per-user scoping inside a tenant.**
  - **Trigger:** A consumer's tenant model is "tenant = workspace, user = individual," and the consumer wants the idempotency key scoped per-user-within-tenant rather than per-tenant globally.
  - **Actors:** A1, middleware.
  - **Steps:** A1 supplies `options.KeyDeriver = (ctx, key) => $"idem:{tenant}:{userId}:{method}:{path}:{key}"`. The middleware uses the consumer's derived key for all cache reads and writes.
  - **Outcome:** Two users inside the same tenant can reuse a key string without collision; the consumer's domain model overrides the framework default.
  - **Covered by:** R25.

- F10. **Mediator boundary (no behavior).**
  - **Trigger:** A consumer app implements a CQRS handler that internally `Send()`s another command (handler-to-handler composition) inside the same HTTP request.
  - **Actors:** Consumer app.
  - **Steps:** No `IdempotencyBehavior<,>` is registered or registerable. The outer HTTP request's idempotency-key is enforced exactly once at the middleware. Internal `Send()` calls have no idempotency layer (and don't need one — they're synchronous in-process invocations within an already-deduplicated boundary).
  - **Outcome:** No silent false-positive dedup on internal command composition. No `IHttpContextAccessor` coupling on the Mediator pipeline.
  - **Covered by:** R20.

---

## Requirements

**Package**
- R0. The middleware and all supporting types ship in a new `Headless.Api.Idempotency` package (folder `src/Headless.Api.Idempotency/`). The package depends on `Headless.Api.Core` (for `IProblemDetailsCreator`, `HttpHeaderNames`, the resource strings), `Headless.Api.Abstractions`, `Headless.Caching.Abstractions`, `Headless.DistributedLocks.Abstractions`, `Headless.MultiTenancy.Abstractions`, `Headless.Hosting`, and `Headless.FluentValidation`. The existing `IdempotencyMiddleware` and `IdempotencyMiddlewareOptions` types are deleted from `Headless.Api.Core` (greenfield; no compatibility shim). The current `SetupMiddlewares.AddIdempotencyMiddleware` extension is also deleted from `Headless.Api.Core` and reintroduced in the new package as `SetupIdempotency.AddIdempotency` (see R17).

**Replay semantics**
- R1. The middleware reads `X-Idempotency-Key` (header name configurable via `options.HeaderName`). When absent, empty, or whitespace, the request passes through without idempotency processing.
- R2. The middleware applies only to HTTP methods listed in `options.Methods` (default: POST, PUT, PATCH, DELETE — case-insensitive). GET / HEAD / OPTIONS / TRACE always pass through.
- R3. The cache key is `idem:{tenant}:{method}:{path}:{idempotency-key}` where:
  - `{tenant}` is `ICurrentTenant.Id` rendered as a string, or empty for null tenant. The empty-string segment is used as-is between the colons (e.g., `idem::POST:/v1/x:abc`); no `_global` literal is reserved in the tenant ID namespace.
  - `{method}` is the uppercased HTTP method.
  - `{path}` is `HttpContext.Request.Path.Value` (excluding query string and host).
  - `{idempotency-key}` is the raw header value (last value if multi-valued).
- R4. The middleware buffers the request body up to `options.MaxBodySizeForHashing` (default 1 MiB) using `Request.EnableBuffering()`, computes SHA-256 over the raw bytes, and rewinds the stream before invoking `next`.

**Lookup branches**
- R5. **Cache hit, hash match:** Write the stored status code, copy the stored headers per the replay allowlist, write the stored body bytes to the response, add `Idempotent-Replayed: true` header. Do not invoke `next`.
- R6. **In-flight marker present:** Apply the configured `InFlightStrategy`. Default `Reject` → 409 Conflict with `g:idempotency-in-flight` problem details. Opt-in `WaitAndReplay` → acquire a distributed lock on the key, wait up to `options.InFlightLockTimeout`, then replay.
- R7. **Cache hit, hash mismatch:** Return 422 Unprocessable Content with `g:idempotency-key-reused` problem details. The extension fields contain only the conflicting `idempotency_key` value and a generic message ("Same idempotency key reused with a different request body."). No body hash prefixes are disclosed in the response — debugging happens via server logs (the middleware logs both hash prefixes at `Warning` severity for SRE investigation). This minimizes derived-info leakage about previously-seen bodies in a model where the idempotency key itself is not separately authenticated. `options.MismatchStatusCode` allows opting back to 409 for clients that follow the Stripe (pre-IETF) convention.
- R8. **Cache miss:** Atomically set an in-flight marker (`ICache.TryInsertAsync` with sentinel value and short TTL = `options.InFlightLockTimeout` + 5s safety margin). On success, wrap the response stream in a capture buffer, invoke `next`, then on completion replace the marker with the full `IdempotencyRecord` and refresh the TTL to `options.IdempotencyKeyExpiration`.

**Concurrency**
- R9. `options.InFlightStrategy` is an enum: `Reject` (default) or `WaitAndReplay`. When `WaitAndReplay`, the middleware uses `IDistributedLockProvider` (resolved from DI) keyed on `lock:idem:{tenant}:{method}:{path}:{key}` with timeout `options.InFlightLockTimeout` (default 30s). On lock-acquire timeout with no populated cache entry, return 409 with `g:idempotency-in-flight-timeout`.

**Response capture**
- R10. The captured response record contains: HTTP status code (int), captured headers (filtered through the replay allowlist), body bytes (byte array), and a creation timestamp. Stored via `ICache.SetAsync` as a single binary-serializable record. No new abstraction is introduced.
- R11. `options.ShouldCacheResponse` is a `Func<HttpContext, bool>` evaluated after the inner handler completes. The default predicate caches responses with status:
  - **2xx** (success) — always cached.
  - **4xx deterministic-client-error** — cached: 400, 401, 403, 404, 405, 409, 410, 411, 412, 413, 414, 415, 416, 422, 451.
  - **4xx transient/retry-worthy** — never cached: 408 (Request Timeout), 425 (Too Early), 429 (Too Many Requests).
  - **5xx** — never cached.
  - **1xx, 3xx** — never cached.
  Consumers can replace the predicate entirely (e.g., regulated APIs that must replay 5xx for audit) without affecting other defaults.

**Header replay**
- R12. `options.ReplayHeaderAllowlist` is an `ISet<string>` (case-insensitive). Default set: `Content-Type`, `Content-Language`, `Content-Encoding`, `Content-Disposition`, `Location`, `Link`, `ETag`, `Last-Modified`, `Cache-Control`, `Vary`. Custom `X-*` headers must be added explicitly by the consumer. Headers not in the allowlist are dropped from the cached record at capture time (not at replay time), so storage stays small.
- R13. On replay, after the allowlisted headers are written, the middleware always adds `Idempotent-Replayed: true`. The spelling (no `X-` prefix) matches Stripe's `Idempotent-Replayed` documented convention to maximize multi-provider SDK overlap. Consumers cannot disable this header in v1.

**Oversize behavior**
- R14. `options.OversizeBehavior` is an enum: `Reject` (default) or `PassThrough`. On `Reject`, return 413 Payload Too Large with `g:idempotency-body-too-large` problem details. On `PassThrough`, log a `Warning` with the key and observed-prefix-length, then invoke `next` without capturing or replaying. The pass-through path must not write `Idempotent-Replayed: true` because no idempotency guarantee was made.

**TTL**
- R15. `options.IdempotencyKeyExpiration` is `TimeSpan?` (default 24 hours). The current default of 1 hour is increased to align with Stripe/IdempotentAPI consensus and the typical client retry window. Null is rejected at validation (replaces the current optional-TTL pattern).

**Options shape**
- R16. The options class is renamed `IdempotencyOptions` (was `IdempotencyMiddlewareOptions`). Greenfield framework, no compatibility shim. The validator stays in the same file directly below the options class per the project convention. New shape:
  ```csharp
  public sealed class IdempotencyOptions
  {
      public TimeSpan IdempotencyKeyExpiration { get; set; } = TimeSpan.FromHours(24);
      public string HeaderName { get; set; } = HttpHeaderNames.IdempotencyKey;
      public ISet<string> Methods { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "POST", "PUT", "PATCH", "DELETE" };
      public InFlightStrategy InFlightStrategy { get; set; } = InFlightStrategy.Reject;
      public TimeSpan InFlightLockTimeout { get; set; } = TimeSpan.FromSeconds(30);
      public int MaxBodySizeForHashing { get; set; } = 1 * 1024 * 1024;
      public OversizeBehavior OversizeBehavior { get; set; } = OversizeBehavior.Reject;
      public int MismatchStatusCode { get; set; } = StatusCodes.Status422UnprocessableEntity;
      public ISet<string> ReplayHeaderAllowlist { get; set; } = /* see R12 */;
      public Func<HttpContext, bool> ShouldCacheResponse { get; set; } = DefaultCachePredicate.Instance;
      public Func<HttpContext, bool>? ShouldApply { get; set; }
      public Func<HttpContext, string, string>? KeyDeriver { get; set; }
      public Func<HttpContext, ValueTask<byte[]>>? RequestFingerprint { get; set; }
  }
  ```

**Registration**
- R17. The new package ships `SetupIdempotency` at the package root, exposing three `AddIdempotency` overloads via C# 14 extension members per the framework convention (one taking `IConfiguration`, one `Action<IdempotencyOptions>`, one `Action<IdempotencyOptions, IServiceProvider>`), all delegating to a single private `_AddIdempotencyCore` helper. Registration calls `services.Configure<IdempotencyOptions, IdempotencyOptionsValidator>(...)` from `Headless.Hosting`. No parameterless overload — options are required.
- R18. The middleware itself is registered as a scoped `IMiddleware` (current pattern preserved). No DI-level dependency on `IDistributedLockProvider` unless `InFlightStrategy.WaitAndReplay` is configured; resolution is lazy via `IServiceProvider.GetService<IDistributedLockProvider>()` inside the middleware. When `WaitAndReplay` is configured at startup and no provider is registered, fail fast in `IValidateOptions` with a clear error message.

**Pipeline placement**
- R19. Canonical order documented in `docs/llms/api.md`:
  ```
  UseExceptionHandler
    → UseRouting
    → UseAuthentication
    → UseHeadlessTenancy        (sets ICurrentTenant)
    → UseAuthorization
    → UseIdempotency             (this RFC; depends on tenant being set)
    → endpoints
  ```
  Auth and tenant must precede idempotency so the cache key can be tenant-scoped and unauthenticated requests cannot allocate storage. Authorization runs between tenant and idempotency so unauthorized requests also do not allocate storage. Document this constraint in the middleware XML doc as well.

**Mediator boundary**
- R20. No `IdempotencyBehavior<TRequest, TResponse>` is added to `Headless.Mediator` or `Headless.Mediator.Behaviors`. Document the explicit non-addition in `src/Headless.Mediator/README.md` and `docs/llms/mediator.md` alongside the existing `AllowMissingTenant` / auth boundary guidance from #279. The reasoning section names the four structural defects: post-model-bind, fires on internal `Send()`, cannot capture HTTP response, requires `IHttpContextAccessor`.

**Per-endpoint overrides**
- R22. The package ships an `IdempotencyMetadata` type and a `WithIdempotency(this IEndpointConventionBuilder, Action<IdempotencyOptions>)` extension. Mapping an endpoint with `.WithIdempotency(o => o.IdempotencyKeyExpiration = TimeSpan.FromDays(7))` attaches an override to the endpoint's metadata collection. The override `Action` is captured and applied lazily at request time, not at startup.
- R23. At request time, the middleware reads `HttpContext.GetEndpoint()?.Metadata.GetMetadata<IdempotencyMetadata>()`. When metadata is present, it constructs a per-request `IdempotencyOptions` by cloning the app-level options and applying the metadata's `Action` over the clone. Cloning is shallow (mutable scalar properties are copied; the `ReplayHeaderAllowlist` and `Methods` sets are copied to fresh instances; delegates are shared by reference). Endpoint-level overrides are only meaningful for properties that are evaluated per-request (`IdempotencyKeyExpiration`, `Methods`, `InFlightStrategy`, `InFlightLockTimeout`, `MaxBodySizeForHashing`, `OversizeBehavior`, `MismatchStatusCode`, `ReplayHeaderAllowlist`, `ShouldCacheResponse`, `ShouldApply`, `KeyDeriver`, `RequestFingerprint`); structural properties (`HeaderName`) ignore the endpoint override and use the app-level value.

**Custom fingerprint**
- R24. `options.RequestFingerprint` is `Func<HttpContext, ValueTask<byte[]>>?` (nullable; default null = raw-body SHA-256). When supplied, the middleware does not buffer or hash the body itself — it invokes the consumer's delegate, which is responsible for reading the body in whatever form the consumer wants (e.g., canonical-JSON, ordered form fields, normalized whitespace) and returning the fingerprint bytes. The middleware uses the returned bytes verbatim for hash-match comparisons. The middleware still enforces `MaxBodySizeForHashing` against the raw request body before invoking the delegate, and still rewinds the request stream so the consumer's delegate can re-read it for the inner handler. The `OversizeBehavior` semantics (R14) apply unchanged. When `RequestFingerprint` is supplied, the consumer owns canonicalization correctness; the framework does not validate the returned bytes' format.

**Custom key derivation**
- R25. `options.KeyDeriver` is `Func<HttpContext, string, string>?` (nullable; default null = built-in `(tenant, method, path, idempotency-key)` composition per R3). The first parameter is the `HttpContext`; the second is the raw idempotency-key value from the header. When supplied, the middleware uses the consumer's delegate output as the cache key verbatim — the consumer owns tenant/scope inclusion. Consumers using `KeyDeriver` typically need access to additional context (current user ID, API key, custom claim); they can resolve scoped services from `HttpContext.RequestServices` inside the delegate.

**Backward compatibility**
- R21. No compatibility shim for the previous guard-style behavior. The class name `IdempotencyMiddleware` is preserved (in the new package); the previous registration extension `AddIdempotencyMiddleware` is renamed to `AddIdempotency` per the new package's naming. The **semantics** of duplicate-key handling change. Existing consumers relying on 409-on-any-duplicate must either accept the new contract (correct behavior) or set `options.ShouldApply = _ => false` to disable. Release notes call this out as an explicit breaking semantic change AND as a project-reference change (`Headless.Api.Core` → `Headless.Api.Idempotency`).

---

## Acceptance Examples

- AE1. **Covers R4, R5, R12, R13.** Given a POST request with `X-Idempotency-Key: k1` and body `{"amount":500}` that returns 201 Created with `Location: /v1/disbursements/d_1` and `Content-Type: application/json`, when an identical retry arrives, the response is 201 with the same `Location` and `Content-Type`, body bytes identical, and `Idempotent-Replayed: true` present.
- AE2. **Covers R7.** Given the same key `k1` from AE1 but body `{"amount":600}`, the retry returns 422 with problem details `g:idempotency-key-reused`. The original cached entry for `k1` is unchanged.
- AE3. **Covers R6, R8.** Given two concurrent POSTs with key `k2` and identical bodies arriving while default `InFlightStrategy.Reject` is configured, exactly one handler invocation occurs; the loser returns 409 with problem details `g:idempotency-in-flight`. After the winner completes, a third request with `k2` and the same body returns the replayed response.
- AE4. **Covers R9.** Given `InFlightStrategy.WaitAndReplay` configured and two concurrent POSTs with key `k3`, the loser blocks on the distributed lock, the winner completes and stores the response, the loser replays. If the loser's wait exceeds `InFlightLockTimeout`, it returns 409 with problem details `g:idempotency-in-flight-timeout`.
- AE5. **Covers R11.** Given a POST that returns 503 (downstream transient), nothing is stored in the cache; a retry with the same key and body executes the handler fresh. Given a POST that returns 422 (validation), the response is cached; a retry returns the cached 422 with `Idempotent-Replayed: true`.
- AE6. **Covers R14.** Given default `OversizeBehavior.Reject` and a 2 MiB body with an idempotency key, the response is 413 with `g:idempotency-body-too-large`. With `OversizeBehavior.PassThrough`, the handler executes once, no `Idempotent-Replayed` header is added on subsequent retries, and a warning is logged.
- AE7. **Covers R3.** Given two tenants `T1` and `T2` both sending POST `/v1/x` with idempotency key `k` and identical bodies, two distinct cache entries exist; replay only happens within the same tenant.
- AE8. **Covers R3.** Given a request with no resolved tenant (e.g., a pre-auth health-check endpoint inside `options.Methods`) using key `k`, the cache key is `idem::POST:/v1/x:k`; a later request from tenant `T1` with the same key targets `idem:T1:POST:/v1/x:k` and does not collide.
- AE9. **Covers R12, R13.** Given a response with `Set-Cookie: session=abc; HttpOnly` and `traceparent: 00-...`, the cached entry contains neither header; replayed responses do not leak the original session cookie or trace context.
- AE10. **Covers R20.** Given a consumer attempts to register an `IdempotencyBehavior<,>` for `Headless.Mediator`, no such type exists in the framework; the consumer must implement it locally and accept the documented defects, which are listed in the mediator README.
- AE11. **Covers R0.** Given a consumer's API project references only `Headless.Api.Core` (and not `Headless.Api.Idempotency`), `AddIdempotency` is not in scope and the `IdempotencyMiddleware` type does not resolve. The previous `AddIdempotencyMiddleware` extension no longer exists in `Headless.Api.Core`. Consumers must add the new package reference to opt in.
- AE12. **Covers R22, R23.** Given an endpoint mapped with `.WithIdempotency(o => o.IdempotencyKeyExpiration = TimeSpan.FromDays(7))` and an app-level default of 24h, a request to that endpoint stores the cache entry with a 7-day TTL; a request to a sibling endpoint without the metadata stores with the 24h default.
- AE13. **Covers R24.** Given `options.RequestFingerprint` configured to compute a canonical-JSON hash and two requests with identical semantic JSON but different key ordering and client-timestamp fields, the second request matches the first and replays the cached response.
- AE14. **Covers R25.** Given `options.KeyDeriver` configured to include a user ID, two users inside the same tenant sending POST with the same idempotency-key value receive two independent cache entries; their responses do not cross.

---

## Success Criteria

- A retry of a request whose first response was lost on the wire returns the original response — the failure mode idempotency exists to handle — verified end-to-end in an integration test.
- Consumers replace local Mediator-level idempotency placeholders (e.g., zad-ngo's FIN-07 `IdempotencyBehavior`) with a single `AddIdempotency(...)` + `UseIdempotency()` call in `Program.cs` after adding a `Headless.Api.Idempotency` package reference, with no per-endpoint configuration required for the default contract.
- A consumer who needs a non-default TTL or fingerprint for one endpoint can express that locally on the endpoint definition (`.WithIdempotency(...)`) rather than fragmenting their app-level options.
- Replay is observable from logs and from the response itself (`Idempotent-Replayed: true`). An operator can answer "is this a replay?" from the response alone without inspecting server state.
- No client retry hits the previously incorrect 409-on-duplicate failure mode. The integration test suite covers all five branches (miss, hit-match, hit-mismatch, in-flight, oversize) plus tenant isolation.
- The `IdempotencyMiddleware` after rewrite is no longer than ~250 lines including capture stream wrapping and problem-details emission, and reuses existing `ICache`, `IClock`, `ICurrentTenant`, and (conditionally) `IDistributedLockProvider` without introducing new core abstractions.
- `docs/llms/api.md` documents the canonical middleware order including `UseIdempotency()`. `docs/llms/mediator.md` documents the explicit non-addition of `IdempotencyBehavior<,>` with the four-defect reasoning.

---

## Scope Boundaries

**Deferred for later**

- `IIdempotencyStore` abstraction over `ICache`. Defer until a second backend is needed. The current `ICache` interface (`ValueTask<bool> TryInsertAsync<T>`, `ValueTask<CacheValue<T>> GetAsync<T>`, `SetAsync<T>`) handles the storage shape; a dedicated interface adds an indirection layer without a concrete second consumer. The package extraction (R0) does NOT change this calculus — the new package's public surface stays minimal until justified.
- `Headless.Api.Idempotency.EntityFramework` package with `EfIdempotencyStore`. Defer until a consumer explicitly needs durability past cache eviction (e.g., a regulated financial API with audit retention requirements). Building it now ships a versioned NuGet package, EF schema migrations, and multi-provider concerns for zero present users. The package naming reserves the slot for the future sibling.
- Custom problem-details factories for the four idempotency-specific responses. Out of scope for v1 per the answered design question; consumers with bespoke problem-details envelopes implement their own `IProblemDetailsCreator` (the existing extension point) and the middleware reuses it transparently.
- Streaming body hash (no buffering). Defer until a consumer reports the 1 MiB cap is a real constraint. Buffer-and-hash is simpler and matches velmie/idempo Go; streaming TeeStream introduces complexity (back-pressure, partial-write semantics) that v1 does not need.
- Roslyn analyzer warning consumers who register an idempotency-like Mediator behavior. Convention only in v1; the rejection rationale lives in docs.
- `Idempotent-Replayed` header configurability. Always-on in v1 for observability; revisit if a consumer reports a real contract conflict.
- Replay of trailers (HTTP/2 / gRPC). v1 captures status + allowlisted headers + body bytes only.
- Body compression awareness. The captured bytes are whatever the response stream wrote; if a downstream middleware (e.g., `UseResponseCompression`) re-compresses on replay, that is the desired behavior. If a consumer wires their own pre-write compression that needs preserving, they configure the allowlist to include `Content-Encoding`. v1 includes `Content-Encoding` in the default allowlist (R12).

**Outside this RFC**

- Refactoring the `IdempotencyKeyExpiration` from `TimeSpan?` to a non-null `TimeSpan` is part of this RFC (R15); broader options-shape audits across the framework are not.
- The MultiTenancy refactor's tenant-resolution middleware order changes are owned by #279; this RFC inherits whatever that branch lands.

---

## Key Decisions

- **Extract to a dedicated `Headless.Api.Idempotency` package.** Idempotency is a discrete, opt-in API concern that not every consumer needs. Keeping it in `Headless.Api.Core` grows that package toward a kitchen sink and forces every consumer to pull the body-buffering code plus a `Headless.DistributedLocks.Abstractions` reference (even conditionally). Extraction matches the existing framework factoring (`Headless.Api.Versioning`, `Headless.Api.FluentValidation`), keeps the ProjectReference graph honest, and lets the future sibling package (`Headless.Api.Idempotency.EntityFramework`) version cleanly. **Reason:** Consistent with framework discipline; cost is one new project + NuGet metadata, benefit is dependency hygiene and clearer evolution path.
- **Minimal core swap over full bundle inside the new package.** Even given the extraction, the five sub-decisions in #280 (replay rewrite, new abstraction, EF package, body cap, header policy) each have independent carrying cost. Ship the correctness fix (guard → replay) plus the in-scope hygiene (size cap, header allowlist, status predicate, in-flight strategy) and defer the abstraction and EF package until a real second consumer exists. **Reason:** Package extraction and storage abstraction are independent decisions; YAGNI on the abstraction layer holds because `ICache` already covers the storage contract, and extracting `IIdempotencyStore` later from a dedicated package is a non-breaking addition.
- **413 default on oversize, opt-in pass-through.** Silent pass-through means a 2 MiB-body client thinks idempotency is active when it isn't — the exact contract violation the rewrite is meant to fix. velmie/idempo Go takes the same position; #280's "pass-through + warning" is the legacy lenient default. **Reason:** Correctness over compatibility on the primary contract.
- **Allowlist for replay headers, default-safe set.** Copy-all replays `Set-Cookie` (auth state leak), `traceparent` (stale trace context, breaks W3C correlation), `Date` (stale clock), and double-CORS under middleware reorderings. Stripe themselves don't byte-replay headers — they reconstruct from a structured record. velmie defaults to empty allowlist; this RFC picks a slightly fuller default (`Content-Type`, `Location`, `ETag`, `Last-Modified`, `Cache-Control`, `Vary`, `Content-Language`, `Content-Encoding`) plus consumer-extensibility. **Reason:** Security and correctness over byte-fidelity; the structured-replay shape is what every reviewed implementation actually does.
- **In-flight default Reject (409), opt-in WaitAndReplay.** Blocking the LB thread for up to 30s is a real cost (worker exhaustion under burst, breaches request-timeout SLOs). velmie defaults to Reject; AWS Powertools defaults to Reject; #280's WaitAndReplay default would be the outlier. Consumers who explicitly want the better single-retry UX can flip the strategy. **Reason:** Don't pay the cost by default; honest 409 is well-understood by HTTP clients.
- **`Idempotent-Replayed: true` always on.** Free observability. Distinguishes first-execution from replay in client SDKs, integration tests, server access logs, and tracing systems. Stripe spelling chosen over `X-Idempotent-Replay` for SDK overlap with clients that already speak Stripe's API. **Reason:** Cost is one extra header; benefit is operator and client clarity for the life of the product.
- **Cache key `(tenant, method, path, key)`, not `(tenant, key)`.** Reusing an idempotency key across endpoints is a client bug, but treating it as fingerprint-mismatch (422) is more surprising than treating endpoints as natural namespaces. `(tenant, method, path, key)` extends Brandur's `(user_id, key)` pattern naturally and matches velmie's behavior. **Reason:** Two endpoints sharing a key string is a benign client error; the cache key composition makes that case correct without explicit cross-endpoint detection logic.
- **22 status for mismatch, 409 opt-in.** IETF draft-07 SHOULD is 422; Stripe's older convention is 409. Defaulting to the IETF position is forward-looking; the 409 escape hatch via `MismatchStatusCode` keeps the door open for clients written against the Stripe contract. **Reason:** Align with standards-track; preserve compatibility for the prior convention as an option.
- **24h default TTL.** Stripe consensus, matches IdempotentAPI; longer than idempo Ruby's 30s (which targets only retry windows) and AWS Powertools's 1h (which targets short Lambda invocations), shorter than PayPal's 45d (which targets payout-confirmation windows). **Reason:** 24h covers the documented retry window for most HTTP clients without paying a long storage tail.
- **No Mediator behavior, ever.** Same four structural defects as the removed `AuthBehavior` and slated-for-removal `TenantRequiredBehavior`. Document the rejection alongside #279's reasoning. **Reason:** Boundary concerns belong at the boundary; Mediator is dispatch.
- **Three custom-hook seams (`KeyDeriver`, `RequestFingerprint`, per-endpoint metadata), no problem-details factory.** The three hooks address concrete consumer use cases (per-user scoping, canonical-JSON fingerprinting, per-endpoint TTL/strategy override) that cannot be expressed through scalar options without bloating the option surface. The problem-details factory is rejected because consumers with bespoke envelopes implement `IProblemDetailsCreator` once and the middleware reuses it through the existing extension point — there's no idempotency-specific shape to override. **Reason:** Configurability where it unlocks legitimate use cases; not where the existing framework seam already handles it.

---

## Dependencies / Assumptions

- The new `Headless.Api.Idempotency.csproj` (Headless.NET.Sdk.Web) depends on: `Headless.Api.Core`, `Headless.Api.Abstractions`, `Headless.Caching.Abstractions`, `Headless.DistributedLocks.Abstractions`, `Headless.MultiTenancy.Abstractions`, `Headless.Hosting`, `Headless.FluentValidation`. All already exist in the repo; no new third-party NuGet dependencies are introduced.
- `Headless.DistributedLocks.Abstractions` is an unconditional ProjectReference of the new package, not a conditional dependency. Concrete locks (`Headless.DistributedLocks.Redis`, `.Cache`, `.Core`) remain consumer-selected; the new package binds only against the abstraction.
- Depends on `IProblemDetailsCreator` (`Headless.Api.Abstractions`) for the four new problem-details types: `g:idempotency-key-reused`, `g:idempotency-in-flight`, `g:idempotency-in-flight-timeout`, `g:idempotency-body-too-large`. Verified that `IProblemDetailsCreator` supports custom extension fields.
- Depends on #279's tenancy refactor being merged before this RFC's docs land — the canonical middleware order in `docs/llms/api.md` already cites `UseHeadlessTenancy()`, but #279 is the authoritative source for the upstream slot.
- Assumes `ICache` implementations (Memory, Redis, Hybrid) atomically support `TryInsertAsync` with a sentinel value — verified by reading `src/Headless.Caching.Abstractions/ICache.cs:44` (the contract). The atomic insert is what gates in-flight detection in the default `Reject` strategy.
- Assumes `Headless.DistributedLocks.Redis` is the practical lock backend when `WaitAndReplay` is enabled. Memory-cache lock (`Headless.DistributedLocks.Cache`) does not provide cross-process protection and should be documented as not-recommended for multi-instance APIs.
- Assumes greenfield posture (no deployed consumers of the current guard-style 409 behavior beyond active development branches). Per project memory; the only known consumer (zad-ngo) is explicitly migrating.

---

## Outstanding Questions

### Resolve Before Planning

- ~~[Affects R12][User decision] Confirm the default header allowlist concrete contents.~~ **Resolved 2026-05-19:** Default allowlist includes `Content-Type`, `Content-Language`, `Content-Encoding`, `Content-Disposition`, `Location`, `Link`, `ETag`, `Last-Modified`, `Cache-Control`, `Vary`.
- ~~[Affects R16][User decision] Rename `IdempotencyMiddlewareOptions` → `IdempotencyOptions`.~~ **Resolved 2026-05-19:** Renamed to `IdempotencyOptions` to match framework convention (`CachingOptions`, `MessagingOptions`, `MultiTenancyOptions`).
- ~~[Affects R7][User decision] Mismatch problem-details extension fields.~~ **Resolved 2026-05-19:** Response carries only the `idempotency_key` field and a generic message. No body-hash prefixes disclosed (minimizes derived-info leakage). Hash prefixes are logged server-side at `Warning` severity for SRE investigation.

### Deferred to Planning

- [Affects R8, R10][Technical] Capture-stream implementation: write a custom `Stream` decorator that tees into a `MemoryStream` while forwarding to the original `HttpResponse.Body`, vs. swapping `Body` for a `MemoryStream` and copying back on completion. Both work; pick during planning based on chunked-transfer behavior and large-response memory profile.
- [Affects R10][Technical] Serialization format for the captured `IdempotencyRecord`. `ICache<T>` already handles its own serialization (MessagePack on the Redis path); confirm that a record containing `byte[] Body`, `Dictionary<string, string[]> Headers`, `int StatusCode`, `DateTimeOffset CreatedAt` serializes cleanly across all `ICache` providers without bespoke converters.
- [Affects R11][Needs research] Confirm the precise 4xx-cacheable list against Stripe's published rules. Specifically: 405 (Method Not Allowed) and 411 (Length Required) — cacheable or not? Treating them as cacheable means a retry that supplies the missing length header still gets the original 411; treating them as transient means we always re-execute and risk reaching the handler.
- [Affects R8][Technical] In-flight marker sentinel value collision with regular records. Options: use a discriminator field on the cached payload (`Kind: InFlight | Complete`), or use two distinct cache key prefixes (`idem-pending:` / `idem-complete:`). The former keeps one key per request; the latter avoids a discriminator field at the cost of two `Get` calls in the WaitAndReplay path.
- [Affects R9, R18][Technical] When `WaitAndReplay` is configured but `IDistributedLockProvider` is not registered, fail at `IValidateOptions` time (startup) or at first-request time. Startup is more discoverable; first-request matches the current lazy-resolution pattern elsewhere in the framework.
- [Affects R0, R18][Technical] Folder layout inside the new package: flat `src/Headless.Api.Idempotency/*.cs` (small package, ~10 files) vs nested `src/Headless.Api.Idempotency/Middlewares/Idempotency/*.cs`. Lean toward flat given the package is single-purpose. Confirm during planning.
- [Affects R0][Technical] Whether to retain `Headless.Api.Core/Middlewares/Idempotency/` as a redirect note (a single `README.md` pointing to the new package) or delete it cleanly. Greenfield posture suggests clean delete; confirm.
- [Affects R23][Technical] Endpoint-metadata merge semantics edge cases: when the metadata override sets `KeyDeriver` or `RequestFingerprint`, those delegates may have closed over services from a different scope than the request scope. Document the constraint ("delegates configured via metadata should be stateless or resolve services from `HttpContext.RequestServices`") and verify the warning fires when consumers configure `WithIdempotency(o => o.KeyDeriver = ctx => myStatefulService.Compute(...))`.
- [Affects R24][Technical] Body-stream rewind interaction with `RequestFingerprint`. The middleware must call `Request.EnableBuffering()` before invoking the consumer's delegate so the delegate can read the body and the inner handler can still read it after. Confirm the delegate signature accepts a `HttpContext` that has buffering already enabled, and that the rewind happens unconditionally on return regardless of whether the delegate rewound or not.
