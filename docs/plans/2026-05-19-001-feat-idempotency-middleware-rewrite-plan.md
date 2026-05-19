---
created: 2026-05-19
status: active
type: feat
issue: https://github.com/xshaheen/headless-framework/issues/280
origin: docs/brainstorms/2026-05-19-idempotency-middleware-rewrite-requirements.md
---

# feat: Rewrite IdempotencyMiddleware as Stripe-style replay in new Headless.Api.Idempotency package

## Summary

Extract `IdempotencyMiddleware` out of `Headless.Api.Core` into a new dedicated `Headless.Api.Idempotency` package and rewrite it from a uniqueness guard (409-on-any-duplicate) to Stripe-style replay semantics: cache the full HTTP response on first execution, replay it byte-equivalent on identical retries, return 422 on body-mismatch, 409 on in-flight (default `Reject`, opt-in `WaitAndReplay` via `IDistributedLockProvider`), 413 on oversize body. Backed directly by `ICache` — no new `IIdempotencyStore` abstraction, no EF storage package in v1. Cache key is `idem:{tenant}:{method}:{path}:{idempotency-key}`. Replay-side header copy is governed by an allowlist (no `Set-Cookie`/`traceparent` leakage). Per-endpoint overrides via `IdempotencyMetadata` + `.WithIdempotency(...)`; consumer customization via `KeyDeriver`, `RequestFingerprint`, `ShouldApply`, `ShouldCacheResponse` delegates. The Mediator `IdempotencyBehavior<,>` pattern is explicitly rejected for the same four structural defects as the removed `AuthBehavior` / slated-for-removal `TenantRequiredBehavior` (#279). Greenfield posture: no compatibility shim; the old types are deleted from `Headless.Api.Core`.

---

## Problem Frame

The current `src/Headless.Api.Core/Middlewares/IdempotencyMiddleware.cs` is 74 lines that read `X-Idempotency-Key`, call `ICache.TryInsertAsync(...)`, execute on insert-success, and return **409 Conflict** on insert-failure. That is a uniqueness guard, not idempotency. The exact failure mode the contract exists to handle — a client whose first response was lost on the wire retries with the same key — gets 409 instead of the original 201. The receipt is unrecoverable.

The standard contract (Stripe, AWS, PayPal, Square, IETF `draft-ietf-httpapi-idempotency-key-header-07`) is:
- **Same key + same body** → replay original response (status, allowlisted headers, body bytes).
- **Same key + different body** → 422 Unprocessable Content.
- **Same key, original in flight** → 409 Conflict.
- **New key** → execute fresh.

Implementation belongs at the boundary, not in the Mediator pipeline (R20): the Mediator alternative runs post-model-bind, fires on every internal `Send()`, cannot capture HTTP status/headers/byte body (only typed response), and requires `IHttpContextAccessor`. Same structural defects as removed/slated-for-removal pipeline behaviors.

The work also factors idempotency out of the growing `Headless.Api.Core` kitchen sink into its own opt-in package (matching the `Headless.Api.FluentValidation` / `Headless.Api.DataProtection` precedent), keeping the ProjectReference graph honest and leaving room for a future sibling `Headless.Api.Idempotency.EntityFramework` package without retrofit.

---

## Source Document

This plan is derived from `docs/brainstorms/2026-05-19-idempotency-middleware-rewrite-requirements.md` (R0–R25, F1–F10, AE1–AE14, A1–A5). Every requirement, flow, and acceptance example in the source is referenced below — either in an Implementation Unit, in a test scenario, in Documentation Plan, or explicitly deferred under Scope Boundaries. The source's "Resolve Before Planning" questions were all resolved on 2026-05-19; the "Deferred to Planning" questions (8 items) are settled in Key Technical Decisions below.

Three brainstorm statements that don't match the repo and are corrected in this plan:
- The brainstorm cites `Headless.Api.Versioning` as a sibling — it doesn't exist. Real sibling templates: `Headless.Api.FluentValidation`, `Headless.Api.DataProtection`.
- The brainstorm depends on `Headless.MultiTenancy.Abstractions` — it doesn't exist. `ICurrentTenant` lives in `Headless.Core` (namespace `Headless.Abstractions`), transitive via `Headless.Api.Core`.
- The brainstorm specifies SDK `Headless.NET.Sdk.Web` — every existing `Headless.Api.*` library uses `Headless.NET.Sdk` + `<FrameworkReference Include="Microsoft.AspNetCore.App" />`. This plan follows the actual convention.
- The brainstorm assumes `docs/llms/mediator.md` exists — it doesn't. This plan creates it.
- The brainstorm calls `ICache.SetAsync` — the contract method is `UpsertAsync`. Plan uses `UpsertAsync`.

---

## Requirements Trace

| Source ID | Plan coverage |
| --- | --- |
| R0 (new package) | U1, U11, U13 |
| R1 (header name + pass-through) | U6 |
| R2 (HTTP methods) | U6 |
| R3 (cache key composition) | U6 |
| R4 (body buffering + SHA-256) | U6, U8 |
| R5 (hit + hash match → replay) | U6 |
| R6 (in-flight) | U7 |
| R7 (mismatch → 422) | U7 |
| R8 (cache miss → sentinel + capture + finalize) | U6, U7 |
| R9 (`InFlightStrategy.WaitAndReplay` via `IDistributedLockProvider`) | U7 |
| R10 (captured record shape) | U5 |
| R11 (`ShouldCacheResponse` default predicate) | U8 |
| R12 (replay header allowlist) | U8 |
| R13 (`Idempotent-Replayed: true`) | U4, U6 |
| R14 (oversize behavior) | U8 |
| R15 (24h TTL default + non-null) | U2 |
| R16 (`IdempotencyOptions` shape) | U2 |
| R17 (`SetupIdempotency` three overloads) | U10 |
| R18 (scoped `IMiddleware`, lazy lock resolution, startup validation) | U10 |
| R19 (canonical pipeline order doc) | U13 |
| R20 (no `IdempotencyBehavior<,>`) | U13 |
| R21 (no compat shim) | U11 |
| R22 (`IdempotencyMetadata` + `WithIdempotency`) | U2, U9 |
| R23 (request-time metadata merge) | U9 |
| R24 (`RequestFingerprint`) | U9 |
| R25 (`KeyDeriver`) | U9 |
| F1 (retry of lost response) | U6 — happy path replay |
| F2 (modified retry) | U7 — mismatch |
| F3 (concurrent in-flight Reject) | U7 — Reject |
| F4 (concurrent in-flight WaitAndReplay) | U7 — WaitAndReplay |
| F5 (oversize Reject 413) | U8 — Reject |
| F6 (oversize PassThrough) | U8 — PassThrough |
| F7 (per-endpoint override) | U9 |
| F8 (canonical-JSON fingerprint) | U9 |
| F9 (per-user key derivation) | U9 |
| F10 (no Mediator behavior) | U13 — doc-only |
| AE1–AE14 | Distributed across U6–U9 unit tests + U12 integration tests; AE10 is doc-only in U13 |

---

## High-Level Technical Design

*Directional guidance for review, not implementation specification. The implementing agent should treat this as context, not code to reproduce.*

### Request-flow decision tree

```mermaid
flowchart TD
    A[Incoming HTTP request] --> B{Has X-Idempotency-Key<br/>and method in Methods<br/>and ShouldApply(ctx) != false?}
    B -- no --> Z[next ctx, no idempotency]
    B -- yes --> C[Read endpoint metadata,<br/>shallow-clone options,<br/>apply metadata overrides]
    C --> D{Body length within<br/>MaxBodySizeForHashing?}
    D -- no, Reject --> E[413 + g:idempotency-body-too-large]
    D -- no, PassThrough --> F[Warn log + next ctx,<br/>no replay header]
    D -- yes --> G[Compute fingerprint:<br/>RequestFingerprint hook<br/>OR default SHA-256 of buffered body]
    G --> H[Derive cache key:<br/>KeyDeriver hook OR default<br/>idem:{tenant}:{method}:{path}:{key}]
    H --> I{ICache.GetAsync<IdempotencyRecord>}
    I -- HasValue, Kind=Complete, hash match --> J[Replay: status + allowlisted headers<br/>+ body bytes + Idempotent-Replayed: true]
    I -- HasValue, Kind=Complete, hash mismatch --> K[422 + g:idempotency-key-reused<br/>log hash prefixes at Warning]
    I -- HasValue, Kind=InFlight --> L{InFlightStrategy?}
    L -- Reject --> M[409 + g:idempotency-in-flight]
    L -- WaitAndReplay --> N[Acquire distributed lock,<br/>re-check cache,<br/>replay on Complete<br/>OR 409 g:idempotency-in-flight-timeout]
    I -- NoValue --> O[TryInsertAsync sentinel Kind=InFlight,<br/>TTL = InFlightLockTimeout + safety]
    O -- inserted --> P[Wrap response in CaptureStream,<br/>invoke next ctx]
    O -- race lost --> L
    P --> Q{ShouldCacheResponse ctx?<br/>and not PassThrough path}
    Q -- yes --> R[Build IdempotencyRecord<br/>status + filtered headers + body bytes,<br/>UpsertAsync TTL = IdempotencyKeyExpiration]
    Q -- no --> S[RemoveAsync sentinel<br/>so next retry re-executes]
```

### Cache state machine for a single key

```text
                  TryInsertAsync(InFlight)
              ┌─────────────────────────────┐
              ▼                             │
        ┌──────────┐   next() throws or    │
NoValue │ InFlight │  ShouldCacheResponse  │ TTL elapses
        └──────────┘   returns false       │ before finalize
              │   ┌──────────────────────► NoValue (retry re-executes)
              │   │
              │ next() completes
              ▼
        ┌──────────┐
        │ Complete │ ◄── matches all subsequent identical retries
        └──────────┘     until IdempotencyKeyExpiration TTL
```

Marker→record finalize uses `UpsertAsync<IdempotencyRecord>(key, record, TTL=IdempotencyKeyExpiration)`. The marker's discriminator field is checked again before write (defense in depth against TTL eviction races — see Key Technical Decisions).

### WaitAndReplay sequence

```mermaid
sequenceDiagram
    participant A as Request A (winner)
    participant B as Request B (loser)
    participant Cache as ICache
    participant Lock as IDistributedLockProvider

    A->>Cache: TryInsertAsync(key, InFlight) → true
    B->>Cache: TryInsertAsync(key, InFlight) → false
    B->>Cache: GetAsync(key) → InFlight
    B->>Lock: TryAcquireAsync(lock:idem:..., acquireTimeout=InFlightLockTimeout)
    A->>A: next() executes handler
    A->>Cache: UpsertAsync(key, Complete{status,headers,body})
    A->>Lock: (no lock held by A; only B contends)
    Lock-->>B: IDistributedLock acquired
    B->>Cache: GetAsync(key) → Complete (re-check after lock)
    B->>B: Replay
    Note over B: If GetAsync returns NoValue (TTL'd out)<br/>OR still InFlight after lock acquire,<br/>return 409 g:idempotency-in-flight-timeout
```

The winner (A) does NOT acquire the lock; the lock exists solely so concurrent losers can serialize their wait. If A crashes before finalizing, B's lock acquires after A's marker TTL expires; B then sees `NoValue` and returns 409 with `g:idempotency-in-flight-timeout` (does not replay a non-existent record).

### Options merge for per-endpoint metadata

| Property kind | Merge behavior |
| --- | --- |
| Mutable scalars (`IdempotencyKeyExpiration`, `MaxBodySizeForHashing`, `MismatchStatusCode`, etc.) | Endpoint override wins |
| Sets (`Methods`, `ReplayHeaderAllowlist`) | Endpoint override creates a fresh set; app-level not mutated |
| Delegates (`KeyDeriver`, `RequestFingerprint`, `ShouldCacheResponse`, `ShouldApply`) | Endpoint override replaces by reference (consumer responsible for service-scope correctness) |
| Structural (`HeaderName`) | App-level wins; metadata override ignored (the header is read before metadata lookup) |

---

## Output Structure

```text
src/Headless.Api.Idempotency/
├── Headless.Api.Idempotency.csproj
├── README.md
├── Setup.cs                                  # SetupIdempotency + three AddIdempotency overloads
├── ApplicationBuilderExtensions.cs           # UseIdempotency()
├── EndpointConventionBuilderExtensions.cs    # WithIdempotency(...)
├── IdempotencyMiddleware.cs                  # IMiddleware (scoped)
├── IdempotencyOptions.cs                     # options + validator below
├── IdempotencyMetadata.cs
├── InFlightStrategy.cs
├── OversizeBehavior.cs
├── IdempotencyRecord.cs                      # internal
├── CaptureStream.cs                          # internal
├── DefaultCachePredicate.cs                  # internal
└── Resources/
    ├── IdempotencyMessageDescriber.cs
    ├── Messages.resx
    └── Messages.ar.resx

tests/Headless.Api.Idempotency.Tests.Unit/
└── ... (per-component unit suites)

tests/Headless.Api.Idempotency.Tests.Integration/
└── ... (WebApplicationFactory end-to-end suites)
```

The tree above is a scope declaration. The implementer may adjust if implementation reveals a better layout. Per-unit `**Files:**` sections are authoritative.

---

## Key Technical Decisions

The eight planning questions from the brainstorm's "Deferred to Planning" section are resolved here, plus five additional decisions surfaced by repo research.

1. **Capture stream uses a tee-style `Stream` decorator** (resolves brainstorm Q307). Implement `CaptureStream : Stream` that wraps the original `HttpResponse.Body`, forwards every write to both the original stream and an internal growable buffer (with a cap matching `MaxBodySizeForHashing`-scale, configurable later). Alternative — swap `Body` for a `MemoryStream` and copy back on completion — works for small responses but doubles memory pressure for chunked-transfer responses and breaks Kestrel's flush-on-write streaming semantics. The tee decorator preserves streaming, captures byte-equivalent output, and is the pattern velmie/idempo's Go implementation uses. *~85% confident; mark CaptureStream `internal sealed` and revisit if a real consumer reports chunked-write regressions.*

2. **`IdempotencyRecord` is a `[MessagePackObject]`-friendly POCO with explicit member ordering** (resolves Q308). Shape: `int StatusCode`, `Dictionary<string, string[]> Headers`, `byte[] Body`, `DateTimeOffset CreatedAt`, plus a `RecordKind Kind` discriminator (see decision 4) and `byte[]? Fingerprint` (the hash that produced this record; null on InFlight markers). Verified via U5's serialization round-trip test against Memory, Redis (MessagePack), and Hybrid `ICache` providers before downstream units assume it works. The shape is small (no nested complex types, all built-in BCL types) so cross-provider serialization should "just work," but the round-trip test pins the contract.

3. **4xx cacheable list adopts brainstorm R11 verbatim** (resolves Q309). Cached: 400, 401, 403, 404, 405, 409, 410, 411, 412, 413, 414, 415, 416, 422, 451. Never cached: 408, 425, 429 (transient/retry-worthy) plus all 5xx, 1xx, 3xx. 405 and 411 in particular are treated as cacheable — replaying them returns the deterministic outcome the handler reached, which is what a retry consumer wants. Consumers needing audit-replay of 5xx replace `ShouldCacheResponse` entirely.

4. **In-flight marker uses a discriminator field on the cached payload** (resolves Q310). `IdempotencyRecord.Kind` is a `RecordKind` enum (`InFlight` | `Complete`). One cache key per request, one `GetAsync` per check, no two-prefix discipline. Alternative — separate `idem-pending:` / `idem-complete:` prefixes — avoids a discriminator field but doubles `Get` cost in the WaitAndReplay path and requires implementers to remember the prefix invariant in every code path that reads cache. The discriminator approach also lets the marker carry a `Fingerprint` (so a future write can verify "is this still the same in-flight request" before finalize — defense against TTL-eviction races; see Risk Analysis).

5. **`WaitAndReplay` without `IDistributedLockProvider` fails at startup** (resolves Q311). `IdempotencyOptionsValidator` cannot inspect DI, so the check moves to a second `IValidateOptions<IdempotencyOptions>` registered by `_AddIdempotencyCore` that takes `IServiceProvider` and confirms `GetService<IDistributedLockProvider>()` returns non-null whenever `InFlightStrategy == WaitAndReplay`. Combined with `.ValidateOnStart()` (auto-wired by `Headless.Hosting`), misconfiguration is reported on host build, not on first request. Aligns with brainstorm R18.

6. **Folder layout is flat** (resolves Q312). ~14 source files at package root + a `Resources/` subfolder. Matches `Headless.Api.FluentValidation` (4 files flat) and `Headless.Api.DataProtection` (4 files flat). Nested folders (`Middlewares/`, `Options/`) add navigation overhead for a single-purpose package.

7. **Clean delete of `src/Headless.Api.Core/Middlewares/Idempotency/`-style stub** (resolves Q313). Greenfield posture per CLAUDE.md + the #279 / transport-wrapper-drift learning. No README.md redirect note. The old `Middlewares/IdempotencyMiddleware.cs` is deleted outright; consumers discover the new package via `Headless.Api.Core/README.md` updates (which now mention `Headless.Api.Idempotency` as the location) and release notes.

8. **Endpoint-metadata delegate scope is a documentation constraint, not enforced** (resolves Q314). XML doc on `IdempotencyMetadata` and on `WithIdempotency` clearly states: *"Delegates configured via metadata should be stateless or resolve scoped services from `HttpContext.RequestServices`. Capturing a service instance from a different scope produces incorrect behavior or `ObjectDisposedException` at runtime."* Roslyn-analyzer enforcement deferred until a real consumer error report.

9. **`RequestFingerprint` reads the already-buffered body** (resolves Q315). Middleware always calls `HttpRequest.EnableBuffering()` before any fingerprint computation. The delegate receives a `HttpContext` whose request stream is buffered and positioned at zero; the delegate may consume the stream. Middleware unconditionally rewinds (`Request.Body.Position = 0`) on return so the inner handler can re-read. `MaxBodySizeForHashing` is enforced against the raw buffered body BEFORE invoking the delegate; the delegate sees only payloads within the cap.

10. **MSBuild SDK is `Headless.NET.Sdk` (not `.Web`)**. Library packages that consume HTTP types use the base SDK plus `<FrameworkReference Include="Microsoft.AspNetCore.App" />`. Confirmed across `Headless.Api.Core.csproj`, `Headless.Api.FluentValidation.csproj`, `Headless.Api.DataProtection.csproj`, `Headless.Api.MinimalApi.csproj`, `Headless.Api.Mvc.csproj`.

11. **Plan does NOT reference `Headless.MultiTenancy.Abstractions`** — package doesn't exist. `ICurrentTenant` is in `Headless.Core` namespace `Headless.Abstractions`, transitive via `Headless.Api.Core` ProjectReference. The new package's `.csproj` ProjectReferences are exactly: `Headless.Api.Core`, `Headless.Api.Abstractions`, `Headless.Caching.Abstractions`, `Headless.DistributedLocks.Abstractions`, `Headless.Hosting`, `Headless.FluentValidation`. No `Headless.MultiTenancy*` reference is needed.

12. **413 emission constructs `ProblemDetails` inline then passes through `IProblemDetailsCreator.Normalize(...)`**. `IProblemDetailsCreator` has no `PayloadTooLarge` factory, and adding one breaks the public interface for consumer custom implementations. The existing public `Normalize(ProblemDetails)` hook (defined for exactly this case) backfills title/type/extensions from the status code while letting the middleware own the body. Pattern matches the cancellation/timeout differentiation learning at `docs/solutions/api/aspnet-core-cancellation-vs-timeout-differentiation-2026-05-07.md`.

13. **`IdempotencyMiddleware` is registered scoped, not singleton**. Current Core middleware uses `TryAddSingleton<IdempotencyMiddleware>()` (works only because it's stateless). The new middleware holds per-request state (the `CaptureStream` instance, the buffered body, the resolved options snapshot). Register `services.TryAddScoped<IdempotencyMiddleware>()` per the `IMiddleware` factory contract.

14. **Descriptor codes use kebab-case** (`g:idempotency-key-reused`, `g:idempotency-in-flight`, `g:idempotency-in-flight-timeout`, `g:idempotency-body-too-large`). Brainstorm specifies this and the #279 work introduced the kebab convention (`g:tenant-required`). Existing `GeneralMessageDescriber` snake_case codes are legacy and not migrated as part of this work.

15. **`docs/llms/mediator.md` is created new**. The brainstorm R20 / Success Criteria assumed it exists. Plan creates it from scratch with two sections: (a) the four-defect rejection of `IdempotencyBehavior<,>` (this work), (b) the existing tenancy boundary guidance from #279 plus a pointer to `src/Headless.Mediator/README.md` for register-time API documentation.

---

## System-Wide Impact

| Surface | Change | Risk |
| --- | --- | --- |
| New NuGet package `Headless.Api.Idempotency` | Created; opt-in via ProjectReference | Adoption requires consumers to add a reference + change `Program.cs` |
| `Headless.Api.Core` public API | Deletes `IdempotencyMiddleware`, `IdempotencyMiddlewareOptions`, `IdempotencyMiddlewareOptionsValidator`, `SetupMiddlewares.AddIdempotencyMiddleware` (2 overloads) | Breaking — release notes call out |
| `Headless.Extensions` public API | Adds `HttpHeaderNames.IdempotentReplayed` constant | Additive |
| `Headless.Api.Idempotency` public API | New `IdempotencyMiddleware`, `IdempotencyOptions` + validator, `IdempotencyMetadata`, `InFlightStrategy`, `OversizeBehavior`, `SetupIdempotency`, `UseIdempotency()`, `WithIdempotency(...)`, `IdempotencyMessageDescriber` | New surface, [PublicAPI]-annotated |
| `docs/llms/api.md` | Adds canonical pipeline-order amendment for `UseIdempotency()` and adds the new package to the additional-packages list | Documentation |
| `docs/llms/mediator.md` | **Created** with the no-IdempotencyBehavior rationale and pointer to tenancy guidance | Documentation |
| `src/Headless.Api.Core/README.md` | Removes idempotency Key-Features bullets and Quick-Reference example | Documentation |
| `src/Headless.Mediator/README.md` | Adds short pointer to `docs/llms/mediator.md` for boundary doctrine | Documentation |
| `headless-framework.slnx` | Adds three new `<Project>` entries under `/Api/` folder | Build |
| `tests/Headless.Api.Tests.Unit/Middlewares/IdempotencyMiddlewareTests.cs` | Deleted (replaced by new test projects) | Coverage migrates with the code |

Affected parties:
- **Framework consumers (A1)**: must add `Headless.Api.Idempotency` ProjectReference and replace `AddIdempotencyMiddleware()` with `AddIdempotency(...)` + `UseIdempotency()`. New default contract (replay, not guard) is the desired behavior.
- **API clients (A2)**: existing identical retries now get the original response instead of 409. No client code changes required to benefit; SDKs that look for `Idempotent-Replayed: true` gain new observability.
- **Operators (A3)**: pipeline-order doc change; new log branches; new response header to look for in access logs.
- **#279 tenancy work (A4)**: this plan inherits whatever pipeline-order changes #279 lands. Plan documents `UseIdempotency()` immediately after `UseAuthorization()`.
- **External `zad-ngo` consumer (A5)**: out-of-repo migration; release notes cover the breaking surface change. No in-repo file affected.

---

## Implementation Units

### U1. Package and test-project scaffolding

**Goal:** Create the empty `Headless.Api.Idempotency` package, two test projects, and register them in the solution. No middleware logic yet — just the build-able skeleton.

**Requirements:** R0.

**Dependencies:** none.

**Files:**
- `src/Headless.Api.Idempotency/Headless.Api.Idempotency.csproj` (new)
- `src/Headless.Api.Idempotency/README.md` (new, skeleton — full content lands in U13)
- `src/Headless.Api.Idempotency/Resources/Messages.resx` (new, empty file with the standard resx scaffolding; entries land in U3)
- `src/Headless.Api.Idempotency/Resources/Messages.ar.resx` (new, empty)
- `tests/Headless.Api.Idempotency.Tests.Unit/Headless.Api.Idempotency.Tests.Unit.csproj` (new)
- `tests/Headless.Api.Idempotency.Tests.Integration/Headless.Api.Idempotency.Tests.Integration.csproj` (new)
- `headless-framework.slnx` (edit — three new `<Project>` entries under existing `/Api/` folder)

**Approach:**
- `.csproj` uses `<Project Sdk="Headless.NET.Sdk">` per Key Technical Decision 10. Sets `<TargetFramework>net10.0</TargetFramework>` and `<RootNamespace>Headless.Api.Idempotency</RootNamespace>` (consistent with sibling root namespaces, not the brainstorm's implicit assumption).
- `<FrameworkReference Include="Microsoft.AspNetCore.App" />` for HTTP types.
- ProjectReferences (per Key Technical Decision 11): `Headless.Api.Core`, `Headless.Api.Abstractions`, `Headless.Caching.Abstractions`, `Headless.DistributedLocks.Abstractions`, `Headless.Hosting`, `Headless.FluentValidation`. No `Headless.MultiTenancy*`.
- `<EmbeddedResource Update="Resources\Messages.resx" />` in an `<ItemGroup Label="Resources">` block per `Headless.Api.FluentValidation.csproj` pattern.
- Test projects use `Headless.NET.Sdk.Test` per CLAUDE.md test convention, with `xunit.v3.mtp-v2` PackageReference and ProjectReferences to the new package + `Headless.Testing`. The Integration project additionally references `Microsoft.AspNetCore.Mvc.Testing` (PackageReference) for `WebApplicationFactory`, matching `tests/Headless.Api.Tests.Integration/Headless.Api.Tests.Integration.csproj`.
- `headless-framework.slnx`: three new lines inside the existing `<Folder Name="/Api/">` block. Forward-slash path style.

**Patterns to follow:**
- `src/Headless.Api.FluentValidation/Headless.Api.FluentValidation.csproj` (csproj shape).
- `src/Headless.Api.DataProtection/` (flat layout precedent).
- `tests/Headless.Api.Tests.Integration/Headless.Api.Tests.Integration.csproj` (integration project shape).
- `tests/Headless.Api.FluentValidation.Tests.Unit/Headless.Api.FluentValidation.Tests.Unit.csproj` (unit project shape).

**Test scenarios:**
Test expectation: none -- scaffolding only; behavior tests land in U2 onwards. The `dotnet build` of the solution after this commit is the verification.

**Verification:** `dotnet build headless-framework.slnx` succeeds with the three new projects discovered. `dotnet test tests/Headless.Api.Idempotency.Tests.Unit/Headless.Api.Idempotency.Tests.Unit.csproj` succeeds (with zero tests).

---

### U2. Options surface, validator, enums, metadata type

**Goal:** Define the public type surface that downstream units depend on: `IdempotencyOptions`, `IdempotencyOptionsValidator`, `InFlightStrategy`, `OversizeBehavior`, `IdempotencyMetadata`.

**Requirements:** R15, R16, R22.

**Dependencies:** U1.

**Files:**
- `src/Headless.Api.Idempotency/IdempotencyOptions.cs` (new — options public class + `internal sealed IdempotencyOptionsValidator` immediately below per project convention)
- `src/Headless.Api.Idempotency/InFlightStrategy.cs` (new — `[PublicAPI] public enum InFlightStrategy { Reject = 0, WaitAndReplay = 1 }`)
- `src/Headless.Api.Idempotency/OversizeBehavior.cs` (new — `[PublicAPI] public enum OversizeBehavior { Reject = 0, PassThrough = 1 }`)
- `src/Headless.Api.Idempotency/IdempotencyMetadata.cs` (new — `[PublicAPI] public sealed class IdempotencyMetadata` carrying a single `Action<IdempotencyOptions> Configure` property and XML doc noting the delegate-scope caveat from Key Technical Decision 8)
- `tests/Headless.Api.Idempotency.Tests.Unit/IdempotencyOptionsValidatorTests.cs` (new)

**Approach:**
- `IdempotencyOptions` mirrors the brainstorm R16 shape verbatim. `IdempotencyKeyExpiration` is non-nullable `TimeSpan` (default 24h per R15 — the brainstorm change from `TimeSpan?` to non-null happens here).
- `HeaderName` defaults to `HttpHeaderNames.IdempotencyKey` (existing constant; U4 adds the *replayed* header).
- `ReplayHeaderAllowlist` initializer uses `StringComparer.OrdinalIgnoreCase` and the 10-entry default set (R12).
- `Methods` initializer uses `StringComparer.OrdinalIgnoreCase` and the 4-entry set (R2 default).
- `ShouldCacheResponse` defaults to a reference to `DefaultCachePredicate.Instance` — type defined in U8 but the reference compiles after U2 because the delegate slot is just a function pointer. Resolve circular by leaving `ShouldCacheResponse` initialized to `null` in U2 and assigning the default in `_AddIdempotencyCore` in U10. (Same pattern for `KeyDeriver`, `RequestFingerprint`, `ShouldApply` — all start `null`; null means "use built-in default".)
- Validator:
  - `IdempotencyKeyExpiration` > `TimeSpan.Zero`.
  - `MaxBodySizeForHashing` > 0.
  - `InFlightLockTimeout` > `TimeSpan.Zero`.
  - `HeaderName` not null/empty.
  - `Methods` not empty and every entry is a recognized HTTP method (validate against `HttpMethods.IsPost` / `IsPut` / etc., not a free-form string match — keep the bar tight).
  - `ReplayHeaderAllowlist` not null (empty is allowed — consumer may want zero header copy).
  - `MismatchStatusCode` ∈ {409, 422}.
  - When `InFlightStrategy == WaitAndReplay`: `InFlightLockTimeout` ≤ a reasonable upper bound (e.g., 5 minutes) — prevents accidental thread-pool exhaustion via misconfig. Bound chosen empirically; revisit if a consumer needs longer.
- `IdempotencyMetadata`: sealed, immutable post-construction. Ctor takes the `Action<IdempotencyOptions>`; property is get-only.

**Patterns to follow:**
- `src/Headless.Caching.Hybrid/HybridCacheOptions.cs` (options + validator-in-same-file pattern).
- `src/Headless.Api.Core/Middlewares/IdempotencyMiddleware.cs:15-29` (current validator shape — replaced).
- `Headless.Hosting`'s `services.Configure<TOption, TValidator>(...)` is the consumer-side entry; not invoked in U2 but shapes the validator interface (it must inherit `AbstractValidator<TOptions>` per `Headless.FluentValidation` conventions).

**Test scenarios:**
- Happy path: default-constructed `IdempotencyOptions` passes validation.
- Edge: `IdempotencyKeyExpiration = TimeSpan.Zero` fails validation with a specific property error.
- Edge: `IdempotencyKeyExpiration = -1.Days()` fails validation.
- Edge: `MaxBodySizeForHashing = 0` fails validation; `= -1` fails.
- Edge: `InFlightLockTimeout = TimeSpan.Zero` fails.
- Edge: `HeaderName = ""` and `HeaderName = null` both fail.
- Edge: `Methods = []` fails.
- Edge: `Methods = ["GET"]` fails (GET should never be in `Methods`, per R2 default; validator forbids).
- Edge: `Methods` containing a non-standard token (e.g., `"FROBNICATE"`) fails.
- Edge: `ReplayHeaderAllowlist = null` fails.
- Edge: `MismatchStatusCode = 400` fails; `= 422` passes; `= 409` passes.
- Edge: `InFlightStrategy = WaitAndReplay` + `InFlightLockTimeout = 10.Minutes()` fails (exceeds upper bound); `= 30.Seconds()` passes.
- Edge: nullable delegates (`KeyDeriver`, `RequestFingerprint`, `ShouldApply`, `ShouldCacheResponse`) all pass validation when null.
- `IdempotencyMetadata` requires non-null `Configure` delegate at construction.

**Verification:** Unit tests pass. `dotnet build` succeeds.

---

### U3. Idempotency-specific problem-details descriptors and resource strings

**Goal:** Define the four `g:*` problem-details descriptors and their resource strings, in the new package.

**Requirements:** R7 (`g:idempotency-key-reused`), R6/R9 (`g:idempotency-in-flight`, `g:idempotency-in-flight-timeout`), R14 (`g:idempotency-body-too-large`).

**Dependencies:** U1.

**Files:**
- `src/Headless.Api.Idempotency/Resources/IdempotencyMessageDescriber.cs` (new — static class mirroring `Headless.Api.Core/Resources/GeneralMessageDescriber.cs` and `IdentityMessageDescriber.cs` patterns)
- `src/Headless.Api.Idempotency/Resources/Messages.resx` (edit — populate with four entries)
- `src/Headless.Api.Idempotency/Resources/Messages.ar.resx` (edit — Arabic translations matching the framework's i18n discipline)
- `src/Headless.Api.Idempotency/Resources/Messages.Designer.cs` (auto-generated; commit as-is)
- `tests/Headless.Api.Idempotency.Tests.Unit/IdempotencyMessageDescriberTests.cs` (new)

**Approach:**
- `IdempotencyMessageDescriber` is `[PublicAPI] internal static class` returning `ErrorDescriptor` instances (the `Headless.Api.Abstractions.ErrorDescriptor` type used across the framework). Four static methods:
  - `KeyReused()` → code `g:idempotency-key-reused`, message from `Messages.resx`.
  - `InFlight()` → code `g:idempotency-in-flight`.
  - `InFlightTimeout()` → code `g:idempotency-in-flight-timeout`.
  - `BodyTooLarge()` → code `g:idempotency-body-too-large`.
- Codes are kebab-case per Key Technical Decision 14. Each method takes no parameters (matches `GeneralMessageDescriber.DuplicatedRequest()` shape).
- Messages.resx entries — English baseline:
  - `g:idempotency-key-reused` → "Same idempotency key reused with a different request body."
  - `g:idempotency-in-flight` → "An identical request with this idempotency key is still being processed. Retry after a short backoff."
  - `g:idempotency-in-flight-timeout` → "Timed out waiting for an in-flight request with this idempotency key to complete."
  - `g:idempotency-body-too-large` → "Request body exceeds the configured limit for idempotency processing."
- Arabic translations: defer to the project's i18n convention; if no Arabic translator review is set up at commit time, leave `.ar.resx` with English placeholders flagged with a `# TODO(i18n)` comment in a sibling text file — explicitly NOT a Roslyn comment in the .resx — and open a follow-up todo in `docs/todos/`. The framework's existing `Messages.ar.resx` files have the same coverage gap pattern.

**Patterns to follow:**
- `src/Headless.Api.Core/Resources/GeneralMessageDescriber.cs:14-17` (descriptor static-method template).
- `src/Headless.Api.Core/Resources/IdentityMessageDescriber.cs` (per-feature describer in own file template).
- `src/Headless.Api.Core/Resources/Messages.resx:177` (resx entry layout).

**Test scenarios:**
- Happy path: each describer method returns a non-null `ErrorDescriptor` with the expected `Code`, non-empty `Message`.
- Edge: `Code` exactly matches the kebab-case string (no accidental snake_case drift).
- Integration with `IProblemDetailsCreator`: feed the descriptor into `creator.Conflict(descriptor)` and `creator.UnprocessableEntity(...)` and assert the resulting `ProblemDetails.Type` URL fragment includes the code. *Covers R7, R6, R9.*

**Verification:** Unit tests pass. Resource strings load in both English and Arabic culture (test culture-specific resolution explicitly).

---

### U4. Add `Idempotent-Replayed` header constant

**Goal:** Surface the response-side replay header as a typed constant in `HttpHeaderNames`.

**Requirements:** R13.

**Dependencies:** none (independent of the new package).

**Files:**
- `src/Headless.Extensions/Constants/HttpHeaderNames.cs` (edit — add one constant)
- `tests/Headless.Extensions.Tests.Unit/Constants/HttpHeaderNamesTests.cs` (edit if exists, else create — assert the constant value)

**Approach:**
- Add `public const string IdempotentReplayed = "Idempotent-Replayed";` adjacent to the existing `IdempotencyKey` constant.
- Note the deliberate absence of the `X-` prefix per brainstorm R13 (Stripe convention for SDK overlap).

**Patterns to follow:**
- `src/Headless.Extensions/Constants/HttpHeaderNames.cs:16` (`IdempotencyKey` declaration shape).

**Test scenarios:**
- Edge: constant value is exactly `"Idempotent-Replayed"` (no prefix, exact casing).

**Verification:** Build succeeds; `Headless.Extensions` consumers across the repo compile.

---

### U5. `IdempotencyRecord` data type and `CaptureStream` decorator

**Goal:** Define the binary-serializable record stored in `ICache` and the response-stream wrapper that captures bytes during execution.

**Requirements:** R8, R10.

**Dependencies:** U1.

**Files:**
- `src/Headless.Api.Idempotency/IdempotencyRecord.cs` (new — `internal sealed` POCO; ALSO defines `internal enum RecordKind { InFlight, Complete }`)
- `src/Headless.Api.Idempotency/CaptureStream.cs` (new — `internal sealed class CaptureStream : Stream`)
- `tests/Headless.Api.Idempotency.Tests.Unit/IdempotencyRecordSerializationTests.cs` (new)
- `tests/Headless.Api.Idempotency.Tests.Unit/CaptureStreamTests.cs` (new)
- `tests/Headless.Api.Idempotency.Tests.Integration/IdempotencyRecordSerializationCrossProviderTests.cs` (new — round-trip across Memory, Redis, Hybrid `ICache`)

**Approach:**
- `IdempotencyRecord`: `int StatusCode`, `Dictionary<string, string[]> Headers`, `byte[] Body`, `DateTimeOffset CreatedAt`, `RecordKind Kind`, `byte[]? Fingerprint`. All public-get/init-set; default constructor for serialization. Decorate with `[MessagePackObject(true)]` per the framework's Redis-cache serialization contract (verify by reading `src/Headless.Caching.Redis/`'s configuration). If `Headless.Caching.Memory` and `Headless.Caching.Hybrid` use different serializers, ensure each round-trips identically — the U5 integration test covers this.
- `CaptureStream`:
  - Wraps `Stream inner` (the original `HttpResponse.Body`) and an internal `MemoryStream buffer`.
  - All `Write*` overloads write to both `inner` and `buffer`. `Flush*` forwards to `inner` only.
  - `Length`, `Position` etc. forward to `inner`. `CanRead = false`, `CanWrite = true`, `CanSeek = false`.
  - `byte[] CapturedBytes` property exposes the buffer's contents.
  - The buffer has a hard cap matching `MaxBodySizeForHashing × 2` (response can exceed the request cap; doubled to be conservative); writes beyond the cap **stop appending to the buffer** but continue forwarding to `inner` and set a `bool TruncatedCapture { get; private set; }` flag. The middleware reads this flag at finalize time; if set, it logs a Warning and does NOT cache the record (replay would be incomplete). This is a v1 safety valve; revisit if real responses regularly exceed the cap.
  - `Dispose(bool)` disposes the `buffer` (not the `inner`; the response pipeline owns inner).
- The `CaptureStream` design is the tee variant per Key Technical Decision 1.

**Patterns to follow:**
- Any existing `Stream` decorator in the framework — `grep -r "class.*: Stream" src/` to find precedents. If none exist, fall back to ASP.NET Core's `Microsoft.AspNetCore.WebUtilities.FileBufferingWriteStream` as a reference (do NOT take a dependency — write our own).
- `src/Headless.Caching.Abstractions/Contracts/CacheValue.cs` (for the cache value shape on lookup).

**Test scenarios:**

*`IdempotencyRecord` serialization:*
- Happy path: round-trip a `Complete` record with status, headers (multi-value entries), body, fingerprint, kind through `ICache.UpsertAsync` + `GetAsync` against the in-memory cache; assert byte-equivalence on retrieval.
- Edge: round-trip an `InFlight` marker (Body = empty array, Fingerprint = null).
- Edge: headers dictionary with empty value array, single-value, multi-value entries.
- Edge: body containing all 256 byte values (round-trip preserves binary integrity).
- Edge: very small body (1 byte), empty body (0 bytes), body at the max cap (e.g., 1 MiB - 1).
- Integration: round-trip via Redis cache (`Headless.Caching.Redis`) using Testcontainers Redis — covers MessagePack serialization. *Covers cross-provider serialization (Key Technical Decision 2).*
- Integration: round-trip via Hybrid cache (`Headless.Caching.Hybrid`) — covers the L1+L2 path.

*`CaptureStream`:*
- Happy path: `Write(byte[])` forwards to inner AND populates `CapturedBytes`.
- Happy path: `WriteAsync` forwards correctly.
- Edge: empty write (zero bytes) — both streams see zero-byte write, `CapturedBytes` unchanged.
- Edge: write exceeding the cap — `CapturedBytes` length equals cap, `TruncatedCapture = true`, inner stream received full payload.
- Edge: multiple writes — `CapturedBytes` is the concatenation in order.
- Edge: `Flush()` on the wrapper forwards to inner only; calling `Flush()` does not affect captured bytes.
- Edge: `Dispose()` does not dispose the inner stream.
- Error path: writing to a disposed `CaptureStream` throws `ObjectDisposedException`.

**Verification:** Unit tests pass. Cross-provider serialization integration tests pass (skipped if Docker is unavailable; tests must be tagged with the framework's Testcontainers-conditional trait — verify the existing pattern at `tests/Headless.Caching.Redis.Tests.Integration/`).

---

### U6. `IdempotencyMiddleware` — core paths: pass-through, cache miss, cache hit + hash match

**Goal:** First substantive middleware behavior. Read header → buffer body → compute fingerprint → derive cache key → branch: pass-through (no key / wrong method / `ShouldApply == false`), cache miss (sentinel insert + capture + finalize), cache hit + hash match (replay).

**Requirements:** R1, R2, R3, R4 (default raw-body SHA-256 path), R5, R8 (happy-path miss → finalize), R13 (write `Idempotent-Replayed: true` on replay).

**Dependencies:** U2, U3, U4, U5.

**Execution note:** Add a failing integration-style test against the request/response contract (one of the AEs, ideally AE1) before writing middleware code. The test goes red, the middleware fills it in, the test goes green. Subsequent units extend; this unit sets the contract.

**Files:**
- `src/Headless.Api.Idempotency/IdempotencyMiddleware.cs` (new — implements `IMiddleware`)
- `tests/Headless.Api.Idempotency.Tests.Unit/IdempotencyMiddlewareTests.cs` (new)

**Approach:**
- Constructor injects: `IOptionsSnapshot<IdempotencyOptions>`, `ICache`, `ICurrentTenant`, `IProblemDetailsCreator`, `IClock`, `ICancellationTokenProvider`, `ILogger<IdempotencyMiddleware>`, `IServiceProvider` (for lazy `IDistributedLockProvider` resolution in U7).
- `InvokeAsync(HttpContext context, RequestDelegate next)`:
  1. Read endpoint metadata via `context.GetEndpoint()?.Metadata.GetMetadata<IdempotencyMetadata>()`. If present, shallow-clone the app-level options and apply the metadata's `Configure` delegate. Otherwise use the snapshot directly. Per-request merge details in U9; U6 stub reads metadata but only the scalar values (`IdempotencyKeyExpiration`, etc.) — full delegate-merge wiring lands in U9.
  2. Read `options.HeaderName` from `Request.Headers`. If empty/whitespace → `await next(context); return;` (R1).
  3. Check `Request.Method` ∈ `options.Methods` (case-insensitive). If not → pass-through (R2).
  4. If `options.ShouldApply != null && options.ShouldApply(context) == false` → pass-through.
  5. Call `Request.EnableBuffering()`. Buffer body up to `options.MaxBodySizeForHashing` (oversize handling lands in U8; in U6, oversize fails the test setup — implementer can throw `NotImplementedException` for now or just respect the cap with a basic check). Compute SHA-256 over the buffered bytes. Rewind `Request.Body.Position = 0`.
  6. Build cache key per R3: `idem:{tenant}:{method}:{path}:{idempotency-key}`. Tenant from `ICurrentTenant.Id` (empty string for null). Method uppercased. Path from `Request.Path.Value ?? ""`. Header value as-is (last value if multi-valued — `Request.Headers[options.HeaderName].LastOrDefault() ?? ""`).
  7. `var existing = await cache.GetAsync<IdempotencyRecord>(key, ct);`
  8. **`existing.HasValue && existing.Value.Kind == Complete && existing.Value.Fingerprint.SequenceEqual(currentFingerprint)`** → replay path. (Mismatch path is U7; in-flight branch is U7.)
     - Set `Response.StatusCode = existing.Value.StatusCode`.
     - For each entry in `existing.Value.Headers` whose key is in `options.ReplayHeaderAllowlist`, write to `Response.Headers`. (Allowlist filtering at capture time lands in U8; U6 writes all stored headers — implementer should still respect the allowlist at write time defensively.)
     - Write body bytes to `Response.Body` (`await Response.Body.WriteAsync(existing.Value.Body, ct)`).
     - Add `Response.Headers[HttpHeaderNames.IdempotentReplayed] = "true";`.
     - Log Information: "Idempotency replay hit for key {KeyPrefix}".
     - Do NOT call `next`.
  9. **`existing.HasValue == false`** (miss):
     - Build the in-flight marker: `new IdempotencyRecord { Kind = InFlight, Fingerprint = currentFingerprint, CreatedAt = clock.UtcNow }`.
     - `var inserted = await cache.TryInsertAsync(key, marker, options.InFlightLockTimeout + 5.Seconds(), ct);`
     - If `inserted == false`: re-read and route to in-flight branch — but that branch is U7. For U6, treat the race-loss as: `return existing-check loop` (re-call GetAsync, route accordingly). If marker is now `Complete` + hash match, replay (per step 8). Otherwise, fall through — U7 fills in the rest.
     - If `inserted == true`: wrap `Response.Body = new CaptureStream(originalBody)`. Save reference to the `CaptureStream`. `await next(context);`.
     - On return:
       - Restore `Response.Body = originalBody` (CaptureStream's purpose is captures; the actual writes have already gone to the originalBody via the tee).
       - If the response is eligible (`ShouldCacheResponse` predicate; U8 defines default — in U6 cache everything) AND `CaptureStream.TruncatedCapture == false`:
         - Build `IdempotencyRecord { Kind = Complete, StatusCode = Response.StatusCode, Headers = capture filtered headers, Body = captureStream.CapturedBytes, Fingerprint = currentFingerprint, CreatedAt = clock.UtcNow }`.
         - `await cache.UpsertAsync(key, completeRecord, options.IdempotencyKeyExpiration, ct);`
       - Else: `await cache.RemoveAsync(key, ct);` (so subsequent retries re-execute — sentinel was tentative).
- The IO sequence (especially step 9 finalize) MUST tolerate exceptions thrown by `next(context)` — surface them after RemoveAsync on the marker. Use `try/catch` around `next` with a `finally` block that does the removal-on-failure step. The "do not cache on exception" rule is implicit in R11 (5xx never cached) but the exception itself may bypass the response-write entirely.

**Patterns to follow:**
- `src/Headless.Api.Core/Middlewares/IdempotencyMiddleware.cs` (current scoped IMiddleware shape — DO NOT copy the singleton registration; that changes per Key Technical Decision 13).
- `tests/Headless.Api.Tests.Unit/Middlewares/IdempotencyMiddlewareTests.cs` (existing unit-test factory pattern for mocked deps).
- `src/Headless.Caching.Abstractions/ICache.cs:44, 138, 28, 161` (method signatures).

**Test scenarios:**

*Pass-through:*
- Happy path: GET request with no idempotency key → `next` called once, no cache interaction.
- Happy path: POST without idempotency-key header → `next` called once, no cache interaction.
- Edge: POST with whitespace-only `X-Idempotency-Key` → pass-through (R1).
- Edge: POST with `X-Idempotency-Key` having empty string → pass-through.
- Edge: PUT with valid key + `ShouldApply` returning false → pass-through, no cache interaction. *(ShouldApply hook itself lands in U9; the U6 happy-path version may assume `ShouldApply == null` — confirm during implementation.)*

*Cache miss → execute + finalize:*
- Happy path: POST with `X-Idempotency-Key: k1` and body, `cache.GetAsync` returns NoValue, `TryInsertAsync` returns true, `next` is invoked, response 201 + headers written, `UpsertAsync` called with a `Complete` record carrying the captured status/headers/body/fingerprint. *Covers AE1, AE7.*
- Edge: cache key composition for tenant `T1` is `idem:T1:POST:/v1/x:k1`. *Covers AE7.*
- Edge: cache key composition for null tenant is `idem::POST:/v1/x:k1` (double colon, no `_global` literal). *Covers AE8.*
- Edge: multi-valued `X-Idempotency-Key` header — only the last value is used.
- Edge: path with query string — the cache key uses `Request.Path.Value` (excludes query).
- Edge: `next` throws → marker is removed from cache (no `Complete` record stored).
- Edge: response status is 503 → `ShouldCacheResponse` returns false (U8 default; U6 default-on-everything is fine for the unit but the AE5 integration test catches the real behavior).

*Cache hit, hash match → replay:*
- Happy path: POST with `X-Idempotency-Key: k1`, body matching stored fingerprint, `GetAsync` returns `Complete` record → response is the stored status + allowlisted headers + body + `Idempotent-Replayed: true`. `next` is NOT invoked. *Covers AE1 (R5, R13).*
- Edge: replay copies a multi-value header (e.g., `Set-Cookie` if mistakenly stored — though the allowlist should drop it; U6 covers the write path, U8 covers the allowlist filter).
- Edge: replay sets `Content-Length` correctly via the body-write (do not pre-set; let Kestrel infer).
- Edge: replay does NOT call any `IClock` or `next` interactions.

**Verification:** Unit tests pass. `dotnet build` of the new package succeeds.

---

### U7. Middleware — mismatch (422), in-flight Reject (409), WaitAndReplay (lock + recheck + 409 timeout)

**Goal:** The three error/concurrency branches: hash mismatch, in-flight under `Reject`, in-flight under `WaitAndReplay`.

**Requirements:** R6, R7, R9.

**Dependencies:** U6, U3.

**Files:**
- `src/Headless.Api.Idempotency/IdempotencyMiddleware.cs` (edit — fill in mismatch and in-flight branches)
- `tests/Headless.Api.Idempotency.Tests.Unit/IdempotencyMiddlewareTests.cs` (edit — add branch tests)

**Approach:**

*Hash mismatch (R7):*
- When `existing.HasValue && existing.Value.Kind == Complete && existing.Value.Fingerprint.SequenceEqual(currentFingerprint) == false`:
  - Build a single-error `ErrorDescriptor` via `IdempotencyMessageDescriber.KeyReused()`.
  - Set `Response.StatusCode = options.MismatchStatusCode` (default 422 per R7).
  - Emit problem details:
    - If `MismatchStatusCode == 422`: prefer `creator.UnprocessableEntity(new Dictionary<string, List<ErrorDescriptor>> { ["idempotency_key"] = [descriptor] })` if the signature fits; if R7 needs a simpler `(detail, error)` shape (single error), construct `ProblemDetails` inline and pass through `creator.Normalize(...)`. Verify the actual `IProblemDetailsCreator` shape during implementation; the brainstorm R7 says "only the conflicting `idempotency_key` value and a generic message."
    - If `MismatchStatusCode == 409`: `creator.Conflict(descriptor)`.
  - Log Warning: "Idempotency key {KeyPrefix} reused with different body. Expected fingerprint prefix {ExpectedHex} got {ObservedHex}." Hex prefixes are first 8 bytes; never write full fingerprints to log to avoid Body-content disclosure via timing-channel logs.
  - Do NOT call `next`.
  - *Covers AE2.*

*In-flight `Reject` (R6):*
- When `existing.HasValue && existing.Value.Kind == InFlight` OR `TryInsertAsync` returned false and the subsequent `GetAsync` still shows `InFlight`:
  - Branch on `options.InFlightStrategy`. `Reject` path:
    - `creator.Conflict(IdempotencyMessageDescriber.InFlight())`.
    - Set status 409. Write body via `Results.Problem(...).ExecuteAsync(context)` or equivalent — match the pattern from `Headless.Api.Core/Middlewares/IdempotencyMiddleware.cs:64-69`.
    - Log Information: "Idempotency in-flight Reject for key {KeyPrefix}".
    - Do NOT call `next`.
  - *Covers AE3.*

*In-flight `WaitAndReplay` (R9):*
- `WaitAndReplay` path:
  - Lazily resolve `var lockProvider = services.GetRequiredService<IDistributedLockProvider>();` (registration-time validation in U10 guarantees it's there).
  - `var lockKey = $"lock:idem:{tenant}:{method}:{path}:{key}";`
  - `await using var dlock = await lockProvider.TryAcquireAsync(lockKey, timeUntilExpires: options.InFlightLockTimeout + 5.Seconds(), acquireTimeout: options.InFlightLockTimeout, ct);`
  - If `dlock is null`: timed-out waiting. Return 409 `g:idempotency-in-flight-timeout`.
  - Re-check cache: `var afterLock = await cache.GetAsync<IdempotencyRecord>(key, ct);`
    - If `Complete` + hash match → replay (delegate to U6's replay logic).
    - If `Complete` + hash mismatch → 422 (delegate to mismatch logic above). *(Edge: A finalized with a different fingerprint somehow — race exotic, but treat as mismatch.)*
    - If `InFlight` → 409 `g:idempotency-in-flight-timeout` (winner crashed between marker and finalize; the lock acquired after the marker TTL expired but before a new request inserted a fresh marker. Conservative: do NOT execute fresh; tell the client to retry. Lock auto-releases on dispose.)
    - If `NoValue` → 409 `g:idempotency-in-flight-timeout` (TTL'd out; same conservative stance).
  - *Covers AE4.*

**Notes on the cache state-machine:** the WaitAndReplay path explicitly handles all four post-lock cache states (Complete-match / Complete-mismatch / InFlight-still / NoValue). Without this enumeration, the middleware risks the TOCTOU bugs catalogued in `docs/solutions/concurrency/circuit-breaker-transport-thread-safety-patterns.md` (learning #3 in research).

**Notes on lock provider:** `IDistributedLockProvider.TryAcquireAsync` returns `IDistributedLock?` (null on failure). `IDistributedLock` is `IAsyncDisposable`. The `await using` pattern handles release; the lock TTL (`timeUntilExpires`) guards against a crashed waiter. The 30s default `InFlightLockTimeout` is the brainstorm R9 default. The waiter-fairness bug in `docs/todos/005-pending-p3-improve-waiter-signaling-in-distributedlockprovide.md` is an operational caveat noted in Risk Analysis but does NOT block this RFC.

**Patterns to follow:**
- `src/Headless.DistributedLocks.Abstractions/RegularLocks/IDistributedLockProvider.cs` (acquisition contract).
- `src/Headless.DistributedLocks.Abstractions/RegularLocks/IDistributedLock.cs` (dispose semantics).
- `src/Headless.Api.Core/Middlewares/IdempotencyMiddleware.cs:64-69` (current `Results.Problem` emission pattern).

**Test scenarios:**

*Hash mismatch:*
- Happy path: `Complete` record with fingerprint `[A]`, retry with body producing fingerprint `[B]` → 422 with `g:idempotency-key-reused`, original record unchanged. *Covers AE2 (R7).*
- Edge: default `MismatchStatusCode` is 422.
- Edge: `MismatchStatusCode = 409` configured → response is 409 with the same problem-details code.
- Edge: response problem-details body contains `idempotency_key` field with the actual key value, and a generic message — does NOT contain hash bytes (verify by parsing the JSON and asserting no hex string appears).
- Integration: server logs at Warning level contain a hex prefix on the mismatch — verify via `ILogger` capture.

*In-flight Reject:*
- Happy path: existing record has `Kind = InFlight` → 409 with `g:idempotency-in-flight`. `next` not invoked.
- Edge: `TryInsertAsync` race-loses, subsequent `GetAsync` returns `InFlight` → 409.
- Edge: `TryInsertAsync` race-loses, subsequent `GetAsync` returns `Complete` + hash match → replay (NOT 409). Tests the race-recovery path.
- Edge: `TryInsertAsync` race-loses, subsequent `GetAsync` returns `Complete` + hash mismatch → 422.
- Edge: `TryInsertAsync` race-loses, subsequent `GetAsync` returns `NoValue` (winner crashed, TTL elapsed before our recheck) → retry the whole flow once; if still racing, return 409 in-flight. *(The bound-once-then-409 keeps the loop finite.)*

*WaitAndReplay:*
- Happy path: `InFlight` marker present, lock acquires within timeout, post-lock GetAsync returns `Complete` + match → replay with `Idempotent-Replayed: true`. *Covers AE4 winner path.*
- Edge: lock acquires within timeout, post-lock GetAsync returns `Complete` + hash mismatch → 422.
- Edge: lock acquires within timeout, post-lock GetAsync returns `InFlight` → 409 `g:idempotency-in-flight-timeout`.
- Edge: lock acquires within timeout, post-lock GetAsync returns `NoValue` → 409 `g:idempotency-in-flight-timeout`.
- Error path: `TryAcquireAsync` returns null (acquire-timeout) → 409 `g:idempotency-in-flight-timeout`. *Covers AE4 timeout path.*
- Edge: lock TTL (`timeUntilExpires`) is `InFlightLockTimeout + 5s` so a holder that crashes mid-replay-decision releases before the client gives up.
- Integration: NSubstitute `IDistributedLockProvider` resolves from `IServiceProvider`; assert `services.GetService<IDistributedLockProvider>()` is invoked exactly when `WaitAndReplay` configured and an in-flight marker is encountered (i.e., lazy resolution per R18).

**Verification:** Unit tests pass. Manual review of the cache state-machine matrix above confirms all four post-lock states are handled.

---

### U8. Middleware — oversize body, status predicate, header allowlist filtering

**Goal:** Three orthogonal behaviors that complete the "what gets cached" surface: body-cap enforcement with Reject/PassThrough, default `ShouldCacheResponse` predicate (R11), and `ReplayHeaderAllowlist` filtering at capture time.

**Requirements:** R4 (cap enforcement, integrating with U6's buffer step), R11, R12, R13 (header allowlist + Idempotent-Replayed always-on), R14.

**Dependencies:** U6, U3.

**Files:**
- `src/Headless.Api.Idempotency/IdempotencyMiddleware.cs` (edit — body-cap branching, allowlist filter, integrate predicate)
- `src/Headless.Api.Idempotency/DefaultCachePredicate.cs` (new — `internal sealed class DefaultCachePredicate` exposing a static `Func<HttpContext, bool> Instance` property; or equivalent static method exposing the predicate)
- `tests/Headless.Api.Idempotency.Tests.Unit/IdempotencyMiddlewareTests.cs` (edit — branch tests)
- `tests/Headless.Api.Idempotency.Tests.Unit/DefaultCachePredicateTests.cs` (new)

**Approach:**

*Body cap (R4, R14):*
- Replace U6's basic buffer step with the full implementation:
  - Call `Request.EnableBuffering(bufferThreshold: options.MaxBodySizeForHashing + 1, bufferLimit: options.MaxBodySizeForHashing + 1)`. The `+1` lets us detect overflow without paying for an extra MB of buffer.
  - Read the request body into a `byte[]` up to `MaxBodySizeForHashing + 1` bytes. If `bytesRead > MaxBodySizeForHashing` (or stream is not at EOF with `bytesRead == MaxBodySizeForHashing + 1`) → oversize branch.
  - **`options.OversizeBehavior == Reject`** (default):
    - Construct `ProblemDetails { Status = 413, Type = "...g:idempotency-body-too-large", Title = ..., Detail = IdempotencyMessageDescriber.BodyTooLarge().Message }` inline + `creator.Normalize(pd)`.
    - Write as `Results.Problem(pd).ExecuteAsync(context)`.
    - Log Warning: "Idempotency request body exceeded cap {Cap} (observed >= {ObservedPrefix})".
    - Do NOT call `next`.
    - *Covers AE6 Reject.*
  - **`options.OversizeBehavior == PassThrough`**:
    - Log Warning: "Idempotency body cap exceeded; passing through without idempotency guarantee. Cap {Cap}, observed >= {ObservedPrefix}, key {KeyPrefix}".
    - Rewind request stream to position 0.
    - `await next(context); return;` — no `Idempotent-Replayed` header, no cache writes, no cache reads.
    - *Covers AE6 PassThrough.*
- Within-cap path: same as U6 (rewind, compute fingerprint, proceed).

*`ShouldCacheResponse` predicate (R11):*
- `DefaultCachePredicate.Instance` is a `Func<HttpContext, bool>` that returns true iff `Response.StatusCode` is in the cacheable set:
  - 2xx: any status 200–299 → true.
  - 4xx cacheable: 400, 401, 403, 404, 405, 409, 410, 411, 412, 413, 414, 415, 416, 422, 451 → true.
  - 4xx transient: 408, 425, 429 → false.
  - 4xx other (e.g., 417, 418, 420, 424, 426, 428, 431, 444, 449, 450, 494…499) → false (conservative; treat as transient/unknown).
  - 5xx: any 500–599 → false.
  - 1xx, 3xx: false.
- Wire as the default for `options.ShouldCacheResponse` in U10's `_AddIdempotencyCore` (when consumer didn't supply one).
- In the middleware finalize step (U6's step 9 finalize), call `var effectivePredicate = options.ShouldCacheResponse ?? DefaultCachePredicate.Instance; var shouldCache = effectivePredicate(context);`.

*Header allowlist filtering at capture time (R12):*
- When building the `IdempotencyRecord` after `next` completes, copy only the headers whose names are in `options.ReplayHeaderAllowlist` (case-insensitive). Drop `Set-Cookie`, `traceparent`, `Date`, `Server`, etc. by default.
- This filter runs at capture (storage) time, NOT at replay time. Result: storage size stays small; replay path doesn't need filtering (already filtered).
- The `Idempotent-Replayed: true` header is added on replay, NOT stored in the record (always-on per R13).

**Patterns to follow:**
- `src/Headless.Api.Core/Abstractions/IProblemDetailsCreator.cs:152` (`Normalize(ProblemDetails)` public hook).
- ASP.NET `HttpRequest.EnableBuffering(bufferThreshold, bufferLimit)` semantics (Microsoft docs).
- `Headless.Api.Core/HeadlessProblemDetailsConstants.cs` (any existing title/type constants for 413).

**Test scenarios:**

*Oversize Reject:*
- Happy path: POST with body == `MaxBodySizeForHashing` bytes → executed normally (under cap).
- Edge: POST with body == `MaxBodySizeForHashing + 1` bytes, `OversizeBehavior = Reject` → 413 with `g:idempotency-body-too-large`, `next` NOT invoked. *Covers AE6 Reject.*
- Edge: chunked-transfer body that streams over the cap → detected during buffering, same 413 outcome.
- Edge: PassThrough config + cap-exceeding body → `next` called once, response NOT cached, no `Idempotent-Replayed` header on subsequent identical retries. *Covers AE6 PassThrough.*
- Edge: PassThrough config + cap-exceeding body → log emits at Warning severity with `key`, `observed-prefix-length`.
- Edge: body exactly at the cap (`MaxBodySizeForHashing` bytes) — accepted, processed normally.

*Default predicate:*
- Happy path: 200, 201, 202, 204, 299 → cache. 422 → cache. 405 → cache. 411 → cache. 451 → cache. *Covers AE5 422-cached.*
- Edge: 408 → no cache (transient).
- Edge: 425, 429 → no cache.
- Edge: 500, 502, 503, 504 → no cache. *Covers AE5 503-not-cached.*
- Edge: 100, 101, 301, 302 → no cache.
- Edge: 417, 418 → no cache (conservative).
- Edge: predicate replaced by consumer to `_ => true` → 503 IS cached (consumer override works).

*Header allowlist filtering:*
- Happy path: response with `Content-Type`, `Location`, `Set-Cookie`, `traceparent`, custom `X-MyHeader` → stored record contains only `Content-Type` and `Location` (default allowlist). *Covers AE9.*
- Edge: replay reads only the allowlisted headers; never writes `Set-Cookie` or `traceparent` to the response.
- Edge: consumer extends allowlist with `X-MyHeader` → stored record now includes it; replay copies it.
- Edge: empty allowlist → only `Idempotent-Replayed` appears on replay (no copied headers).
- Edge: case-insensitive matching: stored as `Content-Type`, allowlist contains `content-type` → matched.
- Edge: multi-valued header (`Set-Cookie: a`, `Set-Cookie: b`) → both values dropped if `Set-Cookie` not allowlisted; both copied if it is.

**Verification:** Unit tests pass. Read the predicate body against R11 line-by-line; missing entries fail review.

---

### U9. Middleware — custom hooks (`KeyDeriver`, `RequestFingerprint`, `ShouldApply`) and per-endpoint metadata merge

**Goal:** Plug in the three consumer extension points and wire the per-endpoint metadata override.

**Requirements:** R22, R23, R24, R25 (and the `ShouldApply` hook from U6 step 4).

**Dependencies:** U6, U2.

**Files:**
- `src/Headless.Api.Idempotency/IdempotencyMiddleware.cs` (edit — replace default-only paths with hook-aware paths)
- `src/Headless.Api.Idempotency/EndpointConventionBuilderExtensions.cs` (new — `WithIdempotency(...)` extension)
- `tests/Headless.Api.Idempotency.Tests.Unit/IdempotencyMiddlewareCustomHooksTests.cs` (new)
- `tests/Headless.Api.Idempotency.Tests.Unit/WithIdempotencyEndpointMetadataTests.cs` (new)

**Approach:**

*`WithIdempotency` extension (R22):*
- Namespace: `Microsoft.AspNetCore.Builder` (matches the #279 pattern at `src/Headless.Api.Core/MultiTenancy/EndpointConventionBuilderExtensions.cs`; uses `#pragma warning disable IDE0130` + `// ReSharper disable once CheckNamespace`).
- Signature: `public static TBuilder WithIdempotency<TBuilder>(this TBuilder builder, Action<IdempotencyOptions> configure) where TBuilder : IEndpointConventionBuilder`.
- Implementation: `builder.WithMetadata(new IdempotencyMetadata(configure)); return builder;`.
- `[PublicAPI]` annotation.

*Per-endpoint metadata merge at request time (R23):*
- Refactor U6's "step 1" into a private helper `IdempotencyOptions ResolveOptions(HttpContext context, IdempotencyOptions appLevel)`:
  - `var metadata = context.GetEndpoint()?.Metadata.GetMetadata<IdempotencyMetadata>();`
  - If `metadata is null`: return `appLevel` (no allocation).
  - Else: shallow-clone `appLevel`:
    - Scalar properties: copy directly.
    - `Methods` and `ReplayHeaderAllowlist`: copy to fresh `HashSet<string>(StringComparer.OrdinalIgnoreCase)` instances.
    - Delegates (`KeyDeriver`, `RequestFingerprint`, `ShouldApply`, `ShouldCacheResponse`): copy by reference.
  - Apply `metadata.Configure(clone);` to the clone.
  - Return clone.
- `HeaderName` is the structural exception per Key Technical Decision merge-table: it's read from the request BEFORE metadata lookup (in U6 step 2), so a per-endpoint `HeaderName` override is ignored. Document in `IdempotencyMetadata` XML doc.

*`ShouldApply` (used in U6 step 4 but not formally wired until U9):*
- `if (options.ShouldApply != null && options.ShouldApply(context) == false) { await next(context); return; }`.

*`KeyDeriver` (R25):*
- Replace U6's R3 default composition with: `var cacheKey = options.KeyDeriver != null ? options.KeyDeriver(context, headerValue) : DefaultKeyDeriver(context, headerValue, currentTenant);`.
- `DefaultKeyDeriver` is `internal static` and implements R3 verbatim.
- The consumer's `KeyDeriver` receives the raw header value; it's responsible for incorporating tenant/scope/user IDs as needed.
- *Covers AE14.*

*`RequestFingerprint` (R24):*
- Replace U8's raw-body SHA-256 path with: `var fingerprint = options.RequestFingerprint != null ? await options.RequestFingerprint(context) : DefaultFingerprint(bufferedBytes);`.
- When `RequestFingerprint` is supplied:
  - The middleware still calls `EnableBuffering()` and still enforces `MaxBodySizeForHashing` (R24 explicit requirement).
  - The middleware still rewinds the request stream after the delegate returns (unconditional).
  - The delegate's output bytes are used verbatim as the fingerprint.
- The delegate receives a `HttpContext` whose request body is buffered and ready to read.
- *Covers AE13.*

*Endpoint metadata merge correctness:*
- The merge happens ONCE per request, at the start. Subsequent reads in the middleware use the resolved options snapshot, not the app-level snapshot. (Important: do NOT mix `appLevel.IdempotencyKeyExpiration` and `resolved.MaxBodySizeForHashing` etc.)
- *Covers AE12.*

**Patterns to follow:**
- `src/Headless.Api.Core/MultiTenancy/EndpointConventionBuilderExtensions.cs` (`.AllowMissingTenant()` shape from #279 — exact template for `.WithIdempotency`).
- `src/Headless.Api.Core/HeadlessApiBuilderExtensions.cs` (other `IEndpointConventionBuilder` extension patterns).

**Test scenarios:**

*`WithIdempotency`:*
- Happy path: `endpoints.MapPost("/x", ...).WithIdempotency(o => o.IdempotencyKeyExpiration = 7.Days())` → endpoint metadata collection contains an `IdempotencyMetadata` with the configure delegate.
- Edge: calling `.WithIdempotency` twice on the same endpoint → both metadata entries attached; merge applies the LAST one (LIFO semantics from `GetMetadata`). Document as v1 behavior; consumers wanting compose-multiple-overrides do so in a single `Action`.

*Per-endpoint metadata merge:*
- Happy path: endpoint with `o.IdempotencyKeyExpiration = 7.Days()` override; request to that endpoint stores record with 7-day TTL. App-level default is 24h, sibling endpoint without metadata stores with 24h. *Covers AE12.*
- Edge: app-level `Methods = {POST}`, metadata override `Methods = {POST, PUT}` (a fresh set) → endpoint accepts PUT; sibling endpoint without metadata still rejects PUT.
- Edge: app-level `ReplayHeaderAllowlist = {Content-Type}`, metadata override adds `X-RequestId` → endpoint cache includes X-RequestId, app-level cache does not.
- Edge: metadata override sets `KeyDeriver` (delegate by-reference) → endpoint uses the override, app-level uses its default.
- Edge: `HeaderName` override is ignored (still reads `X-Idempotency-Key` per app-level config).

*`KeyDeriver`:*
- Happy path: consumer's `KeyDeriver` returns `$"idem:{tenant}:{userId}:{method}:{path}:{header}"` → middleware uses that exact string for `GetAsync` / `TryInsertAsync` / `UpsertAsync`. Two users in the same tenant with the same `header` get distinct cache keys. *Covers AE14.*
- Edge: `KeyDeriver` returns the same string regardless of tenant/method/path → reduces to a single global cache slot for that key (consumer-induced — middleware doesn't second-guess).
- Edge: `KeyDeriver` accesses scoped services via `context.RequestServices.GetRequiredService<ICurrentUser>()`.

*`RequestFingerprint`:*
- Happy path: consumer's `RequestFingerprint` returns canonical-JSON hash for two requests with semantically-equivalent JSON but reordered keys + timestamp drift → second request hash-matches first and replays. *Covers AE13.*
- Edge: consumer's `RequestFingerprint` reads `context.Request.Body` (positioned at zero, buffered) → reads succeed; middleware rewinds afterwards; inner handler reads the body fresh.
- Edge: consumer's `RequestFingerprint` returns `byte[0]` (empty fingerprint) → middleware uses it verbatim; two empty fingerprints hash-match.
- Edge: `RequestFingerprint` + body > `MaxBodySizeForHashing` → oversize branch fires BEFORE the delegate is invoked (R24 explicit).

*`ShouldApply`:*
- Happy path: `ShouldApply = ctx => ctx.Request.Path.StartsWithSegments("/v1")` → POST `/v1/x` runs idempotency, POST `/v2/x` passes through.
- Edge: `ShouldApply = _ => false` → effectively disables idempotency for that endpoint (consumer escape hatch per R21).

**Verification:** Unit tests pass. The merge logic is tested via three distinct sibling endpoints (no metadata / metadata-A / metadata-B) sharing the same middleware instance — verifies no cross-request leakage of the resolved options.

---

### U10. `SetupIdempotency` registration class, `UseIdempotency()`, startup validation

**Goal:** Public DI surface. `services.AddIdempotency(...)` (three overloads), `app.UseIdempotency()`, conditional `IDistributedLockProvider` startup check.

**Requirements:** R0 (package public surface), R17, R18.

**Dependencies:** U2, U6, U7, U8, U9.

**Files:**
- `src/Headless.Api.Idempotency/Setup.cs` (new — file named `Setup.cs` per framework convention; type named `SetupIdempotency` per R17)
- `src/Headless.Api.Idempotency/ApplicationBuilderExtensions.cs` (new — `UseIdempotency(this IApplicationBuilder)`)
- `tests/Headless.Api.Idempotency.Tests.Unit/SetupIdempotencyTests.cs` (new)
- `tests/Headless.Api.Idempotency.Tests.Integration/SetupIdempotencyValidationTests.cs` (new — startup-time validation tests need a real host)

**Approach:**

*`SetupIdempotency` (R17):*
- `[PublicAPI] public static class SetupIdempotency` in `Headless.Api.Idempotency` namespace.
- Uses C# 14 extension members syntax (per `Headless.Caching.Redis/Setup.cs` template):

```csharp
[PublicAPI]
public static class SetupIdempotency
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddIdempotency(IConfiguration configuration) { ... }
        public IServiceCollection AddIdempotency(Action<IdempotencyOptions> setupAction) { ... }
        public IServiceCollection AddIdempotency(Action<IdempotencyOptions, IServiceProvider> setupAction) { ... }

        private IServiceCollection _AddIdempotencyCore()
        {
            services.TryAddScoped<IdempotencyMiddleware>();
            services.TryAddSingleton<IValidateOptions<IdempotencyOptions>, IdempotencyOptionsDIValidator>();
            return services;
        }
    }
}
```

- Each public overload calls `services.Configure<IdempotencyOptions, IdempotencyOptionsValidator>(...)` (FluentValidation auto-wired with `ValidateOnStart` per `Headless.Hosting`), then `services._AddIdempotencyCore()`.
- No parameterless overload (R17).
- `IdempotencyOptionsDIValidator` is a second `IValidateOptions<IdempotencyOptions>` implementation that runs after the FluentValidation validator. It takes `IServiceProvider` and checks: if `options.InFlightStrategy == WaitAndReplay`, verify `serviceProvider.GetService<IDistributedLockProvider>() != null`. Fails with a clear `ValidateOptionsResult.Fail("...")` otherwise.
- After `Configure<TOption, TValidator>` wires FluentValidation + `ValidateOnStart`, the DI-aware validator is picked up alongside via `TryAddEnumerable`. Both run at host build per `Headless.Hosting`'s `ValidateFluentValidation().ValidateOnStart()` chain.

*`UseIdempotency` (R18):*
- `[PublicAPI] public static IApplicationBuilder UseIdempotency(this IApplicationBuilder app) => app.UseMiddleware<IdempotencyMiddleware>();`.
- Namespace: `Microsoft.AspNetCore.Builder` (with the IDE0130 pragma per #279 pattern).
- XML doc on the method MUST include the canonical pipeline-order note: *"Place `UseIdempotency()` after `UseAuthorization()` and after `UseHeadlessTenancy()`. The middleware reads `ICurrentTenant.Id` for cache-key composition; tenant + auth must be resolved first so unauthenticated and unauthorized requests don't allocate idempotency storage."*

*Default delegate wiring:*
- In `_AddIdempotencyCore`, register a post-configure that fills in `ShouldCacheResponse` if the consumer left it null:
  ```csharp
  services.PostConfigure<IdempotencyOptions>(o => o.ShouldCacheResponse ??= DefaultCachePredicate.Instance);
  ```
  (`KeyDeriver`, `RequestFingerprint`, `ShouldApply` stay null because the middleware's "null means default" branch in U6/U9 already handles them.)

**Patterns to follow:**
- `src/Headless.Caching.Redis/Setup.cs:11-73` (canonical three-overload + `_Core` helper template).
- `src/Headless.Hosting/Options/OptionsServiceCollectionExtensions.cs:143-213` (`Configure<TOption, TValidator>` overload signatures).
- `src/Headless.Api.Core/MultiTenancy/HeadlessApiTenancyBuilder*.cs` (DI-aware validator pattern; if no precedent exists, the .NET docs `IValidateOptions<TOptions>` interface is the canonical reference).

**Test scenarios:**

*`AddIdempotency` overloads:*
- Happy path: `services.AddIdempotency(config)` reads `IConfiguration` section into `IdempotencyOptions`, registers middleware, validator, post-configure.
- Happy path: `services.AddIdempotency(o => { ... })` invokes the `Action<TOption>` on the bound options.
- Happy path: `services.AddIdempotency((o, sp) => { ... })` invokes with the service provider.
- Edge: `services.AddIdempotency(setupAction: null)` — should NOT compile (non-nullable parameter per R17).
- Edge: calling `AddIdempotency` twice — second call overrides first (consistent with `services.Configure<T>` semantics; not idempotent at the level of merged options).
- DI: after registration, `serviceProvider.GetRequiredService<IdempotencyMiddleware>()` resolves with all dependencies satisfied.
- DI: `ShouldCacheResponse` post-configure runs — if consumer didn't set it, it defaults to `DefaultCachePredicate.Instance`; if consumer set it, post-configure leaves it alone (the `??=` is the right operator).

*Startup validation:*
- Happy path: `InFlightStrategy = Reject` + no `IDistributedLockProvider` registered → host builds successfully (no validation failure).
- Edge: `InFlightStrategy = WaitAndReplay` + `IDistributedLockProvider` registered → host builds successfully.
- Error path: `InFlightStrategy = WaitAndReplay` + no `IDistributedLockProvider` registered → host build throws `OptionsValidationException` with a clear message naming the missing service and the option that triggers the requirement.
- Integration: full `WebApplicationFactory` startup with the misconfiguration → exception surfaces at app start, NOT first request. *Verifies brainstorm R18 fast-fail timing.*

*`UseIdempotency`:*
- Happy path: `app.UseIdempotency()` resolves the scoped middleware on each request.
- Edge: calling `UseIdempotency` without `AddIdempotency` → fails at middleware resolution time (the framework's standard error).

**Verification:** Unit + integration tests pass. Host-builder validation test confirms misconfiguration is caught at startup.

---

### U11. Delete legacy types from `Headless.Api.Core`

**Goal:** Greenfield cleanup. Remove the old guard-style middleware, options, registration extension, and tests.

**Requirements:** R0, R21.

**Dependencies:** U10 (new package must be fully functional before deletion).

**Files:**
- `src/Headless.Api.Core/Middlewares/IdempotencyMiddleware.cs` (delete entire file)
- `src/Headless.Api.Core/SetupMiddlewares.cs` (edit — remove lines 14–33, the two `AddIdempotencyMiddleware` overloads)
- `src/Headless.Api.Core/README.md` (edit — remove the idempotency Key-Features bullet at line 28 and Quick-Reference example at line 44)
- `tests/Headless.Api.Tests.Unit/Middlewares/IdempotencyMiddlewareTests.cs` (delete entire file)

**Approach:**
- Verify no other file in `src/Headless.Api.Core/` imports `IdempotencyMiddleware`, `IdempotencyMiddlewareOptions`, or `IdempotencyMiddlewareOptionsValidator`. `grep -r "IdempotencyMiddleware" src/Headless.Api.Core/` before deletion.
- Verify no `using` statements in other test files reference the deleted unit-test file's helpers.
- Verify the demo project doesn't wire `AddIdempotencyMiddleware` — research found zero hits in `demo/`, but re-confirm.
- README edits: remove the bullet and the registration example. The Idempotency section is mentioned in two specific lines (per repo-research-analyst output).
- The `Headless.Api.Core.csproj` does NOT currently reference `Headless.DistributedLocks.Abstractions` — no ProjectReference cleanup needed.

**Patterns to follow:**
- The greenfield-clean-delete posture is documented at `docs/solutions/messaging/transport-wrapper-drift-and-doc-sync.md` (no `[Obsolete]` cycle).
- The #279 plan at `docs/plans/2026-05-18-001-refactor-tenancy-http-authorization-requirement-plan.md:310` is the precedent for "delete the file outright" in this repo.

**Test scenarios:**
Test expectation: none -- deletion only. Verification is `dotnet build headless-framework.slnx` succeeds and `dotnet test` on the test projects shows the deleted file's tests are gone (count decreases by 4).

**Verification:** `grep -r "IdempotencyMiddleware\|AddIdempotencyMiddleware\|IdempotencyMiddlewareOptions" src/` returns ONLY hits inside `src/Headless.Api.Idempotency/`. Build + test pass.

---

### U12. Integration tests — `Headless.Api.Idempotency.Tests.Integration` end-to-end coverage

**Goal:** Wire `WebApplicationFactory` with a minimal test app and cover every acceptance example end-to-end against in-memory `ICache`. Cross-provider (Redis) Testcontainers coverage for the marker→record transition and WaitAndReplay flow.

**Requirements:** Success Criteria (all five branches + tenant isolation).

**Dependencies:** U10, U11.

**Files:**
- `tests/Headless.Api.Idempotency.Tests.Integration/Headless.Api.Idempotency.Tests.Integration.csproj` (already created in U1; add necessary PackageReferences if any are still missing)
- `tests/Headless.Api.Idempotency.Tests.Integration/TestAppFactory.cs` (new — `WebApplicationFactory<TestStartup>` subclass)
- `tests/Headless.Api.Idempotency.Tests.Integration/TestStartup.cs` (new — minimal app with one POST endpoint, one PUT endpoint, one endpoint with `.WithIdempotency(...)` for AE12)
- `tests/Headless.Api.Idempotency.Tests.Integration/HappyPathTests.cs` (new — AE1, AE7, AE8)
- `tests/Headless.Api.Idempotency.Tests.Integration/MismatchTests.cs` (new — AE2)
- `tests/Headless.Api.Idempotency.Tests.Integration/InFlightTests.cs` (new — AE3, AE4)
- `tests/Headless.Api.Idempotency.Tests.Integration/StatusPredicateTests.cs` (new — AE5)
- `tests/Headless.Api.Idempotency.Tests.Integration/OversizeTests.cs` (new — AE6)
- `tests/Headless.Api.Idempotency.Tests.Integration/HeaderAllowlistTests.cs` (new — AE9)
- `tests/Headless.Api.Idempotency.Tests.Integration/PerEndpointMetadataTests.cs` (new — AE12)
- `tests/Headless.Api.Idempotency.Tests.Integration/CustomHookTests.cs` (new — AE13, AE14)
- `tests/Headless.Api.Idempotency.Tests.Integration/RedisBackendTests.cs` (new — Testcontainers Redis; same scenarios as InFlight + Happy path)

**Approach:**
- `TestStartup` wires: `services.AddHeadless...()`, `services.AddInMemoryCache()`, `services.AddIdempotency(o => { o.IdempotencyKeyExpiration = ...; })`, `app.UseHeadlessTenancy()`, `app.UseAuthorization()`, `app.UseIdempotency()`, then maps three endpoints:
  - `POST /disbursements` — returns 201 with `Location` header, accepts a JSON body.
  - `POST /errors` — returns whichever status code the test requests (via a query param), for AE5 coverage.
  - `POST /webhook-receiver` — mapped `.WithIdempotency(o => o.IdempotencyKeyExpiration = 7.Days())`, for AE12.
- Tenant context: provide a `TestTenantAccessor` that reads tenant from a custom `X-Test-Tenant` header so AE7/AE8 can switch tenants per request.
- For AE3/AE4 (concurrency), use `Task.WhenAll(2-3 concurrent requests)` and verify exactly one handler invocation via a counter wired into the endpoint. The "winner returns first; losers see in-flight or replay" assertion is timing-sensitive — use `IServerSemaphore`-style synchronization in the test endpoint (the endpoint blocks on a `TaskCompletionSource` controlled by the test) to make races deterministic.
- For AE9, the test endpoint sets `Response.Headers.Append("Set-Cookie", "session=abc; HttpOnly")` and `Response.Headers["traceparent"] = "..."`; the retry response must NOT contain either.
- Testcontainers Redis backend tests (`RedisBackendTests.cs`) tagged with the framework's `[Trait("Category", "Docker")]` (verify exact trait name in the existing test projects) — skipped when Docker unavailable.

**Patterns to follow:**
- `tests/Headless.Api.Tests.Integration/ProblemDetailsTests.cs` (WebApplicationFactory + assertions on problem-details JSON).
- `tests/Headless.Api.Tests.Integration/TenantResolutionMiddlewareTests.cs` (custom-header-driven tenant injection).
- `tests/Headless.Caching.Redis.Tests.Integration/` (Testcontainers Redis trait pattern — verify name during implementation).

**Test scenarios:**

*HappyPathTests:*
- Happy path: AE1 — POST disbursement, second identical request → 201 + same Location + same body + `Idempotent-Replayed: true`.
- Edge: AE7 — same key + body across two tenants → both succeed, two distinct cache entries.
- Edge: AE8 — same key + body, first request has no tenant, second has tenant `T1` → no collision.

*MismatchTests:*
- Happy path: AE2 — same key, different body → 422 with `g:idempotency-key-reused`, original record unchanged. Verify original retry still succeeds with `Idempotent-Replayed: true`.

*InFlightTests (deterministic with semaphore):*
- Happy path: AE3 — two concurrent requests with same key + body under `Reject` → one returns 201, one returns 409. After winner completes, third request returns the cached 201 with `Idempotent-Replayed: true`.
- Happy path: AE4 — two concurrent requests under `WaitAndReplay` → loser blocks, waits, replays after winner finishes.
- Edge: AE4 timeout — set `InFlightLockTimeout = 100ms`; winner takes 5s; loser returns 409 `g:idempotency-in-flight-timeout`.

*StatusPredicateTests:*
- Happy path: AE5 503 — first request returns 503, retry executes handler fresh.
- Happy path: AE5 422 — first request returns 422, retry returns cached 422 with `Idempotent-Replayed: true`.
- Edge: 405 cached; 411 cached; 429 NOT cached; 408 NOT cached.

*OversizeTests:*
- Happy path: AE6 Reject — 2 MiB body + 1 MiB cap → 413.
- Happy path: AE6 PassThrough — 2 MiB body + 1 MiB cap + `OversizeBehavior.PassThrough` → handler runs, response has no `Idempotent-Replayed: true`, retry runs handler again (not cached).

*HeaderAllowlistTests:*
- Happy path: AE9 — response has `Set-Cookie` and `traceparent`; retry response has neither but does have `Content-Type` and `Idempotent-Replayed`.

*PerEndpointMetadataTests:*
- Happy path: AE12 — `/webhook-receiver` records with 7-day TTL; `/disbursements` with 24h TTL. Verify by reading the cache's TTL directly (in-memory cache exposes this in tests).

*CustomHookTests:*
- Happy path: AE13 — supply canonical-JSON `RequestFingerprint`; two requests with reordered JSON keys + drifted client timestamp → second replays first.
- Happy path: AE14 — supply per-user `KeyDeriver`; two users in tenant `T1` with same key + body → two distinct cache entries, no cross-replay.

*RedisBackendTests (Testcontainers Redis):*
- Happy path: AE1 against Redis backend.
- AE3/AE4 against Redis backend (concurrency semantics across processes — though `WebApplicationFactory` is single-process, the test confirms Redis's `TryInsertAsync` is atomic).
- Marker→record finalize against Redis: the TryInsertAsync → Upsert sequence on Redis produces a single key transitioning from `InFlight` to `Complete` (verify via direct Redis-key inspection in the test).

**Verification:** All AEs pass end-to-end with the in-memory cache. Redis-backed tests pass when Docker is available; skipped cleanly otherwise. The success criterion "all five branches (miss, hit-match, hit-mismatch, in-flight, oversize) plus tenant isolation" is verified.

---

### U13. Documentation — `docs/llms/api.md` amendments, create `docs/llms/mediator.md`, package READMEs

**Goal:** Update LLM-facing docs and per-package READMEs so the canonical pipeline order, the new package, and the Mediator-boundary doctrine are discoverable.

**Requirements:** R19, R20.

**Dependencies:** U12 (tests confirm behavior before documentation describes it).

**Files:**
- `docs/llms/api.md` (edit — three locations: line 92 area additional-packages list, line 100 area pipeline-order guidance, line 196 area consumer-owned middleware prose)
- `docs/llms/mediator.md` (new — the file does not exist; create it from scratch)
- `src/Headless.Api.Idempotency/README.md` (final pass over the U1 skeleton — Problem Solved, Key Features, Installation, Quick Start, Configuration, Dependencies, Side Effects sections per the `Headless.Api.FluentValidation/README.md` template)
- `src/Headless.Mediator/README.md` (edit — add a pointer to `docs/llms/mediator.md` for boundary doctrine)
- `src/Headless.Api.Core/README.md` (already edited in U11; if anything was missed about the new package's existence, add a "See `Headless.Api.Idempotency` for idempotency middleware" line in the Building Blocks section)

**Approach:**

*`docs/llms/api.md`:*
- Add `Headless.Api.Idempotency` to the additional-packages list around line 92.
- Extend the tenancy-pipeline guidance bullet around line 100 with a new sentence: "For idempotent-replay middleware, place `app.UseIdempotency()` after `UseAuthorization()` and after `UseHeadlessTenancy()`. Idempotency depends on tenant + auth being resolved so the cache key is tenant-scoped and unauthenticated/unauthorized requests do not allocate storage."
- Update the consumer-owned middleware prose (line 196 area) to list `UseIdempotency()`.

*`docs/llms/mediator.md` (new):*
- Header + intro: "Mediator boundary doctrine for AI agents."
- Section "What goes in the Mediator pipeline": validation behaviors, logging behaviors, response transforms — domain-level cross-cuts.
- Section "What does NOT go in the Mediator pipeline": auth, tenancy, idempotency — boundary concerns. Each has a one-paragraph rationale:
  - **Auth (`AuthBehavior` removed)**: enforcement belongs at the auth middleware boundary.
  - **Tenancy (`TenantRequiredBehavior` removed by #279)**: same boundary argument. Cite `src/Headless.Api.Core/MultiTenancy/RequireTenantAttribute.cs` and the `[RequireTenant]` attribute as the replacement surface.
  - **Idempotency (`IdempotencyBehavior<,>` never added)**: four structural defects:
    1. Fires post-model-bind — abusive payloads fully deserialized before dedupe.
    2. Fires on every internal `Send()` — false positives on handler-to-handler composition (F10).
    3. Cannot cache HTTP status/headers/byte body — only the typed CQRS response.
    4. Requires `IHttpContextAccessor` — defeats the abstraction layer.
  - Reference: `src/Headless.Api.Idempotency/README.md` for the boundary-side replacement.
- Section "Register-time API": pointer to `src/Headless.Mediator/README.md`.

*`src/Headless.Api.Idempotency/README.md` (final):*
- Headers per `Headless.Api.FluentValidation/README.md` template.
- Problem Solved: one-paragraph framing of the guard→replay rewrite.
- Key Features: replay semantics, in-flight strategies, oversize handling, custom hooks, per-endpoint metadata.
- Installation: `dotnet add package Headless.Api.Idempotency`.
- Quick Start: 10-line `Program.cs` snippet with `services.AddIdempotency(o => { ... })` and `app.UseIdempotency()`.
- Configuration: table of all `IdempotencyOptions` properties with defaults.
- Dependencies: list of ProjectReferences from U1.
- Side Effects: pipeline-order requirements (after auth, after tenancy); response header (`Idempotent-Replayed`); cache writes.
- Boundary note: "For why this is NOT a Mediator behavior, see `docs/llms/mediator.md`."

*`src/Headless.Mediator/README.md`:*
- Add a "Boundary doctrine" subsection (1 paragraph) that points to `docs/llms/mediator.md` and summarizes the no-auth / no-tenancy / no-idempotency rejection.

**Patterns to follow:**
- `src/Headless.Api.FluentValidation/README.md` (package README template).
- `docs/llms/api.md` current structure.
- `docs/plans/2026-05-18-001-refactor-tenancy-http-authorization-requirement-plan.md:20` (four-defect prose template — extended for idempotency).

**Test scenarios:**
Test expectation: none -- documentation only. Verification is markdown lint clean + manual readthrough.

**Verification:**
- `grep -r "Idempotent" docs/llms/` returns hits in both `api.md` and `mediator.md`.
- `docs/llms/mediator.md` exists and renders cleanly.
- Package README has all eight sections.
- A run of any markdown-linter the repo uses (verify via `dotnet-tools.json` and CI config) passes.

---

## Scope Boundaries

### Deferred for later (from origin)

The brainstorm explicitly deferred these; this plan does NOT pull them in.

- `IIdempotencyStore` abstraction over `ICache`. Defer until a second backend is needed.
- `Headless.Api.Idempotency.EntityFramework` package. Defer until a consumer needs durability past cache eviction (regulated audit retention, etc.).
- Custom problem-details factories for the four idempotency-specific responses (consumers use the existing `IProblemDetailsCreator` extension point).
- Streaming body hash without buffering. Defer until the 1 MiB cap becomes a real constraint.
- Roslyn analyzer warning on consumer-registered idempotency-like Mediator behaviors. Doc-only rejection in v1.
- `Idempotent-Replayed` header configurability. Always-on in v1.
- HTTP/2 / gRPC trailer replay. v1 captures status + allowlisted headers + body bytes only.
- Body compression awareness. v1 includes `Content-Encoding` in the default allowlist; consumers wiring custom pre-write compression preserve it via the allowlist.

### Outside this RFC (from origin)

- Broader framework-wide options-shape audits beyond `IdempotencyKeyExpiration`'s `TimeSpan? → TimeSpan` change.
- The #279 tenant-resolution middleware order changes (owned by #279; this plan inherits whatever lands).

### Deferred to Follow-Up Work

Plan-local sequencing — work that surfaces from research but is not in this plan's commit set.

- **Arabic resource translations.** U3 ships English baseline; Arabic file shipped with placeholders and a follow-up todo opened in `docs/todos/`. Owner: i18n review pass.
- **Migration of `GeneralMessageDescriber` snake_case codes to kebab-case.** Not in scope here; this RFC introduces kebab-case for new descriptors only. A future cleanup may align the existing legacy codes (`g:duplicated_request` etc.) — but that's a separate plan with deprecation considerations for downstream consumers.
- **Demo project showcase of `UseIdempotency()`.** The repo's `Headless.Api.Demo` does not currently wire idempotency; adding a demo endpoint would aid discoverability. Defer to a follow-up demo-pass commit.
- **`docs/solutions/api/` write-up** capturing this RFC's learnings (typed-factory ProblemDetails lane, marker→record TTL contract, four-defect Mediator argument). Per `docs/solutions/` convention — write after implementation lands.

---

## Risk Analysis & Mitigation

| # | Risk | Likelihood | Impact | Mitigation |
| --- | --- | --- | --- | --- |
| 1 | Cross-provider serialization of `IdempotencyRecord` fails (e.g., Redis MessagePack quirk with `Dictionary<string, string[]>`) | Medium | High — replay path broken on Redis | U5 ships a cross-provider serialization round-trip test against Memory, Redis (Testcontainers), and Hybrid before downstream units depend on it |
| 2 | `IDistributedLockProvider` waiter-starvation under high `WaitAndReplay` contention (TODO 005) | Low (depends on backend choice) | Medium — increased timeouts under load | Document the operational caveat in the package README; recommend Redis-backed lock for multi-instance APIs; the existing TODO already tracks the fix |
| 3 | Marker→record TTL race: marker TTL'd-out before finalize Upsert; concurrent request inserts fresh marker; Upsert overwrites it | Low | Medium — wrong fingerprint stored against new marker | Marker carries fingerprint; finalize Upsert checks marker fingerprint matches before overwrite. Use `TryReplaceAsync` (CAS-like) for the finalize step where the provider supports it; otherwise accept the race and document. See `docs/solutions/logic-errors/terminal-state-overwrite-on-redelivery-2026-05-16.md` for the invariant. |
| 4 | `CaptureStream` truncates a legitimately-large response and the consumer is unaware | Low | Medium — silent storage gap | `CaptureStream.TruncatedCapture` flag triggers a Warning log + skips the Upsert; future retry re-executes. Cap is `MaxBodySizeForHashing × 2`; revisit if real responses exceed |
| 5 | Header allowlist drift: framework adds a new sensitive header (e.g., `Vary` variants); consumers don't update allowlist; sensitive content leaks via replay | Low | Medium — privacy/security | Allowlist is conservative by default. Document the model: "if you don't see the header in the allowlist, it isn't replayed." Headers added to the framework that imply security implications get a separate review |
| 6 | `RequestFingerprint` delegate reads body unbufferred and inner handler can't re-read | Low (covered by R24 contract) | High — handler breaks | Middleware unconditionally calls `EnableBuffering()` before delegate invoke + unconditionally rewinds on return. U9 test asserts the rewind. XML doc on `IdempotencyOptions.RequestFingerprint` makes the contract explicit |
| 7 | `KeyDeriver` consumer accidentally drops tenant from key, causing cross-tenant cache collision | Low | High — privacy/security | Document loudly that `KeyDeriver` REPLACES the default `(tenant, method, path, key)` composition; consumer owns tenant inclusion. README example shows correct shape |
| 8 | New `Headless.Api.Idempotency` package adds `Headless.DistributedLocks.Abstractions` ProjectReference even for consumers using `Reject` strategy (transitive bloat) | Low | Low — one extra abstraction package | Unconditional reference is acceptable; the package is light. Brainstorm Dependencies/Assumptions confirms this is the chosen tradeoff |
| 9 | Pipeline-order misconfiguration: consumer places `UseIdempotency()` before `UseHeadlessTenancy()` → `ICurrentTenant.Id` is null → cache keys lose tenant scope silently | Medium | High — cross-tenant leak | Document order in XML doc, README, `docs/llms/api.md`. Consider a runtime assertion in the middleware: if `ICurrentTenant.IsAvailable == false` AND the registered `ICurrentTenant` is NOT `NullCurrentTenant` (consumer wired tenant but middleware order is wrong), log Warning. Defer the assertion to v1.1 if it complicates v1 |
| 10 | Endpoint-metadata override's delegate captures wrong-scope service → `ObjectDisposedException` at runtime | Low | Medium — runtime failure on the override-bearing endpoint | Documented constraint per Key Technical Decision 8. Roslyn analyzer deferred |
| 11 | Test-time deterministic concurrency for AE3/AE4 is timing-sensitive and flaky | Medium | Medium — CI noise | Use `TaskCompletionSource` controlled by the test inside the endpoint handler to gate winner completion; assertions verify counts not timing. Pattern stable in `tests/Headless.Api.Tests.Integration/`-style suites |
| 12 | Greenfield deletion breaks an undocumented downstream consumer (e.g., a sample app outside the repo references `AddIdempotencyMiddleware`) | Low | Low (per project memory) | Release notes call out R0 + R21 breaking change. zad-ngo migration is the only known consumer per brainstorm |

---

## Documentation Plan

- **Code-side** (XML doc):
  - Every `[PublicAPI]` type and method documented.
  - `UseIdempotency` XML doc carries the pipeline-order constraint.
  - `IdempotencyOptions.RequestFingerprint` carries the body-rewind contract.
  - `IdempotencyMetadata` carries the delegate-scope caveat.
  - `IdempotencyOptions.KeyDeriver` warns that consumer owns tenant inclusion.

- **Package README** (`src/Headless.Api.Idempotency/README.md`): full final pass in U13 per the `Headless.Api.FluentValidation` template.

- **LLM docs**:
  - `docs/llms/api.md`: amend in U13 (additional-packages list, pipeline-order guidance, consumer-middleware prose).
  - `docs/llms/mediator.md`: CREATED in U13 with the boundary doctrine + four-defect idempotency rationale + #279 tenancy guidance + register-time-API pointer.

- **Existing READMEs**:
  - `src/Headless.Api.Core/README.md`: remove idempotency references in U11.
  - `src/Headless.Mediator/README.md`: add boundary-doctrine pointer in U13.

- **Release notes** (out-of-scope for this plan but called out for the implementer):
  - R0 + R21 breaking surface change: `Headless.Api.Core` no longer ships idempotency middleware; consumers add `Headless.Api.Idempotency` package and call `AddIdempotency` / `UseIdempotency`.
  - R15 default TTL change: 1h → 24h.
  - R7 default mismatch status code: was N/A (guard was 409); now 422 by default with `MismatchStatusCode` escape hatch.

---

## Dependencies / Prerequisites

- **#279 tenancy refactor** is referenced by R19's canonical pipeline order. Plan assumes #279 merges before this plan's documentation lands. If #279 is still in flight, U13 documentation can land first with a forward-reference; the code changes do not depend on #279.
- **`Headless.Hosting`** `services.Configure<TOption, TValidator>(...)` API (already exists at `src/Headless.Hosting/Options/OptionsServiceCollectionExtensions.cs:143`).
- **`Headless.Caching.Abstractions`** `ICache.TryInsertAsync`, `GetAsync`, `UpsertAsync`, `RemoveAsync` (already exist).
- **`Headless.DistributedLocks.Abstractions`** `IDistributedLockProvider.TryAcquireAsync` returning `IDistributedLock?` (already exists).
- **`Headless.Core`** `ICurrentTenant.Id` (already exists; namespace `Headless.Abstractions`).
- **`Headless.Api.Abstractions`** `IProblemDetailsCreator.Normalize`, `Conflict`, `UnprocessableEntity` (already exist).
- **`Headless.Extensions`** `HttpHeaderNames.IdempotencyKey` constant (already exists; U4 adds `IdempotentReplayed` alongside).
- **`Headless.Testing`** test harness primitives (`TestBase`, `FakeTimeProvider` integration). Already used by existing tests.
- **Docker** (optional) for the Testcontainers Redis integration tests in U12. Tests skip when Docker unavailable.
- No new third-party PackageReferences across the framework.

---

## Alternative Approaches Considered

1. **Keep `IdempotencyMiddleware` inside `Headless.Api.Core`; do the replay rewrite in place without package extraction.** Cost saved: one new project + NuGet metadata. Cost paid: `Headless.Api.Core` continues to grow toward a kitchen sink; every consumer pulls body-buffering code + (transitive) `Headless.DistributedLocks.Abstractions`. **Rejected** because the brainstorm's "framework discipline" decision (Key Decisions section) is right — extraction matches `Headless.Api.FluentValidation` / `Headless.Api.DataProtection` precedent and leaves room for the future EF sibling.

2. **Introduce `IIdempotencyStore` abstraction over `ICache` now (in this RFC) to leave room for the EF backend.** Cost: extra type + DI wiring + adapter from `ICache` to `IIdempotencyStore`. Cost paid by all consumers, benefit only to a hypothetical future EF user. **Rejected** per brainstorm Key Decisions — the abstraction adds an indirection layer without a concrete second consumer. Extracting `IIdempotencyStore` later from this dedicated package is a non-breaking addition.

3. **Use ASP.NET's `IOutputCacheStore` + `OutputCacheAttribute` instead of building a middleware.** OutputCache has a similar shape (response capture + key derivation + replay). **Rejected** because OutputCache's intent is response caching for GET-style read-throughs; idempotency is for POST/PUT/PATCH/DELETE retry semantics. The contracts diverge on: header allowlist (OutputCache copies more by default), in-flight handling (OutputCache doesn't have an in-flight strategy), fingerprinting (OutputCache uses headers + query, not body hashing), and TTL (OutputCache TTL is short by default).

4. **Wait-and-replay using `IBackgroundJobClient` / polling instead of `IDistributedLockProvider`.** Could avoid the distributed-lock dependency. **Rejected** because polling adds latency and the distributed-lock abstraction is already in the framework. The lock approach is also the velmie/idempo Go choice.

5. **Add `IdempotencyBehavior<TRequest, TResponse>` to `Headless.Mediator` as an opt-in alternative for non-HTTP scenarios.** **Rejected** per brainstorm R20 + #279 precedent. The four structural defects are not mitigatable without HTTP context; non-HTTP scenarios (background workers, etc.) need a different contract entirely (job-level idempotency keys, not HTTP idempotency keys).

---

## Success Metrics

Per the brainstorm's Success Criteria, verified at the end of U12 + U13:

- **The lost-response retry returns the original response.** Verified by U12 `HappyPathTests.AE1` end-to-end test using `WebApplicationFactory` + in-memory `ICache`.
- **One-call consumer setup.** `services.AddIdempotency(o => { ... })` + `app.UseIdempotency()` in `Program.cs`. No per-endpoint configuration required for default contract. Verified by U12 `TestStartup`.
- **Per-endpoint override expressible locally.** `.WithIdempotency(o => o.IdempotencyKeyExpiration = 7.Days())` on a single endpoint. Verified by U12 `PerEndpointMetadataTests.AE12`.
- **Replay observable from response.** `Idempotent-Replayed: true` header on every replay. Verified across all U12 happy-path tests.
- **All five branches + tenant isolation covered.** U12 integration suite enumerates each: miss (HappyPath), hit-match (HappyPath), hit-mismatch (Mismatch), in-flight (InFlight), oversize (Oversize), tenant isolation (HappyPath AE7/AE8).
- **Middleware code ≤ ~250 lines.** Verified by line count of `src/Headless.Api.Idempotency/IdempotencyMiddleware.cs` at U10 completion. Reuses `ICache`, `IClock`, `ICurrentTenant`, `IDistributedLockProvider` without new abstractions.
- **Canonical middleware order documented.** `docs/llms/api.md` amendment in U13.
- **`IdempotencyBehavior<,>` non-addition documented.** `docs/llms/mediator.md` created in U13 with four-defect rationale.
- **Coverage targets met.** Per CLAUDE.md: line ≥85%, branch ≥80%, mutation ≥70%. Verified at U12 completion via `dotnet test --collect:"XPlat Code Coverage"` (or the project's preferred coverage tool from `dotnet-tools.json`).

---

## Operational / Rollout Notes

- **Greenfield posture; no per-instance rollout.** The change ships as a NuGet release with the breaking semantic + project-reference change in the release notes.
- **No data migration.** `ICache` entries from the old guard-style middleware (cache key `idempotency_key:{key}`, no method/path/tenant scoping) and the new replay-style middleware (cache key `idem:{tenant}:{method}:{path}:{key}`) live in disjoint key spaces. Old entries TTL out naturally; no manual eviction needed.
- **Monitoring** — operators should add:
  - Counter on response header `Idempotent-Replayed: true` presence (replay rate).
  - Counter on 422 responses with type `g:idempotency-key-reused` (client-side bugs).
  - Counter on 409 responses with type `g:idempotency-in-flight` / `g:idempotency-in-flight-timeout` (contention).
  - Counter on 413 responses with type `g:idempotency-body-too-large` (sizing issues).
  - Log search on `IdempotencyMiddleware` `Warning` severity for `TruncatedCapture` events.
- **Reversibility** — consumers can disable per-app via `options.ShouldApply = _ => false` (R21 escape hatch) without reverting the package reference.
- **Pipeline order verification** — recommend a one-shot startup smoke check (manual, not coded): hit a tenanted endpoint with a key, observe the cache for the new key shape `idem:{tenant}:...`. Mis-ordered middleware produces `idem::...` (missing tenant) — a recognizable failure mode.

---

## Outstanding Questions

All "Resolve Before Planning" questions from the brainstorm were resolved on 2026-05-19. All eight "Deferred to Planning" questions are settled in Key Technical Decisions above. The following are implementation-time refinements that this plan deliberately defers to the implementer's judgment during U5–U10:

- **Exact `TryReplaceAsync` vs `UpsertAsync` choice for marker → complete finalize.** `TryReplaceAsync` gives CAS-like protection (decision 4 + risk 3); `UpsertAsync` is simpler. Pick during U6 / U7 based on actual `ICache` provider semantics across Memory and Redis; document the choice in the code comment with the rationale. Both are correct given the marker's fingerprint discriminator.

- **Exact upper bound on `InFlightLockTimeout` validation.** Plan suggests 5 minutes (decision 2 validator). Implementer can adjust if a real consumer needs longer (e.g., long-running webhook handlers).

- **Whether to log full fingerprint hex or only an 8-byte prefix on mismatch.** Plan defaults to 8-byte prefix (security: avoid Body-content disclosure via log channel). Implementer confirms or extends to 16 bytes after seeing operator feedback.

- **Whether `IdempotencyMessageDescriber` exposes the four codes as public or internal.** Plan defaults `internal static` (the codes are surfaced through ProblemDetails responses; consumers don't construct them directly). If a downstream consumer needs to assert against the codes in their own integration tests, expose as `public`.

- **Whether to add a metric counter / OpenTelemetry instrumentation in v1.** Plan defers to operational tooling (Operational Notes section). Add only if the framework already has a metrics convention to follow; otherwise defer to v1.1.

These are tactical implementation choices that don't materially change the plan structure or downstream-unit dependencies. Each is small enough to land in a follow-up commit if disagreement surfaces during code review.
