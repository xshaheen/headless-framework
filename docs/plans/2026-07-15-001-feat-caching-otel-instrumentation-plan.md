---
title: Caching OpenTelemetry Instrumentation - Plan
type: feat
date: 2026-07-15
topic: caching-otel-instrumentation
artifact_contract: x-unified-plan/v1
artifact_readiness: requirements-only
product_contract_source: x-brainstorm
execution: code
---

# Caching OpenTelemetry Instrumentation - Plan

## Goal Capsule

- **Objective:** Add OpenTelemetry `Meter` metrics and `ActivitySource` traces across the `Headless.Caching.*` providers, covering the cache-semantic events the framework already logs (hit/miss/stale, set, evict, factory outcome, fail-safe activation, eager/background refresh, invalidation), and establish the framework-wide OTel registration convention using caching as the reference implementation. Closes issue #384.
- **Product authority:** Repo owner (Mahmoud Shaheen). Instrument set, placement, privacy defaults, and the framework-wide native-emission theme decided and verified against the OpenTelemetry spec; captured in [docs/solutions/conventions/opentelemetry-instrumentation-conventions.md](../solutions/conventions/opentelemetry-instrumentation-conventions.md).
- **Open blockers:** None. Placement, instrument set, naming, and privacy are settled; remaining forks are implementation details deferred to planning.

## Product Contract

### Summary

Instrument the caching subsystem with native BCL `Meter` and `ActivitySource` emission, woven into the code paths that own each event: the `FactoryCacheCoordinator` (Core) for factory/fail-safe/refresh/timeout and get-or-add outcome, the Hybrid cache for L1/L2 tier attribution and invalidation propagation, and thin store-level counters/spans at each provider for direct operations. Consumers subscribe by a public instrumentation-name constant — no per-feature `.OpenTelemetry` satellite package. Metrics never carry the cache key; spans carry it only behind a default-off opt-in.

### Problem Frame

The caching subsystem is the framework's most behaviorally rich component — factory-backed reads with soft/hard/background timeouts, fail-safe stale serving, eager refresh, tag/generation invalidation, and two-tier hybrid composition. Today all of that is observable only through structured logs. An operator cannot answer "what is my L1 vs L2 hit ratio", "how often is fail-safe carrying me", "what is p95 factory latency", or "is eager refresh keeping entries warm" without scraping logs. Issue #384 (part of #369, matching the #287 quality bar set by distributed-locks) closes that gap. The events are already identified in code — the coordinator has structured-log call sites at exactly the points that need meters and spans — so the work is emitting telemetry at known hooks, not discovering where the events are.

### Key Decisions

- **K1. Instrument in-place at the event owner, by value.** Emit from `FactoryCacheCoordinator` (semantic events: factory outcome, fail-safe activation, eager/background refresh, timeout, get-or-add outcome), from `HybridCache` (L1/L2 tier attribution, invalidation publish/receive, recovery), and thin store-level counters/spans at `InMemoryCache` / `RedisCache` for direct operations. Rejected a single `ICache` decorator: it sits above Hybrid so it cannot attribute L1 vs L2 tier, and it sees only the final `CacheValue` of `GetOrAddAsync` — it cannot distinguish a fresh hit, stale-served hit, factory-computed value, or fail-safe activation, which is exactly the signal that matters. Rejected coordinator-only: it leaves direct-op traffic and evictions unmetered, failing the issue's "meters on all paths across providers" acceptance.

- **K2. One meter, focused instrument set, tier as a dimension.** A single `Headless.Caching` meter with ~8 instruments (see R-group Instruments), not FusionCache's four-meter / ~30-instrument split (which FusionCache itself defaults mostly off). Tier is a `headless.cache.tier` dimension (`l1`/`l2`/`hybrid`), not separate meters. Hit/miss/stale is one outcome-tagged counter, mirroring distributed-locks' `reason`-tagged single-counter shape rather than separate `hit`/`miss` instruments. Factory outcome is metered as success/error/timeout together so failure rate is computable from a real denominator.

