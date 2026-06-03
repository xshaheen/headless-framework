---
date: 2026-06-02
topic: cache-entry-envelope-options
issue: 371
parent_issue: 369
---

# Cache entry-metadata envelope + CacheEntryOptions (abstractions + Memory)

## Summary

Introduce the metadata foundation for the `Headless.Caching.*` M1/M2 program (#369): a Memory-provider entry envelope that separates **logical expiration** (value is stale) from **physical expiration** (entry is evicted), reserves slots for a last-factory-error and tags, and a new `CacheEntryOptions` value type that replaces the bare `TimeSpan` on `GetOrAddAsync`. The change is behavior-preserving — with only a duration set, the cache behaves exactly as it does today; the new structure is dormant capacity that later issues activate.

## Problem Frame

Today the cache collapses "the value is stale" and "the entry is gone" into a single `ExpiresAt` instant on Memory's internal `CacheEntry`, and `GetOrAddAsync` accepts a bare `TimeSpan expiration`. That model has no room for the resilience features the roadmap commits to: fail-safe (serve a stale value when the factory fails) needs a value to survive *past* its staleness point; soft/hard factory timeouts and throttled retries need somewhere to record the last factory error; tagging needs a per-entry tag set. The roadmap's gating principle is "design the metadata model once; both M1 and M2 ride it" — so the cost of getting the envelope shape wrong is paid six times across #372–#380, either as inherited mistakes or repeated re-breaks.

This issue is the foundation slice: establish the shape now, in Memory, without shipping any new observable behavior. It is deliberately a technical/architectural brainstorm — the envelope and options shape *are* the subject.

## Key Decisions

- **Full internal envelope, lean public options.** The stored envelope materializes its complete field set now (logical expiration, physical expiration, last-factory-error, tags) as internal structural capacity, so the storage shape settles once. The public `CacheEntryOptions` exposes only `Duration`; each later knob (fail-safe, timeouts, refresh) is added to the public surface by the M1 PR that activates it. No public property silently does nothing.

- **Shared semantics, not a shared type.** There is no shared CLR envelope type across providers. The field set and its semantics are the contract; each provider models the fields natively (Memory keeps live object references; Redis #372 designs its own wire format). The cross-provider conformance harness is the drift guard. This keeps the caching provider assemblies decoupled and avoids introducing the domain's first `InternalsVisibleTo` wiring.

- **`CacheEntryOptions` scoped to `GetOrAddAsync`.** Only the factory-based read-through path takes options. The write family (`UpsertAsync`, `TryInsertAsync`, `TryReplaceAsync`, …) keeps `TimeSpan?` and stores envelopes where logical == physical. Rationale: fail-safe and timeouts are meaningful only when a factory exists; a plain write has nothing to fall back to.

- **Implicit `TimeSpan → CacheEntryOptions` conversion.** Existing `GetOrAddAsync(key, factory, TimeSpan.FromMinutes(5))` call sites compile unchanged; advanced callers construct `CacheEntryOptions` explicitly. This is the ergonomic precedent for the entire M1 options-bearing surface.

### Envelope shape: before / after

```mermaid
flowchart LR
  subgraph Before["CacheEntry — today"]
    B1["value"]
    B2["ExpiresAt  (stale == evicted)"]
    B3["Size / LastAccessTicks"]
  end
  subgraph After["CacheEntry — #371"]
    A1["value"]
    A2["LogicalExpiresAt   (value stale)"]
    A3["PhysicalExpiresAt  (entry evicted)"]
    A4["LastFactoryError?  (reserved, null)"]
    A5["Tags?              (reserved, empty)"]
    A6["Size / LastAccessTicks"]
  end
  Before --> After
```

For #371, `LogicalExpiresAt == PhysicalExpiresAt == now + Duration` on every write, and the reserved slots stay empty — which is why behavior is unchanged.

## Requirements

### Entry-metadata envelope (Memory)

- R1. Memory's stored entry separates a **logical expiration** (the instant the value becomes stale) from a **physical expiration** (the instant the entry is eligible for eviction/removal). The existing single-instant `ExpiresAt` concept is replaced by these two.
- R2. The envelope carries a reserved **last-factory-error** slot capable of holding error information plus its timestamp. In #371 this slot is always empty; it is populated starting with fail-safe (#373).
- R3. The envelope carries a reserved **tags** slot capable of holding a per-entry set of tag strings. In #371 this slot is always empty; it is populated starting with tagging (#378).
- R4. Eviction, LRU, size accounting, and `IsExpired`/read-miss decisions are driven by **physical** expiration in #371, preserving current eviction behavior. The logical expiration is recorded but not yet consulted by any read path.

### CacheEntryOptions surface

- R5. A new `CacheEntryOptions` type exposes a single active member: `Duration` (the logical lifetime requested by the caller). It does not expose fail-safe, timeout, refresh, or tagging members in #371.
- R6. `Duration` validation matches today's contract for `GetOrAddAsync` expiration (must be positive); invalid durations fail the same way callers see today.
- R7. An implicit conversion from `TimeSpan` to `CacheEntryOptions` exists so a `TimeSpan` argument is accepted wherever `CacheEntryOptions` is expected, mapping the `TimeSpan` to `Duration`.

### GetOrAddAsync signature

- R8. `ICache.GetOrAddAsync` takes `CacheEntryOptions` in place of `TimeSpan expiration`. Via R7, existing call sites passing a `TimeSpan` compile and behave unchanged.
- R9. The write family (`UpsertAsync`, `UpsertAllAsync`, `TryInsertAsync`, `TryReplaceAsync`, `TryReplaceIfEqualAsync`, increment/set-if/set-add, set operations) retains its `TimeSpan?` parameters and is **not** changed in #371. Writes through this family produce envelopes where logical == physical.

### Behavior preservation & conformance

- R10. With only `Duration`/`TimeSpan` provided (the only available input in #371), Memory's externally observable behavior — hits, misses, expiry timing, eviction, cloning, size limits, stampede protection — is identical to the pre-change behavior. The existing Memory unit/integration tests pass unchanged.
- R11. The cross-provider conformance harness gains the scaffolding for entry-metadata assertions and, for #371, asserts logical/physical **parity** (they coincide) and that reserved slots are empty. The harness is structured so #372 (Redis payload) and #373 (fail-safe) attach their behavioral assertions without restructuring.
- R12. The `CacheEntryOptions` public surface is reviewed and documented as the M1 extension point — later PRs add members to it rather than replacing it.

## Acceptance Examples

- AE1. **Covers R8, R7, R10.**
  - **Given:** existing application code calling `GetOrAddAsync(key, factory, TimeSpan.FromMinutes(10))`.
  - **When:** the code is recompiled against the new signature with no edits.
  - **Then:** it compiles via the implicit conversion and produces the same cache hits/misses and expiry timing as before.

- AE2. **Covers R1, R4, R10.**
  - **Given:** a value added with a 10-minute duration and no other options.
  - **When:** the entry's logical and physical expirations are computed.
  - **Then:** both equal `now + 10 minutes`; the entry expires for readers and becomes eligible for eviction at the same instant, exactly as today.

- AE3. **Covers R9.**
  - **Given:** a value written via `UpsertAsync(key, value, TimeSpan.FromMinutes(5))`.
  - **When:** the resulting envelope is inspected.
  - **Then:** logical == physical == `now + 5 minutes`; the write API surface is unchanged from today.

- AE4. **Covers R2, R3, R11.**
  - **Given:** any entry created in #371.
  - **When:** its reserved slots are inspected.
  - **Then:** the last-factory-error slot is empty and the tags slot is empty, and the conformance harness asserts this parity/emptiness invariant.

## Scope Boundaries

### Deferred to later M1/M2 issues

- Fail-safe / serve-stale-on-failure behavior and the logic that reads logical expiration — #373.
- Soft/hard factory timeouts, background completion, and last-factory-error population/throttling — #374.
- Eager (proactive) refresh and adaptive caching — #375, #376.
- Tagging behavior and `RemoveByTagAsync` (populating and querying the tags slot) — #378–#380.
- Redis payload format that serializes the same field set — #372.
- Sliding expiration — #377.

### Outside this issue's shape

- Extending `CacheEntryOptions` to the write family. The write family stays on `TimeSpan?`; options attach only where a factory exists.
- Introducing a shared envelope CLR type or a `Caching.Core` package. Semantics are shared via the contract + conformance harness, not via a shared type or `InternalsVisibleTo`.
- Any new observable cache behavior. #371 is structure-only.

## Dependencies / Assumptions

- Depends on nothing — this is the foundation slice of M1 (per #369).
- Assumes greenfield posture: the signature change to `GetOrAddAsync` ships as a breaking change with no compatibility shim beyond the implicit conversion (which exists for ergonomics, not back-compat).
- Assumes the conformance harness (`Headless.Caching` provider conformance) is the agreed mechanism for guarding field-set/semantic drift between Memory and the upcoming Redis envelope (#372); if no caching conformance harness exists yet, establishing its skeleton is part of this issue's R11.

## Outstanding Questions

### Resolve before planning

- None blocking. The four shape decisions (envelope depth, shared-semantics-not-type, options scope, implicit conversion) are settled.

### Deferred to planning

- Exact type modeling of the reserved last-factory-error and tags slots (nullable fields, struct vs reference, how `WithExpiration`-style entry derivation carries them) — a dev-plan/codebase-exploration concern, constrained by R2/R3 to "empty in #371."
- Whether `CacheEntryOptions` is a `record`/`class`/`struct` and where it lives within `Headless.Caching.Abstractions` — settled at planning against the existing `Contracts/` layout.
- Whether a caching conformance harness package already exists or must be created as part of R11.
