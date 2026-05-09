---
title: "feat: IPublishFilter + tenant propagation filters"
type: feat
status: active
date: 2026-05-09
origin: docs/brainstorms/2026-05-09-tenant-propagation-filters-requirements.md
issue: https://github.com/xshaheen/headless-framework/issues/235
parent_epic: https://github.com/xshaheen/headless-framework/issues/217
depends_on: https://github.com/xshaheen/headless-framework/issues/228
---

# feat: IPublishFilter + tenant propagation filters

## Summary

Extend `Headless.Messaging.Core` with a new `IPublishFilter` extension point mirroring `IConsumeFilter`, a shared `IPublishExecutionPipeline` invoked from both `DirectPublisher` and `OutboxPublisher`, and a `MultiTenancy/` folder housing the `TenantPropagationPublishFilter` / `TenantPropagationConsumeFilter` pair. A single `MessagingBuilder.AddTenantPropagation()` extension wires both sides over the typed envelope properties shipped in #228.

---

## Problem Frame

The framework already exposes `PublishOptions.TenantId` and `ConsumeContext<T>.TenantId` as first-class envelope properties (PR #239), but ships no built-in mechanism to populate the publish-side property from the ambient `ICurrentTenant` or to restore tenant context on consume. Every multi-tenant consumer authors ~70 lines of glue (see zad-ngo PR #152), and forgetting the consume side is a silent failure that leaks data across tenants. The full motivation lives in the origin requirements doc — see Sources & References below.

Planning surfaced one consequential issue not visible from the brainstorm: the existing single-filter contract on the consume side is silently broken under multi-filter use. `MessagingBuilder.AddSubscribeFilter<T>()` registers via `TryAddScoped<IConsumeFilter, T>` (only the first call wins) and `ConsumeExecutionPipeline` resolves singular `GetService<IConsumeFilter>()` (only one filter runs). The brainstorm's R2 explicitly requires multi-filter behavior; this plan fixes both sides in scope.

---

## Requirements

- R1. `IPublishFilter` and a `PublishFilter` abstract base ship in `Headless.Messaging.Core`, symmetric to `IConsumeFilter` / `ConsumeFilter`. Context types expose a mutable `PublishOptions` carrier so filters can populate `TenantId` before `_ApplyTenantId` runs *(see origin: docs/brainstorms/2026-05-09-tenant-propagation-filters-requirements.md)*.
- R2. `MessagingBuilder.AddPublishFilter<T>()` registers filters with scoped lifetime via `TryAddEnumerable`. Multiple filters resolve via `GetServices<IPublishFilter>()` and execute in registration order; the same applies after the multi-filter fix on the consume side.
- R3. The publish pipeline invokes registered `IPublishFilter` instances before `MessagePublishRequestFactory.Create(...)` so a filter-set `PublishOptions.TenantId` is treated identically to a caller-set value and inherits the existing 4-case integrity policy unchanged.
- R4. `TenantPropagationConsumeFilter` reads `ConsumeContext.TenantId` (typed, never the raw header), calls `ICurrentTenant.Change(...)` when non-null, and disposes the returned scope on both `OnSubscribeExecutedAsync` and `OnSubscribeExceptionAsync`. When `ConsumeContext.TenantId` is `null`, no tenant change occurs.
- R5. `TenantPropagationPublishFilter` sets `PublishOptions.TenantId = ICurrentTenant.Id` only when ambient tenant is non-null AND the typed property is unset. Never writes the raw `Headers.TenantId` header.
- R6. Caller-set `PublishOptions.TenantId` is preserved verbatim — system messages override either by setting it explicitly or publishing while ambient tenant is null.
- R7. `MessagingBuilder.AddTenantPropagation()` is the single registration entry point and is idempotent.
- R8. The consume filter trusts the inbound envelope; XML docs and `docs/llms/multi-tenancy.md` document the trust boundary explicitly.
- R9. Concurrent consumes for different tenants observe AsyncLocal isolation (existing `AsyncLocalCurrentTenantAccessor` behavior — verified by tests, no new code).
- R10. Across retries, the filter restores the same tenant from the persisted envelope on every attempt.
- R11. `docs/llms/messaging.md`, `docs/llms/multi-tenancy.md`, and `src/Headless.Messaging.Core/README.md` document the new abstraction, the multi-filter fix, the `AddTenantPropagation()` extension, and the trust boundary.
- R12. **Multi-filter chain fix (planning-discovered).** The existing `IConsumeFilter` registration is migrated from `TryAddScoped` to `TryAddEnumerable`; `ConsumeExecutionPipeline` is migrated from singular `GetService` to enumerable `GetServices`, with executing-phase forward order and executed/exception-phase reverse order (mirrors ASP.NET filter semantics).

**Origin acceptance examples:** AE1, AE2, AE3, AE4, AE5, AE6, AE7, AE8 — all carried forward; mapping to test scenarios in U5, U6.

---

## Scope Boundaries

- **Strict-tenancy publish guard (#238)** — composes cleanly; ships in its own issue.
- **EF write guard (#234) / Mediator behavior (#236)** — different enforcement seams; share `MissingTenantContextException` only.
- **Envelope signing / external-producer validation** — Phase-2 work in #217. This plan documents the trust assumption (R8) but does not implement validation.
- **A new `Headless.Messaging.MultiTenancy` sub-package** — rejected by origin; `Headless.Messaging.Core` already references `Headless.Core`.
- **Per-publish DI scope** — current scope chain is sufficient for this plan's filters; revisit if a future publish filter needs per-call resolution.
- **Additional `IPublishFilter` consumers** (correlation, principal, idempotency-key, OTel propagation filters) — `IPublishFilter` is the foundation; other filters land as their own work.

### Deferred to Follow-Up Work

- **Migrating zad-ngo's local `TenantPropagation*` copy** to consume the upstream version: separate PR in `xshaheen/zad-ngo` after this plan ships.
- **Strict-tenancy publish guard (#238)**: composes with this plan via `MissingTenantContextException`; sibling issue.

---

## Context & Research

### Relevant Code and Patterns

- `src/Headless.Messaging.Core/IConsumeFilter.cs` — the contract this plan mirrors; `IConsumeFilter` interface, `ConsumeFilter` abstract base, sealed `FilterContext` / `ExecutingContext` / `ExecutedContext` / `ExceptionContext` records.
- `src/Headless.Messaging.Core/Internal/IConsumeExecutionPipeline.cs` — the pipeline this plan mirrors for publish; per-message `CreateAsyncScope`, single-filter resolution to be migrated to enumerable.
- `src/Headless.Messaging.Core/Internal/DirectPublisher.cs` — `_PublishCoreAsync` is the primary publish entry point; filter loop must wrap `_publishRequestFactory.Create(...)` and `_SendAsync`.
- `src/Headless.Messaging.Core/Internal/OutboxPublisher.cs` — second publish entry point; both `PublishAsync` and `PublishDelayAsync` call `_publishRequestFactory.Create`. Easy to miss; both must invoke the new pipeline.
- `src/Headless.Messaging.Core/Internal/IMessagePublishRequestFactory.cs` — owns `_ApplyTenantId` 4-case integrity policy; runs after filters; do not modify.
- `src/Headless.Messaging.Abstractions/PublishOptions.cs:75` — `TenantId` is `{ get; init; }`, immutable after construction. Filter context must wrap it in a settable carrier.
- `src/Headless.Messaging.Core/Configuration/MessagingBuilder.cs:127` — `AddSubscribeFilter<T>()` registration pattern; this plan fixes its `TryAddScoped` to `TryAddEnumerable` and adds the parallel `AddPublishFilter<T>()`.
- `src/Headless.Core/Abstractions/ICurrentTenant.cs` — `Change(string? id, string? name = null)` returns `IDisposable`; `MustDisposeResource` attribute already enforces dispose semantics.
- `src/Headless.Core/Abstractions/ICurrentTenantAccessor.cs` — `AsyncLocalCurrentTenantAccessor` provides per-flow scoping regardless of DI lifetime.
- `tests/Headless.Messaging.Core.Tests.Unit/ConsumeFilterTests.cs` — unit-test pattern (NSubstitute + AwesomeAssertions, in-file context builders).
- `tests/Headless.Messaging.Core.Tests.Unit/DirectPublisherTests.cs` — `TestTransport : ITransport` rig; `_CreateDirectPublisher(...)` helper.
- `tests/Headless.Messaging.Testing.Tests.Unit/EndToEndTests.cs` — `MessagingTestHarness` pattern for full publish→consume flows.

### Institutional Learnings

- `docs/solutions/guides/messaging-transport-provider-guide.md` — Documents the 4-case integrity policy on `Headers.TenantId`; transports must round-trip the value verbatim. Filters must populate the typed property *before* the integrity gate, never write the raw header.
- `docs/solutions/messaging/transport-wrapper-drift-and-doc-sync.md` — When a public messaging API surface is reshaped, human docs, `docs/llms/messaging.md`, and provider-side examples must update in the same change. Applies to `IPublishFilter`.
- `docs/solutions/concurrency/circuit-breaker-transport-thread-safety-patterns.md` — `IDisposable` returned from async cleanup paths must be disposed deterministically even on cancellation; mirrors the consume-side filter's dispose discipline.

Gaps (recorded for `/dev-compound` after implementation): no learnings exist for filter-vs-decorator rationale, multi-tenancy + AsyncLocal in messaging, `TryAddScoped` vs `TryAddEnumerable` for multi-implementation interfaces, or builder-extension idempotency patterns. Worth capturing post-implementation.

### External References

- ASP.NET Core MVC filter pipeline ordering (executing forward, executed/exception reverse) — well-known pattern this plan mirrors for `IPublishFilter` and the consume-side multi-filter fix.

---

## Key Technical Decisions

- **`IPublishExecutionPipeline` is registered Singleton and creates per-publish DI scopes internally**, mirroring `IConsumeExecutionPipeline` (`Setup.cs:111` — `TryAddSingleton`). `DirectPublisher` and `OutboxPublisher` are both `TryAddSingleton` (`Setup.cs:103-106`), so a scoped pipeline would be a captive dependency. The pipeline calls `serviceProvider.CreateAsyncScope()` per `ExecuteAsync` invocation and resolves `IEnumerable<IPublishFilter>` from that scope. Avoids two near-identical filter loops in `DirectPublisher` and `OutboxPublisher` that would drift over time.
- **Multi-filter chain fix is in scope.** Brainstorm R2 requires multi-filter behavior; without the fix, `AddTenantPropagation()` shipped alongside any other consume filter silently drops one. Plan migrates both sides to `TryAddEnumerable` + `GetServices<>` and adopts forward executing / reverse executed-exception ordering.
- **`PublishOptions` is converted from `sealed class` to `sealed record class`.** Filters mutate the carrier via `with` expressions (`ctx.Options = (ctx.Options ?? new()) with { TenantId = ... }`); without record semantics, every filter would have to copy every property by hand and would silently drop new fields added later. The conversion is semantically additive (init-only properties remain init-only; equality is now value-based but no consumer relies on reference equality of `PublishOptions`). Same conversion applied to `ConsumeContext<TMessage>` for symmetry and future-proofing.
- **Cross-phase filter state lives on the filter instance, not on a context Items bag.** Consume pipeline already creates per-message DI scopes; consume filters get a fresh instance per message. Publish-side filters in this plan are stateless. YAGNI for an Items bag until a future publish filter needs cross-phase state.
- **Filters live in `Headless.Messaging.Core/MultiTenancy/`, not a new sub-package.** `Headless.Messaging.Core` already references `Headless.Core` (`src/Headless.Messaging.Core/Headless.Messaging.Core.csproj:26`); a new package adds maintenance overhead for ~60 LOC of filters.
- **Publish-context `Options` is mutable; the `PublishOptions` record handed to the factory is the filter chain's final value.** Each filter receives the current `PublishOptions` reference and reassigns the carrier slot via `with`; subsequent filters and `_publishRequestFactory.Create(...)` see the latest value.
- **`PublishingContext` carries `TimeSpan? DelayTime`** so filters can inspect or modify the scheduled delay (`IScheduledPublisher.PublishDelayAsync<T>(TimeSpan delayTime, ...)` carries delay separately from `PublishOptions`; without exposing it, filters lose visibility into a primary publish dimension). For non-delayed publishes, the property is `null`.
- **`PublishExceptionContext.ExceptionHandled` is a "silent swallow" semantics on the publish side** — when true, `PublishAsync` returns a successful task to the caller even though the transport / outbox failed. This is *not* identical to consume-side handling (which acks a message). XML docs on both `PublishExceptionContext` and `IPublishFilter`, plus `docs/llms/messaging.md`, must call this out explicitly so filter authors don't accidentally mask real publish failures. Otherwise, exceptions rethrow via `ExceptionDispatchInfo.Capture(...).Throw()`, mirroring the consume-side `ReThrow()` pattern.

---

## Open Questions

### Resolved During Planning

- **Exact `IPublishFilter` triad shape** *(origin Deferred)*: mirror `IConsumeFilter` exactly — `OnPublishExecutingAsync(PublishingContext)` / `OnPublishExecutedAsync(PublishedContext)` / `OnPublishExceptionAsync(PublishExceptionContext)`. Context inheritance mirrors the consume side (`PublishFilterContext` base + sealed phase-specific records).
- **Pipeline insertion point** *(origin Deferred)*: `IPublishExecutionPipeline` is invoked from `DirectPublisher._PublishCoreAsync` and from `OutboxPublisher.PublishAsync` / `PublishDelayAsync`, before `_publishRequestFactory.Create(...)`.
- **Transport bypass audit** *(origin Deferred)*: confirmed — no transport bypasses. All 11 transports route through `DirectPublisher._SendAsync` or `OutboxPublisher` → `IDispatcher` → `IMessageSender` → `ITransport.SendAsync`. Single insertion point covers everything.
- **`AddTenantPropagation()` idempotency mechanics** *(origin Deferred)*: `TryAddEnumerable(ServiceDescriptor.Scoped<IFilter, TFilter>())` deduplicates by (service, implementation) tuple, so calling `AddTenantPropagation()` twice is a safe no-op for both filter types.
- **Multi-filter ordering semantics**: executing-phase forward order, executed and exception-phase reverse order — matches ASP.NET MVC filter pipeline; documented in `IPublishFilter` and updated `IConsumeFilter` XML docs.

### Deferred to Implementation

- **Whether the existing test surface relies on `PublishOptions` / `ConsumeContext` reference equality.** U1's sweep step is the resolution; recording here so implementation does not skip it.
- **Whether existing `ConsumeFilterTests.cs` tests assert single-filter-only behavior** that breaks when migrating to enumerable resolution. Read the file in U2 and migrate any affected assertions.
- **Diagnostic listener events around the new pipeline.** `DirectPublisher` already emits `BeforePublish` / `AfterPublish` / `ErrorPublish` events in `_SendAsync`. Decide during U3 whether filter execution emits its own events or remains transparent — leaning transparent (filters are an internal pipeline detail, not a tracing-relevant boundary), but confirm against the existing diagnostic listener conventions.

---

## High-Level Technical Design

> *This illustrates the intended approach and is directional guidance for review, not implementation specification. The implementing agent should treat it as context, not code to reproduce.*

**Publish pipeline shape after this plan:**

```
IMessagePublisher.PublishAsync(message, options)
    └── DirectPublisher._PublishCoreAsync                                      [unchanged entry point]
            └── IPublishExecutionPipeline.ExecuteAsync                         [NEW — wraps the call below]
                    ├── for each IPublishFilter in registration order:
                    │       OnPublishExecutingAsync(ctx)   ← filter sets ctx.Options.TenantId, etc.
                    ├── _publishRequestFactory.Create(ctx.Options)             [unchanged — _ApplyTenantId runs here]
                    ├── transport.SendAsync(...)                               [unchanged]
                    ├── for each IPublishFilter in REVERSE order:
                    │       OnPublishExecutedAsync(ctx)
                    └── on exception: for each filter in REVERSE order:
                            OnPublishExceptionAsync(ctx)   ← any filter may set ExceptionHandled=true to swallow

OutboxPublisher.PublishAsync / PublishDelayAsync
    └── IPublishExecutionPipeline.ExecuteAsync                                 [NEW — same wrapper as Direct path]
            └── _publishRequestFactory.Create + IDataStorage.StoreMessage      [outbox-specific tail]
```

**Multi-filter ordering semantics (mirrored to consume side via U2):**

```
Filters: [A, B, C]   (registration order)

Executing phase:   A.OnExecutingAsync → B.OnExecutingAsync → C.OnExecutingAsync
                                                        ↓
                                        Inner work (factory.Create + transport.Send)
                                                        ↓
Executed phase:    C.OnExecutedAsync ← B.OnExecutedAsync ← A.OnExecutedAsync
                                                        ↓ (on throw)
Exception phase:   C.OnExceptionAsync ← B.OnExceptionAsync ← A.OnExceptionAsync
```

This pattern matches ASP.NET MVC filters and gives "stack" semantics — outer filters wrap inner filters' lifetime, including their exceptions.

**Filter state lifecycle (consume side, illustrative):**

```
Per-message DI scope created by ConsumeExecutionPipeline
    ↓
Fresh TenantPropagationConsumeFilter instance resolved (Scoped lifetime)
    ↓
OnSubscribeExecutingAsync:  if (ctx.TenantId != null)
                                _scope = currentTenant.Change(ctx.TenantId)   // stored on instance field
    ↓
Consumer body runs under tenant
    ↓
OnSubscribeExecutedAsync:   _scope?.Dispose()    // restores prior tenant
        OR
OnSubscribeExceptionAsync:  _scope?.Dispose()    // restores even on failure
    ↓
DI scope disposed; filter instance discarded
```

---

## Implementation Units

### U1. `IPublishFilter` contract, base class, context types, and `PublishOptions` record conversion

**Goal:** Define the new abstraction symmetric to `IConsumeFilter` so subsequent units have something to integrate against. Convert `PublishOptions` (and `ConsumeContext<T>` for symmetry) to record class so filters can mutate via `with` expressions. Pure addition + one ergonomic refactor; no public-API behavior change.

**Requirements:** R1

**Dependencies:** None

**Files:**
- Create: `src/Headless.Messaging.Core/IPublishFilter.cs`
- Modify: `src/Headless.Messaging.Abstractions/PublishOptions.cs` (convert `sealed class` → `sealed record class`; preserve all init-only properties verbatim)
- Modify: `src/Headless.Messaging.Abstractions/ConsumeContext.cs` (convert `sealed class` → `sealed record class`; preserve all init-only properties)
- Test: `tests/Headless.Messaging.Core.Tests.Unit/PublishFilterTests.cs`
- Modify: existing tests against `PublishOptions` / `ConsumeContext` if any rely on reference equality (sweep `tests/` for `ReferenceEquals` or identity-based assertions; convert to value-equality if found).

**Approach:**
- **Record conversion (do this first).** `PublishOptions` and `ConsumeContext<TMessage>` become `sealed record class` definitions. Init-only properties remain. Equality becomes value-based — sweep tests for any `ReferenceEquals` or identity-based assertions and convert. The pipeline's `MessagePublishRequestFactory._ApplyTenantId` / `_BuildConsumeContext` factory paths are unaffected because they operate on the property set, not on identity.
- Define `IPublishFilter` with three methods mirroring `IConsumeFilter`: `OnPublishExecutingAsync(PublishingContext)`, `OnPublishExecutedAsync(PublishedContext)`, `OnPublishExceptionAsync(PublishExceptionContext)`.
- Provide `PublishFilter` abstract base with default no-op `ValueTask.CompletedTask` returns, mirroring `ConsumeFilter`.
- Define `PublishFilterContext` base record carrying message metadata (message type, content reference, topic when known), a settable `Options` property of type `PublishOptions?` (current options as a record, mutated by filters via `with`), and a settable `DelayTime` property of type `TimeSpan?` (null for non-delayed publishes; carries `IScheduledPublisher.PublishDelayAsync`'s delay parameter so filters can inspect/modify).
- Define sealed `PublishingContext` (pre-publish — filters mutate `Options` and `DelayTime` here), `PublishedContext` (post-success), `PublishExceptionContext` (on throw — exposes `Exception` and a settable `ExceptionHandled` flag).
- All context types are `sealed`; XML docs explicitly note: (a) cross-phase filter state belongs on the filter instance, not the context; (b) `ExceptionHandled = true` on the publish side **silently swallows** the failure — `PublishAsync` returns success even if the transport failed, which is *not* the same semantics as consume-side ack. Filter authors must understand this before setting the flag.

**Patterns to follow:**
- Mirror `src/Headless.Messaging.Core/IConsumeFilter.cs` line-for-line where shape matches; only deviate where publish vs consume semantics genuinely differ (e.g., no `Result` slot — publish has no caller-visible return).
- Existing record-style classes elsewhere in the framework (sweep `src/Headless.*/` for `sealed record class` patterns to match formatting and XML doc style).

**Test scenarios:**
- Happy path: `PublishFilter` (abstract base) overrides — calling each method on a concrete derived class with no overrides returns a completed `ValueTask`.
- Edge: a derived filter overriding only `OnPublishExecutingAsync` leaves the other two methods at the base no-op behavior.
- Edge: `PublishingContext.Options` is settable — assigning `Options with { TenantId = "x" }` is observable via subsequent reads.
- Edge: `PublishingContext.DelayTime` defaults to `null` for non-delayed publishes; settable to a `TimeSpan` value.
- Edge: `PublishExceptionContext.ExceptionHandled` defaults to `false`; XML doc warning about silent-swallow semantics is present (verified by sourcegen / lint check if available, otherwise manual review).
- Record conversion: `PublishOptions { TenantId = "a" } == PublishOptions { TenantId = "a" }` returns true (value equality after conversion); `with` expressions compile and produce expected mutations.

**Verification:**
- `IPublishFilter` and `PublishFilter` compile in `Headless.Messaging.Core` with no consumer code changes.
- `PublishFilterTests.cs` tests pass; coverage at ≥85% line / ≥80% branch on the new file.
- No XML doc warnings on the new types.
- `dotnet build --no-incremental -v:q -nologo /clp:ErrorsOnly` clean across the solution after record conversions (no callers broken).

---

### U2. Multi-filter chain fix on consume side

**Goal:** Fix the silent multi-filter bug on `IConsumeFilter`: registration via `TryAddScoped` (only first wins) and resolution via singular `GetService` (only one runs). Migrate to enumerable registration and resolution with forward-executing / reverse-executed-exception ordering.

**Requirements:** R12, R2 (the publish-side migration in U4 inherits the same pattern)

**Dependencies:** None — independent bug fix; sequenced before U3 so the new publish-side pipeline is built on a known-good ordering pattern.

**Files:**
- Modify: `src/Headless.Messaging.Core/Configuration/MessagingBuilder.cs` (`AddSubscribeFilter<T>()`)
- Modify: `src/Headless.Messaging.Core/Internal/IConsumeExecutionPipeline.cs` (`ConsumeExecutionPipeline.ExecuteAsync`)
- Modify: `src/Headless.Messaging.Core/IConsumeFilter.cs` (XML docs only — document multi-filter ordering)
- Modify: `tests/Headless.Messaging.Core.Tests.Unit/ConsumeFilterTests.cs` (add multi-filter tests; review existing tests for any single-filter-only assertions that break)

**Approach:**
- `AddSubscribeFilter<T>()`: replace `Services.TryAddScoped<IConsumeFilter, T>()` with `Services.TryAddEnumerable(ServiceDescriptor.Scoped<IConsumeFilter, T>())`. Same-type duplicates dedup automatically.
- `ConsumeExecutionPipeline.ExecuteAsync`: replace `provider.GetService<IConsumeFilter>()` with `provider.GetServices<IConsumeFilter>().ToArray()` and iterate. Executing phase: foreach in array order. Executed phase: foreach in reverse. Exception phase: foreach in reverse, with `ExceptionHandled = true` from any filter swallowing the exception (last-wins semantics across filters when multiple set the flag; document explicitly).
- `IConsumeFilter` XML doc: update the lying claim ("Filters are executed in the order they are registered") to describe accurate ordering with the executing-forward / reverse-executed-exception pattern.

**Patterns to follow:**
- ASP.NET Core MVC filter pipeline ordering (well-known reference for forward-executing / reverse-after).

**Execution note:** Add the multi-filter test cases to `ConsumeFilterTests.cs` *before* changing the pipeline code. The test suite should fail on the broken behavior, then pass after the migration — characterization-first against the bug.

**Test scenarios:**
- Happy: two filters `A` and `B` registered via `AddSubscribeFilter<A>().AddSubscribeFilter<B>()` — both resolve via `GetServices<IConsumeFilter>()`.
- Order (executing): with filters `[A, B]` and a recording consumer body, `A.OnSubscribeExecutingAsync` runs before `B.OnSubscribeExecutingAsync` before the consumer body.
- Order (executed): `B.OnSubscribeExecutedAsync` runs before `A.OnSubscribeExecutedAsync` (reverse).
- Order (exception): consumer throws → `B.OnSubscribeExceptionAsync` runs before `A.OnSubscribeExceptionAsync` (reverse).
- Exception handled: filter `A` (outer) sets `ExceptionHandled = true` → exception is swallowed; filter `B` (inner) executed exception path even when outer swallows.
- Edge: zero filters registered → pipeline degrades to direct consumer invocation (regression check vs. existing behavior).
- Edge: same filter type registered twice via `AddSubscribeFilter<A>().AddSubscribeFilter<A>()` → `TryAddEnumerable` dedups; only one instance resolves.
- Edge: filter `A` registered, filter `B` resolves successfully even when `A` throws synchronously in `OnSubscribeExecutingAsync` (does the chain abort? — answer: yes, abort and run reverse-order exception phase from `B` backward, mirroring ASP.NET behavior; cover with a test).

**Verification:**
- All existing `ConsumeFilterTests.cs` tests pass (or are explicitly updated to match new ordering).
- New multi-filter tests pass.
- `provider.GetServices<IConsumeFilter>()` returns instances in registration order across the test surface.

---

### U3. `IPublishExecutionPipeline` + integration into both publishers

**Goal:** Add the publish-side filter pipeline and wire it into `DirectPublisher._PublishCoreAsync` and `OutboxPublisher.PublishAsync` / `PublishDelayAsync`. This is the unit that gives `IPublishFilter` runtime effect.

**Requirements:** R3, R10

**Dependencies:** U1, U2

**Files:**
- Create: `src/Headless.Messaging.Core/Internal/IPublishExecutionPipeline.cs` (interface + `PublishExecutionPipeline` implementation)
- Modify: `src/Headless.Messaging.Core/Internal/DirectPublisher.cs` (inject `IPublishExecutionPipeline`; replace inline `_publishRequestFactory.Create + _SendAsync` with pipeline.ExecuteAsync)
- Modify: `src/Headless.Messaging.Core/Internal/OutboxPublisher.cs` (same pattern in both `PublishAsync` and `PublishDelayAsync`)
- Modify: `src/Headless.Messaging.Core/Setup.cs` (register `IPublishExecutionPipeline` with appropriate lifetime — singleton if stateless, scoped otherwise; mirror the existing pipeline registrations)
- Test: `tests/Headless.Messaging.Core.Tests.Unit/PublishExecutionPipelineTests.cs`
- Modify: `tests/Headless.Messaging.Core.Tests.Unit/DirectPublisherTests.cs` (verify pipeline invocation; existing `TestTransport` rig + `_CreateDirectPublisher` helper still works)
- Modify: `tests/Headless.Messaging.Core.Tests.Unit/OutboxPublisherTests.cs` if present (apply same coverage)

**Approach:**
- `IPublishExecutionPipeline.ExecuteAsync<T>(T? content, PublishOptions? options, TimeSpan? delayTime, Func<PublishOptions?, TimeSpan?, CancellationToken, Task> innerPublish, CancellationToken cancellationToken)` — accepts a delegate for the publisher-specific tail (transport send vs. outbox storage vs. delayed storage). The pipeline runs filters around the delegate invocation. The `delayTime` parameter threads `IScheduledPublisher.PublishDelayAsync`'s value through to the context so filters can observe and mutate it.
- Pipeline construction: register `IPublishExecutionPipeline` as **Singleton** in `Setup.cs` (mirrors `IConsumeExecutionPipeline` registration at `Setup.cs:111`; both `DirectPublisher` and `OutboxPublisher` are themselves Singleton at `Setup.cs:103-106`, so a scoped pipeline would be a captive dependency). The pipeline's implementation accepts `IServiceProvider` as a constructor dependency and calls `serviceProvider.CreateAsyncScope()` per `ExecuteAsync` invocation. Inside the scope, resolve `provider.GetServices<IPublishFilter>().ToArray()`, build a `PublishingContext` carrier with the mutable `Options` and `DelayTime` slots, run the executing chain forward, invoke `innerPublish(ctx.Options, ctx.DelayTime, ct)`, run executed chain reverse on success, run exception chain reverse on throw with `ExceptionHandled` honored (silent-swallow semantics — see U1).
- `DirectPublisher._PublishCoreAsync`: inject `IPublishExecutionPipeline`; the inner delegate calls `_publishRequestFactory.Create(filteredOptions)` + `_SendAsync(...)` exactly as today (factory.Create still applies `_ApplyTenantId` to the filter-mutated options). The `delayTime` parameter is unused on the direct path — the inner delegate ignores it.
- `OutboxPublisher.PublishAsync` / `PublishDelayAsync`: inject the pipeline; inner delegates call the existing `_publishRequestFactory.Create(filteredOptions, filteredDelayTime?)` + `_PublishInternalAsync(...)`. Both methods pass through the pipeline; `PublishAsync` calls with `delayTime: null`, `PublishDelayAsync` calls with the caller-supplied delay.

**Critical implementation note:** `DirectPublisher`, `OutboxPublisher`, and `MessagePublishRequestFactory` are all `TryAddSingleton`. The pipeline must NOT be Scoped — it must be Singleton with internal scope creation, exactly as `ConsumeExecutionPipeline` does at `IConsumeExecutionPipeline.cs:43` (`await using var scope = serviceProvider.CreateAsyncScope()`). This ensures thread safety across concurrent publishes from the singleton publishers.

**Execution note:** Start with `DirectPublisher` integration; verify e2e via `DirectPublisherTests`; then mirror the change in `OutboxPublisher` (both methods). Easy to forget the `PublishDelayAsync` site — explicit verification step covers it.

**Test scenarios:**
- Happy (DirectPublisher): pipeline runs single filter's executing → factory.Create → transport.Send → executed.
- Happy (OutboxPublisher.PublishAsync): pipeline runs filter chain → factory.Create → storage write.
- Happy (OutboxPublisher.PublishDelayAsync): same as PublishAsync with non-null `delayTime`; explicit test prevents regression.
- Filter mutation (options): filter sets `ctx.Options = ctx.Options with { TenantId = "acme" }` → factory.Create sees `TenantId = "acme"` → `_ApplyTenantId` accepts it → wire header `headless-tenant-id = "acme"`.
- Filter mutation (delay): filter sets `ctx.DelayTime = TimeSpan.FromMinutes(5)` on a `PublishDelayAsync` call → factory.Create receives the mutated delay; resulting envelope's scheduled time reflects the new value.
- Multi-filter ordering: same as U2 but for publish — executing forward, executed/exception reverse.
- Exception path (transport throws): pipeline calls exception filters in reverse; if any filter sets `ExceptionHandled = true`, exception is swallowed and `PublishAsync` returns success (silent-swallow); if not, rethrown via `ExceptionDispatchInfo.Throw()` preserving the stack.
- Exception path (silent-swallow consequence): test explicitly asserts that with `ExceptionHandled = true`, the caller's `await PublishAsync(...)` completes without throwing even though `_SendAsync` threw — guards the documented (and risky) semantics from regression.
- Edge: zero filters registered → pipeline still creates per-publish scope but iterates an empty filter array; behavior matches pre-pipeline direct delegate call.
- Edge: filter throws in executing → exception phase runs in reverse from filters earlier in the chain; inner publish does NOT execute.
- Concurrency: pipeline used from singleton `DirectPublisher` across two concurrent publishes resolves separate filter instances per call (per-publish scope guarantees isolation); no cross-publish state bleed.
- Integration: filter chain `[TenantPropagationPublishFilter]` (built in U5) running over a DirectPublisher publish under ambient tenant `"acme"` results in `_publishRequestFactory.Create` receiving `PublishOptions.TenantId = "acme"`. (Cross-cutting verification with U5.)

**Verification:**
- `DirectPublisher` and both `OutboxPublisher` methods invoke the pipeline; `git grep _publishRequestFactory.Create src/Headless.Messaging.Core/Internal/` shows only pipeline-mediated callsites.
- `PublishExecutionPipelineTests` and modified publisher tests pass.
- Coverage on the new pipeline ≥85% line / ≥80% branch.
- Existing integration tests for transports (`Headless.Messaging.RabbitMq.Tests.Integration` etc.) pass without modification — proves transport coverage is automatic.

---

### U4. `MessagingBuilder.AddPublishFilter<T>()`

**Goal:** Public registration API for `IPublishFilter`, symmetric to `AddSubscribeFilter<T>()` and using the corrected `TryAddEnumerable` pattern from the start.

**Requirements:** R2

**Dependencies:** U1, U2

**Files:**
- Modify: `src/Headless.Messaging.Core/Configuration/MessagingBuilder.cs` (add `AddPublishFilter<T>()` next to `AddSubscribeFilter<T>()`)
- Test: `tests/Headless.Messaging.Core.Tests.Unit/MessagingBuilderTests.cs` (add tests; create the file if it does not exist)

**Approach:**
- Method signature: `public MessagingBuilder AddPublishFilter<T>() where T : class, IPublishFilter`.
- Body: `Services.TryAddEnumerable(ServiceDescriptor.Scoped<IPublishFilter, T>()); return this;`
- XML doc describes correct multi-filter ordering (forward executing, reverse executed/exception) and idempotency under same-type re-registration.

**Patterns to follow:**
- The (now-corrected) `AddSubscribeFilter<T>()` from U2.

**Test scenarios:**
- Happy: `AddPublishFilter<TestFilter>()` registers a scoped `IPublishFilter` resolvable via `GetServices<IPublishFilter>()`.
- Multi-type: `AddPublishFilter<A>().AddPublishFilter<B>()` resolves both in registration order.
- Idempotency: `AddPublishFilter<A>().AddPublishFilter<A>()` registers `A` once.
- Edge: registered with no `AddHeadlessMessaging()` call — should the call be guarded? Mirror `AddSubscribeFilter`'s current behavior (no guard); document.

**Verification:**
- `MessagingBuilderTests` pass.
- DI graph after `AddHeadlessMessaging(m => m.AddPublishFilter<X>())` contains exactly one `IPublishFilter` descriptor of `X`.

---

### U5. Tenant propagation filter pair + `AddTenantPropagation()` extension

**Goal:** Ship the two filters that motivated this work, plus the single registration entry point. This is the user-visible deliverable.

**Requirements:** R4, R5, R6, R7, R8

**Dependencies:** U1, U2, U3, U4

**Files:**
- Create: `src/Headless.Messaging.Core/MultiTenancy/TenantPropagationConsumeFilter.cs`
- Create: `src/Headless.Messaging.Core/MultiTenancy/TenantPropagationPublishFilter.cs`
- Create: `src/Headless.Messaging.Core/MultiTenancy/MultiTenancyMessagingBuilderExtensions.cs` (hosts `AddTenantPropagation()` extension method)
- Test: `tests/Headless.Messaging.Core.Tests.Unit/MultiTenancy/TenantPropagationConsumeFilterTests.cs`
- Test: `tests/Headless.Messaging.Core.Tests.Unit/MultiTenancy/TenantPropagationPublishFilterTests.cs`

**Approach:**
- `TenantPropagationConsumeFilter` extends `ConsumeFilter`; injects `ICurrentTenant` (singleton). On executing: read `ConsumeContext.TenantId` from the executing context's argument list (typed); if non-null, call `currentTenant.Change(value)` and store the `IDisposable` on a private field. On executed and exception: dispose the field if non-null. XML doc surfaces the trust boundary explicitly: filter trusts the inbound envelope; topics with external producers must layer validation.
- `TenantPropagationPublishFilter` extends `PublishFilter`; injects `ICurrentTenant`. On executing: if `ctx.Options?.TenantId is null && currentTenant.Id is { } id` → assign new `PublishOptions { ...current, TenantId = id }` to the carrier (record-with semantics; or build manually since `PublishOptions` may not be a record — confirm shape from `PublishOptions.cs`).
- `MultiTenancyMessagingBuilderExtensions.AddTenantPropagation(this MessagingBuilder builder)` calls `AddSubscribeFilter<TenantPropagationConsumeFilter>()` and `AddPublishFilter<TenantPropagationPublishFilter>()`; returns the builder for chaining. Idempotency inherits from `TryAddEnumerable` in both underlying registrations.
- The filter classes are placed in namespace `Headless.Messaging.MultiTenancy` (folder-rooted) but use `RootNamespace=Headless.Messaging` per the existing csproj — confirm namespace resolves correctly and matches existing `MultiTenancy/` conventions in the framework if any (e.g., `Headless.Mediator/Behaviors/TenantRequiredBehavior.cs` uses `Headless.Mediator.Behaviors`). Match Headless.Mediator style.

**Patterns to follow:**
- `src/Headless.Mediator/Behaviors/TenantRequiredBehavior.cs` — sister-feature pattern for tenant enforcement at the mediator boundary; same `ICurrentTenant` injection idiom.
- `src/Headless.Messaging.Core/IConsumeFilter.cs` `ConsumeFilter` derivation pattern.

**Test scenarios:**
- **TenantPropagationConsumeFilter — Happy (Covers AE1):** `ConsumeContext<T>.TenantId = "acme"` → executing phase calls `ICurrentTenant.Change("acme")` (NSubstitute verifies) → executed phase disposes the returned scope.
- **TenantPropagationConsumeFilter — Null path (Covers AE2):** `ConsumeContext.TenantId = null` → no `Change` call → no disposal.
- **TenantPropagationConsumeFilter — Exception path:** consumer body throws → exception phase still disposes the scope (mirror dispose discipline from circuit-breaker learning).
- **TenantPropagationConsumeFilter — Restoration:** prior `ICurrentTenant.Id` value (e.g., from outer worker scope) is restored after the filter's scope disposes.
- **TenantPropagationConsumeFilter — Cancellation:** filter disposes scope deterministically when `OperationCanceledException` propagates.
- **TenantPropagationPublishFilter — Happy (Covers AE3):** ambient `ICurrentTenant.Id = "acme"`, `ctx.Options.TenantId = null` → executing phase mutates carrier so `ctx.Options.TenantId = "acme"`.
- **TenantPropagationPublishFilter — Caller override preserved (Covers AE4):** ambient `"acme"`, `ctx.Options.TenantId = "system"` → no mutation.
- **TenantPropagationPublishFilter — Null ambient (Covers AE5):** `ICurrentTenant.Id = null`, `ctx.Options.TenantId = null` → no mutation; carrier still null.
- **TenantPropagationPublishFilter — Null ambient + explicit (Covers AE7):** `ICurrentTenant.Id = null`, `ctx.Options.TenantId = "explicit"` → no mutation; preserved.
- **AddTenantPropagation — Happy:** registration produces both filter types in DI; `GetServices<IConsumeFilter>()` and `GetServices<IPublishFilter>()` each contain one tenant filter.
- **AddTenantPropagation — Idempotency:** double-registration does not duplicate.
- **AddTenantPropagation — Composition with other filters:** `AddSubscribeFilter<X>().AddTenantPropagation()` resolves both `X` and `TenantPropagationConsumeFilter` (regression guard against the U2 fix).

**Verification:**
- All filter unit tests pass (NSubstitute mocks verify `Change` and `Dispose` calls).
- `AddTenantPropagation()` is idempotent under double-call.
- Composes correctly with at least one other consume filter and one other publish filter (verified via `MessagingBuilderTests` extension).

---

### U6. End-to-end propagation tests

**Goal:** Verify the full publish→consume contract through `MessagingTestHarness` covering all eight origin Acceptance Examples and the AsyncLocal isolation requirement.

**Requirements:** R9, R10; covers origin AE1–AE8

**Dependencies:** U1–U5

**Files:**
- Create: `tests/Headless.Messaging.Testing.Tests.Unit/MultiTenancy/TenantPropagationE2ETests.cs`

**Approach:**
- Use `MessagingTestHarness` (per `tests/Headless.Messaging.Testing.Tests.Unit/EndToEndTests.cs` template) with in-memory transport and `AddTenantPropagation()` registered.
- Each test sets ambient `ICurrentTenant` via `currentTenant.Change(...)` inside a `using` block before publishing, then asserts the consumed `ConsumeContext.TenantId` matches.
- Concurrent test (AE8): publish two messages under different ambient tenants in parallel; consume both; assert each consumer body observed its own tenant via captured tenant snapshots.
- Retry test (AE6): use a consumer that throws on first attempt and succeeds on second; assert both attempts saw the same `ConsumeContext.TenantId`; verify `ICurrentTenant.Change`/dispose called on each attempt.

**Patterns to follow:**
- `tests/Headless.Messaging.Testing.Tests.Unit/EndToEndTests.cs` for harness setup.
- `tests/Headless.Messaging.Testing.Tests.Unit/SharedHostIsolationTests.cs` for AsyncLocal isolation patterns under concurrent dispatch.

**Test scenarios:**
- **Covers AE1, AE3 — round-trip:** publish under `ICurrentTenant.Id = "acme"` → consume observes `ICurrentTenant.Id == "acme"` inside consumer body; restored after.
- **Covers AE2, AE5, AE7 — system message:** publish under no ambient tenant → consume observes no tenant change; `ConsumeContext.TenantId == null`.
- **Covers AE4 — caller override preserved:** publish with explicit `PublishOptions.TenantId = "system"` under ambient `"acme"` → consume observes `"system"`, not `"acme"`.
- **Covers AE6 — retry preserves envelope:** consumer throws on first attempt, succeeds on second → both attempts see same tenant; both `Change`/dispose pairs balanced.
- **Covers AE8 — concurrent isolation:** two parallel publishes with tenants `"acme"` and `"globex"` → two parallel consumes observe their own tenant; no cross-talk via captured snapshots.
- **Integration — outbox publish:** publish via `IOutboxPublisher` under ambient tenant → outbox storage holds the typed `TenantId`; subsequent dispatch consumes with the same tenant.

**Verification:**
- All eight origin AEs are covered by at least one test scenario (mapped above).
- Tests run via `Skill(compound-engineering:dotnet-test)` against `Headless.Messaging.Testing.Tests.Unit`.
- No flakiness across 10 sequential runs of the concurrent isolation test.

---

### U7. Documentation updates

**Goal:** Document the new abstraction, the multi-filter fix, the trust boundary, and the registration extension across the three doc surfaces.

**Requirements:** R11, R8

**Dependencies:** U1–U6

**Files:**
- Modify: `docs/llms/messaging.md`
- Modify: `docs/llms/multi-tenancy.md`
- Modify: `src/Headless.Messaging.Core/README.md`
- Possibly modify: `docs/solutions/guides/messaging-transport-provider-guide.md` if the new pipeline affects transport-provider guidance.

**Approach:**
- `docs/llms/messaging.md`: add a "Filters" section covering both `IConsumeFilter` (with multi-filter ordering note) and `IPublishFilter`. Document `AddSubscribeFilter<T>()`, `AddPublishFilter<T>()`, and ordering semantics. **Explicitly warn about `PublishExceptionContext.ExceptionHandled` silent-swallow semantics** — handling on publish ≠ handling on consume; setting it true masks transport / outbox failures from the caller. Add a "Multi-tenancy" subsection pointing to the `AddTenantPropagation()` one-liner.
- `docs/llms/multi-tenancy.md`: add a messaging section covering `PublishOptions.TenantId` / `ConsumeContext.TenantId` envelope plumbing (link to #228), the `AddTenantPropagation()` extension, the trust boundary (filter trusts envelope; external producers must layer validation), and the link to `MissingTenantContextException` for the strict-tenancy guard sibling (#238) for forward-reference.
- `src/Headless.Messaging.Core/README.md`: add filter usage example for both `IConsumeFilter` and `IPublishFilter` plus the `AddTenantPropagation()` quick-start.
- All three docs: update the multi-filter ordering claim consistently (forward executing, reverse executed/exception).

**Test expectation:** none — pure documentation. Verification is manual review against the implementation.

**Verification:**
- All three docs reference the same ordering semantics.
- Trust-boundary language matches the XML doc on `TenantPropagationConsumeFilter`.
- `AddTenantPropagation()` example compiles when copy-pasted into a fresh `AddHeadlessMessaging` call.
- No dead references to the old `TryAddScoped` registration pattern.

---

## System-Wide Impact

- **Interaction graph:** All 11 transport packages (RabbitMq, Kafka, AzureServiceBus, AwsSqs, Pulsar, Nats, RedisStreams, InMemoryQueue + storage adapters) inherit the new filter behavior automatically because every publish path routes through `DirectPublisher` or `OutboxPublisher`. `IDirectPublisher` consumers (`HybridCache`, `DynamicPermissionDefinitionStore`) are also covered transparently.
- **Error propagation:** Filter exceptions in executing-phase abort the chain and trigger reverse-order exception filters; transport exceptions trigger reverse-order exception filters before propagating; `ExceptionHandled = true` in any filter swallows the exception. Existing `PublisherSentFailedException` semantics in `DirectPublisher._SendAsync` preserved — the exception still flows through the new pipeline boundary.
- **State lifecycle risks:** `TenantPropagationConsumeFilter` stores `IDisposable` on a private field; correctness depends on per-message DI scope (already guaranteed by `ConsumeExecutionPipeline.CreateAsyncScope`). For publish, filters must be stateless or tolerate sharing within a DI scope (this plan's filters are stateless).
- **API surface parity:** `IPublishFilter` mirrors `IConsumeFilter`; `AddPublishFilter<T>()` mirrors `AddSubscribeFilter<T>()`; `AddTenantPropagation()` is the only new convenience extension. No breaking changes to existing transport contracts or `IMessagePublisher` / `IOutboxPublisher`.
- **Integration coverage:** End-to-end tests in U6 prove publish→consume round-trip across the full stack; transport integration tests inherit coverage via the transparent pipeline insertion.
- **Unchanged invariants:** `MessagePublishRequestFactory._ApplyTenantId` 4-case integrity policy unchanged; `Headers.TenantId` wire key unchanged; `PublishOptions.TenantId` validation rules unchanged; `ConsumeContext.TenantId` lenient resolution in `_ResolveTenantId` unchanged. The plan adds capability around the existing surface; it does not modify the existing surface.

---

## Risks & Dependencies

| Risk | Mitigation |
|------|------------|
| **DI captive-dependency trap.** `DirectPublisher` and `OutboxPublisher` are Singleton; a Scoped `IPublishExecutionPipeline` would be silently captured and break per-publish filter resolution. | U3 explicitly registers the pipeline as Singleton with internal `CreateAsyncScope()` per call, mirroring `IConsumeExecutionPipeline` (`Setup.cs:111`). U3 verification step asserts pipeline lifetime. |
| **`ExceptionHandled` silent-swallow semantics on publish are easy to misuse.** A filter that handles exceptions returns success to the caller even when the transport / outbox failed — caller has no idea the message wasn't sent. | XML docs on `PublishExceptionContext` and `IPublishFilter`, plus `docs/llms/messaging.md` warnings, explicitly call out the silent-swallow semantics and contrast them with consume-side ack semantics. U3 includes an explicit test asserting silent-swallow behavior so the contract is locked in. |
| Multi-filter ordering semantics surprise existing `IConsumeFilter` users (currently only one filter resolves; switching to enumerable changes runtime behavior). | Greenfield project; CLAUDE.md explicitly prefers cleaner APIs over compatibility shims. The current behavior is broken (XML doc lies about ordering), so users relying on multi-filter behavior already have a bug. Document the change in U7. |
| Forgetting `OutboxPublisher.PublishDelayAsync` integration site — silent tenant propagation gap for delayed messages. | U3 explicitly enumerates both `OutboxPublisher` methods plus the `delayTime` parameter threading; verification step requires both to invoke the pipeline. End-to-end test in U6 covers outbox path. |
| Filter execution adds overhead to every publish (minor). | Pipeline is a thin wrapper; per-publish cost is one `CreateAsyncScope()` + one DI resolution + one delegate call. Zero-filter case still pays scope overhead — verify via existing benchmarks if any; otherwise treat as immaterial vs. transport latency. |
| **`PublishOptions` / `ConsumeContext` record conversion changes equality semantics from reference to value.** Existing code relying on identity comparisons (`ReferenceEquals`, `==` on stored references) would silently change behavior. | U1 sweeps `tests/` and `src/` for `ReferenceEquals(...PublishOptions...)` / `ReferenceEquals(...ConsumeContext...)` patterns and `==` on stored option references; converts to value-semantic checks where present, documents the change otherwise. Build-clean check after conversion catches compile-time fallout. |
| `ICurrentTenant` is registered as `NullCurrentTenant` (singleton) by `Headless.Messaging.Core/Setup.cs:101` if no application overrides — `TenantPropagationPublishFilter` would observe null `Id` always and silently no-op. | Document in `AddTenantPropagation()` XML doc that the application must register a real `ICurrentTenant` (typically via `Headless.Api.MultiTenancySetup`) for propagation to work. Consider adding a startup validator (deferred to a sibling issue). |
| Existing `ConsumeFilterTests.cs` may have assertions coupled to single-filter behavior. | U2 reviews the file and migrates affected tests; characterization-first execution note ensures changes are visible. |
| Multi-tenant integration tests across transports are not part of this plan — propagation correctness in a real RabbitMQ / Kafka topology is unverified by this PR. | U6 e2e tests use the in-memory transport (representative of the publish/consume surface). Per-transport integration tests for tenant propagation are out of scope for this issue but tracked as follow-up under #217's Phase-2 scope. |

---

## Documentation / Operational Notes

- All three doc surfaces (`docs/llms/messaging.md`, `docs/llms/multi-tenancy.md`, `src/Headless.Messaging.Core/README.md`) updated in the same PR per the transport-wrapper-drift learning.
- Post-implementation: capture three new entries in `docs/solutions/` via `/dev-compound`:
  1. Filter-vs-decorator rationale and the `IPublishFilter` design (messaging category).
  2. Multi-tenancy + AsyncLocal propagation across messaging boundaries (concurrency category).
  3. `TryAddEnumerable` vs `TryAddScoped` for multi-implementation interfaces (api category).
- No rollout / monitoring concerns — pure additive feature; existing transports unchanged.
- After merge, file the zad-ngo migration follow-up PR to delete the local `TenantPropagation*` copy and adopt the upstream version.

---

## Sources & References

- **Origin document:** [docs/brainstorms/2026-05-09-tenant-propagation-filters-requirements.md](../brainstorms/2026-05-09-tenant-propagation-filters-requirements.md)
- **Issue:** https://github.com/xshaheen/headless-framework/issues/235
- **Parent epic:** https://github.com/xshaheen/headless-framework/issues/217 (Messaging Phase 1 foundations)
- **Depends on:** https://github.com/xshaheen/headless-framework/issues/228 (TenantId envelope, shipped via PR #239)
- **Related:** #234 (EF write guard), #236 (Mediator behavior), #237 (ProblemDetails handler), #238 (strict-tenancy publish guard)
- **Precedent:** https://github.com/xshaheen/zad-ngo/pull/152 (FOUND-02 / U5 — local implementation to be migrated post-merge)
- **Key code:**
  - `src/Headless.Messaging.Core/IConsumeFilter.cs` (mirror template)
  - `src/Headless.Messaging.Core/Internal/IConsumeExecutionPipeline.cs` (pipeline mirror template)
  - `src/Headless.Messaging.Core/Internal/DirectPublisher.cs:35` (insertion point)
  - `src/Headless.Messaging.Core/Internal/OutboxPublisher.cs` (second insertion site)
  - `src/Headless.Messaging.Core/Configuration/MessagingBuilder.cs:127` (registration pattern)
  - `src/Headless.Messaging.Abstractions/PublishOptions.cs:75` (init-only constraint)
- **Institutional learnings:**
  - `docs/solutions/guides/messaging-transport-provider-guide.md` (4-case integrity policy)
  - `docs/solutions/messaging/transport-wrapper-drift-and-doc-sync.md` (doc co-update discipline)
  - `docs/solutions/concurrency/circuit-breaker-transport-thread-safety-patterns.md` (dispose discipline under cancellation)