- **K3. Native emission is the framework-wide model; typed registration helpers; no satellite packages.** Every feature Core emits `Activity`/`Meter` through BCL `System.Diagnostics` primitives, exposes a `public const string` instrumentation name, and ships typed `Add<Feature>Instrumentation()` extensions on `TracerProviderBuilder`/`MeterProviderBuilder` (thin `AddSource`/`AddMeter` wrappers). The OpenTelemetry SDK is never referenced by any Headless package; `OpenTelemetry.Api` (80 KB, zero transitive deps on net10) may be referenced by implementation packages where actually used — never by `*.Abstractions`. This matches the official .NET library-instrumentation guidance and the existing native subsystems (distributed-locks, jobs). Caching is built native-first; messaging's `DiagnosticSource`→span bridge is scheduled for migration to native under its own plan ([docs/plans/2026-07-15-002-refactor-messaging-native-otel-plan.md](2026-07-15-002-refactor-messaging-native-otel-plan.md)), out of scope here. Full rationale lives in [docs/solutions/conventions/opentelemetry-instrumentation-conventions.md](../solutions/conventions/opentelemetry-instrumentation-conventions.md).

- **K4. Key on spans opt-in (default off); key never on metrics.** `headless.cache.name` (low cardinality, non-sensitive) is always attached. The raw key appears on spans only behind a default-off `IncludeKeyInTraces` flag, because cache keys routinely carry tenant/user identifiers and PII cannot be un-leaked from a trace backend. The key is never a metric dimension, for cardinality and privacy. Stricter than FusionCache, which puts the key on every span with no redaction — the right default for a general-purpose framework.

- **K5. Bespoke `headless.cache.*` names — because no OTel cache convention exists.** Verified: OpenTelemetry has **no** cache semantic convention (only unadopted proposal semantic-conventions#1747; Redis-as-DB carries no hit/miss semantics), and none for locks or jobs. The framework rule is "follow an official semantic convention where one exists and fits (messaging → `messaging.*`), else bespoke `headless.<subsystem>.*`" — for caching, bespoke is the only correct option and is itself the OTel-recommended approach for non-standard telemetry. Both instrument names AND framework-owned attribute keys are namespaced (`headless.cache.outcome`, `headless.cache.tier`, `headless.cache.trigger`), not bare. Metric mechanics: lowercase dotted, `snake_case` segments, pluralize only countables, no `_total` suffix, units in instrument metadata (`unit: "ms"`) never in the name.

### Requirements

**Placement and coverage**

- R1. Semantic events — factory execution outcome, fail-safe activation, eager refresh, background completion, factory timeout, and get-or-add outcome (hit/miss/stale) — are emitted from `FactoryCacheCoordinator` at the existing structured-log call sites.
- R2. Direct `ICache` operations that bypass the coordinator (get, upsert, remove, increment, set primitives, prefix/tag/clear/flush) are metered at the provider or Hybrid layer so no traffic path is silent.
- R3. On the Hybrid cache, reads and writes are attributed to their tier (`l1`/`l2`/`hybrid`) and invalidation propagation (tag/clear/flush publish and receive) is metered.
- R4. Instrumentation adds no measurable overhead when no listener is attached, using the BCL `HasListeners()` / `Counter.Enabled` early-out already used by distributed-locks.

**Instruments**

- R5. All instruments register against one `Meter` named by the public instrumentation-name constant; all spans against one `ActivitySource` of the same name. The instrument set is the table below.
- R6. Every instrument carries a `headless.cache.name` dimension; framework-owned attribute keys are namespaced `headless.cache.*` (not bare), and the raw cache key is never an instrument dimension (K4, K5).
- R7. Metric and attribute names use the `headless.cache.*` scheme with the K5 mechanics (lowercase dotted, `snake_case`, no `_total`, units in metadata) and are documented in `docs/llms/caching.md` and the package README (the issue's "documented instrument names" acceptance).

| Instrument | Kind | Purpose | Key dimensions |
|---|---|---|---|
| `headless.cache.requests` | Counter | Read outcomes (covers hit/miss) | `headless.cache.operation`, `headless.cache.outcome` (hit/miss/stale), `headless.cache.tier` |
| `headless.cache.writes` | Counter | Set/upsert outcomes (covers set) | `headless.cache.operation`, `headless.cache.tier` |
| `headless.cache.evictions` | Counter | Entry evictions (covers evict) | `headless.cache.evict_reason` (expired/capacity/removed/flushed), `headless.cache.tier` |
| `headless.cache.factory.executions` | Counter | Factory runs; the failure-rate denominator | `headless.cache.outcome` (success/error/timeout) |
| `headless.cache.factory.duration` | Histogram | Factory execution latency (core SLO); `unit: ms` in metadata | `headless.cache.outcome` |
| `headless.cache.failsafe.activations` | Counter | Fail-safe stale serving (covers fail-safe-activation) | `headless.cache.trigger` (factory_error/factory_timeout/lock_acquire_failed) |
| `headless.cache.refreshes` | Counter | Eager + background refresh (covers refresh) | `headless.cache.refresh_kind` (eager/background), `headless.cache.outcome` (success/error) |
| `headless.cache.invalidations` | Counter | Hybrid tag/clear/flush propagation | `headless.cache.invalidation_kind` (tag/clear/flush), `headless.cache.direction` (publish/receive) |

**Traces**

- R8. `GetOrAddAsync` starts a `cache.get_or_add` span; factory execution and any distributed-lock acquire run as child spans (`cache.factory`, `cache.factory_lock`). On the Hybrid cache, L1/L2 store access appears as child spans tagged by tier.
- R9. A span whose operation fails in a caller-visible way (factory hard-timeout with no fail-safe fallback, propagated exception) sets `ActivityStatusCode.Error`; a fail-safe activation is recorded as a span event/attribute, not an error.
- R10. Spans carry `headless.cache.name` and `headless.cache.tier` always; the raw key only when `IncludeKeyInTraces` is enabled (K4).

**Registration and packaging**

- R11. The Meter and ActivitySource instances live in `Headless.Caching.Core` and emission uses BCL primitives only; the OpenTelemetry SDK is never referenced, and `OpenTelemetry.Api` appears in `Headless.Caching.Core` solely for the typed registration helper (K3) — never in `Headless.Caching.Abstractions`.
- R12. A `public static` type exposes the instrumentation name as a `public const string` so consumers reference the symbol, not a magic string (fixing the distributed-locks wart where the const is trapped inside an `internal` class). Meter/ActivitySource instances stay internal.
- R13. The `IncludeKeyInTraces` toggle is configured on the caching setup builder (`AddHeadlessCaching`), not at OTel-registration time.
- R14. `Headless.Caching.Core` ships typed `AddCachingInstrumentation()` extensions on `TracerProviderBuilder` and `MeterProviderBuilder` — thin wrappers over `AddSource`/`AddMeter` using the R12 const — so registration DX is uniform with the other subsystems; manual subscribe-by-name remains equally supported.

## Acceptance Examples

- AE1. Covers R1, R6. **Given** fail-safe is enabled and a stale reserve exists, **when** the factory throws, **then** `headless.cache.failsafe.activations{trigger=factory_error}` increments by 1, the caller receives the stale value, and no cache key appears on any metric dimension.
- AE2. Covers R3. **Given** a two-tier hybrid cache and a key present only in L2, **when** `GetOrAddAsync` is called, **then** `headless.cache.requests` records `{headless.cache.outcome=miss,headless.cache.tier=l1}` and `{headless.cache.outcome=hit,headless.cache.tier=l2}`, and the factory does not run.
- AE3. Covers R1, R8. **Given** a configured hard factory timeout and no fail-safe reserve, **when** the factory exceeds it, **then** `headless.cache.factory.executions{outcome=timeout}` increments, the `cache.get_or_add` span status is `Error`, and `CacheFactoryTimeoutException` propagates.
- AE4. Covers R4, R10. **Given** no metric/trace listener subscribed, **when** any cache operation runs, **then** no per-operation allocation attributable to instrumentation occurs; **and given** a trace listener with `IncludeKeyInTraces` off, **then** spans emit but no `cache.key` attribute is present.

## Scope Boundaries

- In scope: metrics + traces for the caching paths named above; the public name constant; the `IncludeKeyInTraces` toggle; docs for instrument names.
- Deferred to follow-up: serialize/deserialize error counters and CAS-write-lost counter (Redis) — low-value extensions to the same meter, addable non-breakingly.
- Outside this issue's identity: adopting the OTel `db.client.*` semantic conventions (K5); building a `Headless.Caching.OpenTelemetry` satellite package (K3 rejects satellite packages unconditionally — the typed helper lives in Core); the messaging bridge→native migration (approved, but tracked in its own plan — see Q2).

## Dependencies / Assumptions

- Depends on the distributed-locks OTel pattern as the template: `HeadlessDiagnostics.CreateMeter/CreateActivitySource`, source-generated `[Counter]`/`[Histogram]` instruments (`Microsoft.Extensions.Diagnostics.Metrics`), and the `Start(name, kind)` + `HasListeners` early-out helper.
- Assumes the coordinator can obtain the cache instance name (the `headless.cache.name` value) — it currently takes an `IFactoryCacheStore` with no name. Threading the named-instance identity to the coordinator's emit sites is a planning-level design task (a provider/store carrying its registered name, or the name passed through the coordinator call). Flagged for planning, not resolved here.
- Assumes evictions are only reliably observable on the in-memory tier; Redis server-side evictions are not client-observable and are documented as such rather than metered.

## Outstanding Questions

**Resolved** (recorded in [docs/solutions/conventions/opentelemetry-instrumentation-conventions.md](../solutions/conventions/opentelemetry-instrumentation-conventions.md))

- Q1. K3 (native emission + subscribe-by-name) is confirmed — verified as the official .NET library-instrumentation best practice.
- Q2. The messaging bridge→native migration is approved and tracked as its own plan ([docs/plans/2026-07-15-002-refactor-messaging-native-otel-plan.md](2026-07-15-002-refactor-messaging-native-otel-plan.md)); it stays out of scope for #384 so caching is not hostage to the harder refactor. Caching lands first as the native reference.

**Deferred to planning**

- Q3. Exact mechanism for threading the cache instance name (`headless.cache.name`) to coordinator emit sites (see Assumptions).
- Q4. Whether get-or-add hit/miss is counted once at the coordinator boundary or also mirrored at provider store reads without double-counting — resolve when the emit sites are laid out.

## Sources / Research

- Issue #384 (caching: OpenTelemetry meters + traces), part of #369, quality bar #287.
- Pattern to match: `src/Headless.DistributedLocks.Core/RegularLocks/DistributedLocksDiagnostics.cs`, `DistributedLockMetrics.cs`; central helper `HeadlessDiagnostics` (in `Headless.Constants`).
- Semantic hub and existing event/log sites: `src/Headless.Caching.Core/FactoryCacheCoordinator.cs` (+ `.EagerRefresh.cs`, `.BackgroundCompletion.cs`); contract surface `src/Headless.Caching.Abstractions/ICache.cs`.
- Multi-tier composition and invalidation: `src/Headless.Caching.Hybrid/` (`HybridCache.ReadOperations.cs`, `HybridCache.WriteOperations.cs`, `CacheInvalidationMessage.cs`, `HybridCacheRecoveryQueue.cs`).
- Two in-repo registration precedents: native (`Headless.DistributedLocks`, `Headless.Jobs` — subscribe by name, documented in `docs/llms/distributed-locks.md:212`, `docs/llms/jobs.md:729`) vs bridge (`src/Headless.Messaging.OpenTelemetry/Setup.cs`, `SetupMetrics.cs`).
- External reference: FusionCache (`~/Dev/oss/FusionCache`) — native BCL emission, four-meter split, `fusioncache.*` bespoke names, key on every span (no redaction), separate `.OpenTelemetry` registration package. Informed K2 (rejected its breadth) and K4 (rejected its unconditional key-on-span).
