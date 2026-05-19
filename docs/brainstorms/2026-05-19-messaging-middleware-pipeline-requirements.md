---
date: 2026-05-19
topic: messaging-middleware-pipeline
---

# Messaging: Typed Middleware Pipeline (replace filter triad)

## Summary

Replace the current `IPublishFilter` / `IConsumeFilter` triad (executing/executed/exception callbacks) with a single typed-context middleware seam per direction — `IPublishMiddleware<TContext>` / `IConsumeMiddleware<TContext>` — using `next`-delegate semantics, `TContext` polymorphism (object-typed and typed-`T` on the same interface), two registration scopes (bus / per-(T, group)), and numeric-priority ordering. The framework runtime preserves the existing after-success log-and-suppress wrapper and the never-swallow-cancellation guarantee. Implementation uses fast reflection now (FastExpressionCompiler-cached typed dispatch); source-gen lands as a swap-in dispatcher in a separate track without changing the public API. Resolves the #218 "do we need a typed behavior layer?" question by making typed and object-typed needs the same seam.

---

## Problem Frame

The current filter pipeline (`src/Headless.Messaging.Core/IPublishFilter.cs`, `src/Headless.Messaging.Core/IConsumeFilter.cs`, `src/Headless.Messaging.Core/Internal/IPublishExecutionPipeline.cs`, `src/Headless.Messaging.Core/Internal/IConsumeExecutionPipeline.cs`) has well-thought-out semantics but five concrete ergonomic and shape problems for the work landing in P2 (#232 publisher intent split):

1. **Triad forces state on the filter instance.** `TenantPropagationConsumeFilter` holds an `IDisposable? _scope` field and disposes it in two paths (`OnSubscribeExecutedAsync`, `OnSubscribeExceptionAsync`), with defensive pre-assignment dispose and a four-line comment justifying it. A `next`-delegate model collapses this to `using var scope = currentTenant.Change(id); await next();` — one method, no field, no defensive logic.

2. **No pre-publish short-circuit primitive.** `PublishExceptionContext.ExceptionHandled` is a post-failure swallow, not a pre-publish skip. Consumers wanting pre-publish gating (feature-flag, dedup, denylist) must throw and catch in another filter — exception-as-control-flow.

3. **Object-typed `Content` + separate `Type MessageType`.** Filters must self-gate (`if (context.Content is T)`) and cast manually. Per-message-type filters are not first-class.

4. **Mutating publish state through `OptionsCore` re-exposure is awkward.** `PublishingContext.Options` is re-declared `new` to convert the read-only base into mutable, and the XML doc carries a 12-line warning that mutation only works in the executing phase and that assigning `null` discards caller-set fields. `next`-delegate semantics make this implicit — the value passed into `next(...)` is what flows downstream.

5. **Pure registration-order ordering does not scale.** With ~15 plausible middleware candidates across the framework (tenant, idempotency, outbox correlation, OTel, retry-count, dedup, schema-validate, encryption, compression, audit, rate-limit, tracing, baggage, deadlining, plus user middleware), "whoever called `AddPublishFilter` first wins" silently breaks when a new middleware is inserted.

The shape consensus across MassTransit, NServiceBus, Wolverine, Rebus, and MediatR (~570 words of research, this conversation) is that **all five use `next`-delegate / russian-doll, none use the MVC-style executing/executed/exception triad**. Headless is the outlier on shape. The triad is a UI-framework idiom; messaging frameworks converge on middleware/pipe shapes.

The cherry-picked design takes MassTransit's `TContext` polymorphism + scope-tier registration as the bone structure, with refinements borrowed from MediatR (typed context properties instead of a payload bag), NServiceBus (separate context *types* per phase for compile-time safety), and Wolverine (signature designed for future source-gen weaving). Anchor-relative ordering and endpoint-scope tier are deferred until the first-party middleware count makes them concrete.

---

## Actors

- A1. **Middleware author (framework consumer).** Writes a class implementing `IPublishMiddleware<TContext>` or `IConsumeMiddleware<TContext>` for application-level concerns (validation, idempotency, feature gates, audit).
- A2. **First-party middleware (Headless.Messaging.Core).** `TenantPropagationConsumeFilter`, `TenantPropagationPublishFilter`, and any future cross-cutting middleware shipped inside the framework.
- A3. **Provider package author.** Headless.Messaging.RabbitMq, .Nats, .Kafka, etc. — must not depend on the triad shape. Migrate any internal use of the filter pipeline.
- A4. **Source-gen track (future).** A later workstream replaces the FastExpressionCompiler-based typed dispatch with compile-time-generated invokers. Must not require breaking the middleware author-facing API.
- A5. **#232 publisher intent split.** Will extend `PublishContext<T>` into `SendContext<T>` and `BroadcastContext<T>` non-breakingly. Must not be blocked by this RFC's shape decisions.

---

## Key Flows

- F1. **Author writes a typed consume middleware.**
  - **Trigger:** Application needs to enforce `OrderPlaced` idempotency keyed on `MessageId`.
  - **Actors:** A1.
  - **Steps:** Implement `IConsumeMiddleware<ConsumeContext<OrderPlaced>>`. Register at per-(T, group) scope. Inside `InvokeAsync(context, next)`, check the dedup store using `context.MessageId`. On hit, return without calling `next` (short-circuit). On miss, record the id and `await next()`.
  - **Outcome:** Duplicate `OrderPlaced` deliveries skip the handler without throwing or relying on exception flow.
  - **Covered by:** R1, R3, R7.

- F2. **Author writes an object-typed cross-cutting middleware.**
  - **Trigger:** Add structured audit logging for every published message regardless of type.
  - **Actors:** A1.
  - **Steps:** Implement `IPublishMiddleware<PublishContext>` (no `T`). Register at bus scope. Read `context.MessageType`, `context.Options`, headers from the context. Call `await next()`. After return, log success; on exception, log failure and rethrow.
  - **Outcome:** Single middleware fires for every publish, no per-type registration, AOT-clean (no `MakeGenericType` involved).
  - **Covered by:** R1, R4.

- F3. **Author orders middleware with numeric priority.**
  - **Trigger:** A new audit middleware must run AFTER tenant propagation so audit records carry the resolved tenant id.
  - **Actors:** A1.
  - **Steps:** Register with `.WithPriority(200)` where `TenantPropagationConsumeMiddleware` is `.WithPriority(100)`. Lower priority runs first (outer ring on the way in, inner ring on the way back). The framework sorts within each scope by ascending priority; ties resolve in registration order.
  - **Outcome:** Ordering is explicit and survives library composition; no anchor type needs to be referenced.
  - **Covered by:** R6, R8.

- F4. **First-party middleware migrates from triad.**
  - **Trigger:** `TenantPropagationConsumeFilter` is rewritten as `TenantPropagationConsumeMiddleware`.
  - **Actors:** A2.
  - **Steps:** Replace three callback methods with one `InvokeAsync`. Replace `_scope` field + defensive double-dispose with `using var scope = ...; await next();`. Delete the four-line defensive comment.
  - **Outcome:** First-party middleware is shorter, has no mutable state across the lifecycle, and serves as the canonical example for downstream consumers.
  - **Covered by:** R10, R14.

- F5. **Saga compensation across `next()`.**
  - **Trigger:** A consume middleware reserves capacity in an inventory store before the handler runs; if the handler throws, the reservation must be released.
  - **Actors:** A1.
  - **Steps:** Inside `InvokeAsync`, acquire the reservation. Wrap `await next()` in a try/catch. On success, return normally. On exception, release the reservation and rethrow. No framework-level compensation registry needed — the russian-doll shape gives each middleware its own compensation scope.
  - **Outcome:** Compensation is expressed inline in user code without a separate exception phase. Multiple middleware compose naturally — each compensates on its own catch.
  - **Covered by:** R1, R5.

- F6. **Retry-with-state around `next()`.**
  - **Trigger:** A consume middleware retries transient failures up to N times with backoff, observing each attempt.
  - **Actors:** A1.
  - **Steps:** Inside `InvokeAsync`, loop calling `await next()` in a try/catch. On retryable exception types, increment an attempt counter, await a delay, and continue the loop. Per-attempt timeout uses a linked `CancellationTokenSource` derived from `context.CancellationToken`; downstream middleware must read cancellation from `context.CancellationToken`, which the retry middleware swaps for the per-attempt token via a context method (`context.WithCancellationToken(...)`).
  - **Outcome:** Retry policy is a single middleware. Per-attempt timeouts propagate to downstream middleware through the context.
  - **Covered by:** R1, R4.

- F7. **Error-policy chain with explicit ownership.**
  - **Trigger:** Multiple middleware want to handle different exception classes — one routes `ValidationException` to a DLQ; another swallows `TransientPublishException` for retry; the rest propagate.
  - **Actors:** A1.
  - **Steps:** Each middleware wraps `await next()` in a try/catch typed to its own exception class. Catches that don't match rethrow naturally. Order matters: outer middleware sees exceptions inner middleware did not catch. The composition policy is registration order (with numeric priority), not framework configuration.
  - **Outcome:** Error policy is expressed as ordinary structured exception handling. No `ExceptionHandled` flag, no shared error registry, no exception-as-control-flow anti-pattern.
  - **Covered by:** R1, R5.

---

## Requirements

**Interface shape**
- R1. Introduce `IPublishMiddleware<TContext>` and `IConsumeMiddleware<TContext>` as separate interface families, each with a single method `ValueTask InvokeAsync(TContext context, Func<ValueTask> next)`. The two interfaces do not share a base type; publish and consume have different lifecycles, different context shapes, and different scope hierarchies.
- R2. `TContext` is constrained such that the publish family accepts publish-shaped contexts and the consume family accepts consume-shaped contexts. Concretely: `IPublishMiddleware<TContext> where TContext : PublishContext` and `IConsumeMiddleware<TContext> where TContext : ConsumeContext`.
- R3. The same `IConsumeMiddleware<TContext>` interface supports both object-typed middleware (`IConsumeMiddleware<ConsumeContext>`) and typed middleware (`IConsumeMiddleware<ConsumeContext<OrderPlaced>>`) via `TContext` polymorphism. There is no second seam for cross-cutting concerns. Typed `TContext` is only valid at per-(T, group) scope (R7).
- R4. Cancellation is exposed on the context (`context.CancellationToken`), not on the `next` delegate. `next` is `Func<ValueTask>` with no parameters. Middleware that needs to scope cancellation downstream (e.g., per-attempt retry timeout) uses `context.WithCancellationToken(...)` to swap the token before invoking `next`.
- R5. Exception semantics inside the middleware body are standard try/catch. There is no `ExceptionHandled` flag on any context. Middleware that wants to swallow an inner failure does so by catching around `await next()` and returning normally. The pipeline runtime preserves two guarantees from the current filter pipeline: (a) middleware code running *after* a successful `await next()` is wrapped in a runtime catch-and-log so a post-success middleware throw does not propagate to the publish caller (preventing duplicate publishes when audit/log middleware fails after the outbox commits); (b) `OperationCanceledException` is never silently swallowed — if a middleware catches OCE and returns normally, the runtime detects this via the context's cancellation state and rethrows. Both guarantees are framework-enforced, not author responsibility.

**Registration scopes**
- R6. Middleware is registered at one of two scopes: **bus** (fires for every publish/consume on the bus) or **per-(T, group)** where `group` corresponds to the existing `ConsumerExecutorDescriptor.GroupName`. The composition is bus → per-(T, group), each scope wrapping the next on the consume side; publish-side equivalents wrap publisher invocation. Endpoint scope is deferred to a follow-on issue until a concrete need materializes (no first-party middleware in this RFC requires it).
- R7. Per-(T, group) scope on the consume side accepts `IConsumeMiddleware<ConsumeContext<T>>` and runs only for consumers matching both `T` and the named group. Bus scope accepts only object-typed `TContext` (`IConsumeMiddleware<ConsumeContext>` / `IPublishMiddleware<PublishContext>`). Registering a typed middleware (e.g., `IConsumeMiddleware<ConsumeContext<OrderPlaced>>`) at bus scope is a startup `MessagingConfigurationException` naming the misuse — typed dispatch requires per-(T, group) scope.

**Ordering**
- R8. Each scope has an independent ordering list. Middleware authors specify ordering using numeric priority: `.WithPriority(int)`. Lower priority runs first (outer ring). Ties resolve in registration order. Default priority is `0` for user middleware; first-party middleware uses documented constants (e.g., `TenantPropagationPriority = -1000`) so user middleware naturally runs after framework middleware unless explicitly placed before.
- R9. Anchor-relative ordering (`.Before<X>()` / `.After<X>()`) is deferred to a follow-on issue until first-party middleware count makes it concrete. Numeric priority covers the v1 case; anchor ordering can be added non-breakingly as a richer alternative when needed.

**Context types**
- R10. The current `PublishingContext` / `PublishedContext` / `PublishExceptionContext` triad collapses into two context types: `PublishingContext<T>` (mutable; visible to middleware before `await next()` is invoked) and `PublishedContext<T>` (read-only; visible to middleware after `await next()` returns successfully). The split preserves the existing NServiceBus-style compile-time guarantee that `Options` and `DelayTime` cannot be mutated after the publish has happened. Middleware authored against `PublishingContext<T>` sees `Options` and `DelayTime` as mutable on the way in; the same middleware sees `PublishedContext<T>` (a different type with read-only properties) when control returns from `next()`. The pipeline constructs `PublishedContext<T>` from `PublishingContext<T>` at the inner-ring boundary.
- R12. The consume side requires unsealing the existing `ConsumeContext<T>` record and introducing a non-generic `ConsumeContext` base class. Today `ConsumeContext<TMessage>` is `public sealed record ... where TMessage : class` (`src/Headless.Messaging.Abstractions/ConsumeContext.cs`); the new shape removes `sealed`, chooses class-with-records-disabled or non-sealed-record, and either duplicates members on the base or refactors the typed record to inherit from it. The non-generic `ConsumeContext` exposes the same fields with `Message` typed as `object?` and `MessageType` typed as `Type`. This is a structural change to the public API surface, not a non-event.

**Migration of first-party middleware**
- R13. `TenantPropagationPublishFilter` becomes `TenantPropagationPublishMiddleware` implementing `IPublishMiddleware<PublishContext>`. The new shape uses a single `InvokeAsync` with a local `using` for any disposable scope.
- R14. `TenantPropagationConsumeFilter` becomes `TenantPropagationConsumeMiddleware` implementing `IConsumeMiddleware<ConsumeContext>`. The `_scope` field, defensive double-dispose, and the multi-callback dispose discipline are removed.

**Registration API**
- R15. `MessagingBuilder` exposes scope-aware registration methods replacing `AddPublishFilter<T>` / `AddSubscribeFilter<T>`:
  - `AddBusPublishMiddleware<T>()` / `AddBusConsumeMiddleware<T>()` — bus scope; `T` must be an object-typed middleware (R7).
  - `AddPublishMiddlewareFor<TMiddleware, TMessage>()` — typed publish middleware. Publish has no consumer-group concept, so per-`T` is the finest publish-side scope.
  - `AddConsumeMiddlewareFor<TMiddleware, TMessage>(string group)` — typed consume middleware scoped to `(TMessage, group)`. Group is required because the consume side has multiple handlers per `T` distinguished by group.
  - Each registration returns a fluent handle supporting `.WithPriority(int)`.

**Compatibility**
- R20. There is no compatibility shim for `IPublishFilter` / `IConsumeFilter`. The interfaces are deleted. Greenfield project; no deployed consumers. Internal migration scope includes:
  - First-party middleware: `TenantPropagationPublishFilter`, `TenantPropagationConsumeFilter` (R13, R14).
  - Tests: `ConsumeFilterPipelineTests.cs` (~9 fixture types encoding triad semantics — must be rewritten or removed), `PublishedContextIsTransactionalTests.cs` (4 test methods using `AddPublishFilter<...>`), `MessagingBuilderTests.cs` (registration-API surface tests).
  - Setup: `SetupMessagingTenancy.cs` (registers the two tenant filters via `TryAddEnumerable`).
  - Provider packages: confirmed no provider package implements `IPublishFilter` / `IConsumeFilter` directly; only `Headless.Messaging.Core` references them.

---

## Acceptance Examples

- AE1. **Covers R3, R5.** Given a typed `IConsumeMiddleware<ConsumeContext<OrderPlaced>>` that performs idempotency dedup registered at per-(T, group) scope, when a duplicate `OrderPlaced` arrives and the middleware returns without calling `next`, the consumer handler is not invoked, no exception propagates, and the message is acked normally.
- AE2. **Covers R5.** Given a consume middleware that catches `OperationCanceledException` after `await next()` and returns normally, when the inner consumer body cancels, the runtime detects the swallowed cancellation via `context.CancellationToken.IsCancellationRequested` and rethrows OCE up the chain — the swallow is not honored.
- AE2b. **Covers R5.** Given an audit middleware that throws `ObjectDisposedException` after `await next()` returns successfully (e.g., a logging scope was disposed), the framework runtime catches and logs the post-success throw; `IMessagePublisher.PublishAsync` returns successfully to the caller; the outbox row is not duplicated by caller retry.
- AE3. **Covers R6, R7.** Given an `IConsumeMiddleware<ConsumeContext<OrderPlaced>>` registered at per-(T, group) scope with group `"checkout-handler"`, when `OrderPlaced` is delivered to a consumer in group `"reporting"`, the middleware does not fire; when delivered to `"checkout-handler"`, it fires.
- AE3b. **Covers R7.** Given a registration call `AddBusConsumeMiddleware<MyTypedMw>()` where `MyTypedMw : IConsumeMiddleware<ConsumeContext<OrderPlaced>>`, at application startup the host throws `MessagingConfigurationException` naming `MyTypedMw` and the misuse: typed middleware must be registered at per-(T, group) scope.
- AE4. **Covers R8.** Given `TenantPropagationConsumeMiddleware` with priority `-1000` and `AuditConsumeMiddleware` with priority `200`, when a message arrives, tenant propagation runs first on the way in and last on the way out; audit middleware sees the resolved tenant id when it logs.
- AE5. **Covers R10.** Given a publish middleware mutates `context.Options.Headers` before `await next()` and tries to mutate them again in code after `next()` returns, the post-`next` mutation fails to compile because the type the middleware now sees is `PublishedContext<T>` with read-only `Options`.
- AE6. **Covers R14.** Given the rewritten `TenantPropagationConsumeMiddleware` runs and the handler throws, the tenant scope is disposed before the exception propagates out of the middleware (because `using` is unwound), without any field-based dispose discipline.
- AE7. **Covers F5 (saga compensation), R5.** Given a consume middleware reserves inventory before `await next()` and catches the handler's exception to release the reservation, when the handler throws, the release runs and the exception propagates with the original stack trace intact.
- AE8. **Covers F6 (retry-with-state), R4.** Given a retry middleware calls `context.WithCancellationToken(perAttemptCts.Token)` before each `await next()`, downstream middleware reading `context.CancellationToken` sees the per-attempt token; cancelling the per-attempt CTS aborts only that attempt, not the overall consume operation.

---

## Success Criteria

- A downstream consumer can implement either an object-typed cross-cutting middleware or a typed per-`T` middleware using the same interface family, distinguishing only by the `TContext` parameter, and the choice of typing is the only registration concern.
- `TenantPropagationConsumeMiddleware` is shorter than the original `TenantPropagationConsumeFilter` (~30 lines vs ~90), has no mutable state across the lifecycle, and is structurally identical in shape to a user-written middleware.
- Adding a new first-party middleware between two existing ones is a one-line registration call with explicit numeric priority; the change does not silently reorder unrelated middleware.
- #232's `ISendPublisher` / `IBroadcastPublisher` split lands without modifying the `IPublishMiddleware<TContext>` interface or any middleware authored against `PublishContext<T>`.
- A reader of the messaging docs can decide which scope and `TContext` to use for a new concern in under a minute by following one decision tree, not two.
- The source-gen track, when it lands, can replace the runtime dispatcher without touching `IPublishMiddleware<TContext>`, `IConsumeMiddleware<TContext>`, `PublishingContext<T>`, `PublishedContext<T>`, `ConsumeContext<T>`, or any registration extension. Source-gen is observable only as a perf and AOT improvement.
- **Consumer-observable:** A new user landing on the messaging README and given the goal "write an idempotency middleware for `OrderPlaced`" produces working code on first try within ~10 minutes, including registration. (Validated via README walkthrough or AI-generated-snippet correctness check.)
- **Consumer-observable:** A developer migrating from MassTransit can map MassTransit's `IFilter<ConsumeContext<T>>` to `IConsumeMiddleware<ConsumeContext<T>>` and its `cfg.UseConsumeFilter` to `AddConsumeMiddlewareFor<...>(group)` without reading the full requirements doc — concept overlap is high enough that a one-page migration table suffices.

---

## Scope Boundaries

- Source-gen implementation of typed dispatch (separate track, post-#232).
- Replacing the existing AOT debt in `_BuildConsumeContext` and `_DispatchAsync` (covered by the source-gen track).
- Wolverine-style convention methods (plain `Before(...)` / `After(...)` methods discovered by name) — explicit interfaces only.
- Publisher intent split (send/broadcast) — #232 owns this; this RFC commits to the extension point as a design constraint (see Dependencies / Assumptions).
- Outbox / inbox redesign — #232 owns this. `IsTransactional` and other outbox-context surfacing belong to #232's context-hierarchy extension.
- OTel restructuring — already settled via `IActivityTagEnricher` (#275, merged 2026-05-14).
- Anchor-relative ordering (`.Before<X>()` / `.After<X>()`) — deferred to a follow-on issue (R9). Numeric priority covers v1.
- Endpoint registration scope — deferred to a follow-on issue. Two scopes (bus + per-(T, group)) cover v1.
- Roslyn analyzer enforcing "don't catch `OperationCanceledException`" — convention only in v1, analyzer deferred. Runtime guarantee (R5) covers correctness.
- Renaming the existing `Headless.Messaging.Configuration.MessagingBuilder.AddSubscribeFilter` / `AddPublishFilter` extensions for compatibility — they are deleted, not renamed.

---

## Key Decisions

- **One seam per direction, not two.** Five-framework research (MassTransit, NServiceBus, Wolverine, Rebus, MediatR) shows zero precedent for two parallel registration seams. All five converge on one seam with variation in `TContext` polymorphism, stages, scope, or selective application. Adopting MassTransit's `TContext` polymorphism + scope-tier registration as the baseline.
- **Cherry-picked over literal MassTransit.** Adopt MassTransit's `TContext` and scope-tier registration, but use MediatR-style typed context properties (no `GetPayload<T>()` bag), NServiceBus's separate context types per phase where it adds compile-time safety (`PublishingContext<T>` vs `PublishedContext<T>`), and a Wolverine-friendly signature so a future source-gen track can inline the chain without API changes. Anchor-relative ordering and endpoint scope are deferred until concrete needs materialize.
- **Separate `IPublishMiddleware` and `IConsumeMiddleware` (not a shared `IMessageMiddleware`).** Publish and consume have different context shapes (Options/DelayTime vs TenantId/Result/Exception), different scope hierarchies in #232, and different first-party concerns. Sharing a base would force `TContext` to be a meaningless join type. All five frameworks separate the directions; Headless follows.
- **Cancellation on context, not on `next`.** Four-of-four consensus among the messaging frameworks (MassTransit, NServiceBus, Rebus, ASP.NET Core). MediatR's separate-`ct`-parameter shape is the outlier. Context-carried ct allows runtime to inject timeouts or composite tokens without changing the public API.
- **Standard try/catch exception semantics plus two framework-enforced guarantees.** Middleware bodies use ordinary try/catch (matches all four messaging frameworks). Two correctness properties from the current pipeline are preserved at the runtime layer, not as author discipline: (a) post-success middleware throws are caught and logged so they don't trigger caller retry / duplicate publishes; (b) `OperationCanceledException` is never silently swallowed — runtime detects swallowed OCE via cancellation-state check and rethrows.
- **Numeric priority for v1; defer anchor-relative ordering.** Two first-party middleware ship in v1 (the two tenant filters). Numeric priority with documented framework constants covers ordering needs. Anchor-relative ordering (`.Before<X>()`) is a richer alternative that can be added non-breakingly when first-party middleware count makes it concrete.
- **Two scopes for v1 (bus + per-(T, group)); defer endpoint scope.** No first-party middleware in this RFC requires endpoint-level granularity. Headless's transport model has topic + group, not endpoint. Adding endpoint scope from MassTransit verbatim before a concrete need is identified would invent a new abstraction.
- **Split context (`PublishingContext<T>` vs `PublishedContext<T>`), not single mutable.** Preserves the existing compile-time guarantee that `Options` and `DelayTime` cannot be mutated after publish. Middleware that needs to read post-publish state sees a different type with read-only properties. Aligns with NServiceBus's stage-types-as-safety pattern.
- **Name "middleware", not "behavior" or "filter".** Avoids collision with `Headless.Mediator.Behaviors` (request/response shape, different concept). Matches ASP.NET Core vocabulary your users already have. "Filter" carries the MVC-triad connotation we're explicitly leaving behind.
- **Fast reflection now, source-gen later, no public-API change between the two.** The consume pipeline already isn't AOT-clean (`FastExpressionCompiler` + `MakeGenericMethod`); adding typed-middleware dispatch via the same FastExpressionCompiler pattern doesn't regress consume-side AOT posture. Publish-side regresses slightly (publish was AOT-clean) but the source-gen track resolves both. Typed consume-side dispatch uses the existing `ConditionalWeakTable<Type, Delegate>` + `FastExpressionCompiler.CompileFast()` cache pattern (same as `ConsumeExecutionPipeline._BuildConsumeContext`).

---

## Dependencies / Assumptions

- **Forward-compat with #232 (design constraint, not a deliverable):** `PublishingContext<T>` and `PublishedContext<T>` are designed as the bases of a hierarchy that #232 may extend with `SendContext<T>`, `BroadcastContext<T>`, and outbox-aware context surfacing (`IsTransactional`) without breaking middleware authored against `IPublishMiddleware<PublishingContext<T>>`. Middleware authored against the base continues to fire for both send and broadcast.
- **Source-gen API-stability constraint (design constraint, not a deliverable):** the public middleware API surface — interface signatures, registration extensions, context types — is designed to remain stable when the source-gen track replaces the runtime dispatcher. Validating this requires a generator sketch during planning; see Outstanding Questions.
- Assumes the source-gen track will eventually land; this is not formally scheduled. If it never lands, the framework retains its current AOT posture (consume side not AOT-clean; publish side regresses from AOT-clean to not-AOT-clean for typed publish middleware) — flagged for downstream tracking.
- Assumes the FastExpressionCompiler dependency in `Headless.Messaging.Core.csproj` remains acceptable for the lifetime of v1. The source-gen track is expected to remove it.
- Assumes `ConsumerExecutorDescriptor.GroupName` remains the per-consumer scoping identifier on the consume side. Verified to exist in `src/Headless.Messaging.Core/Internal/IConsumeExecutionPipeline.cs:78`.
- Assumes greenfield posture (no deployed consumers of `IPublishFilter` / `IConsumeFilter`). Per project memory; confirmed.

---

## Outstanding Questions

### Resolve Before Planning

- [Affects R15][User decision] Final registration method naming. Options: `AddBusConsumeMiddleware<T>()` (verbose, explicit) vs `AddConsumeMiddleware<T>()` with scope as a builder method (`.AtBusScope()`, `.ForGroup<T>("g")`).

### Deferred to Planning

- [Affects R8][Technical] Numeric priority registration: should priorities be open-ended `int` or constrained to documented bands (e.g., `[-1000, 1000]` with reserved framework ranges)?
- [Affects R10][Technical] How `PublishingContext<T>` → `PublishedContext<T>` transition is implemented: same instance with `as`-pattern API, separate instances with copy-construct, or some other mechanism. Pick during planning.
- [Affects R20][Technical] Whether the FastExpressionCompiler-cached invoker per middleware should be a per-pipeline cache or share the existing `_compiledConsumeContextFactories` table. Existing table is keyed on message type; middleware dispatch is keyed on (middleware type, message type) — likely a separate table.
- [Source-gen prerequisite][Needs research] Confirm that the proposed interface signatures emit cleanly from a Roslyn incremental generator without breaking user-observable `next` semantics (Func<ValueTask> as a parameter is heap-observable; Wolverine sidesteps by not exposing it as a delegate). Validate by sketching a generator stub. **This is a planning blocker, not a planning artifact** — if the sketch reveals a fundamental obstruction, the source-gen stability constraint must be relaxed before R10 and R15 lock.

### From 2026-05-19 review (strategic, user judgment)

- [Affects Problem Frame] Premise rests on five-framework shape research, not a documented consumer-pain signal. The motivating problems are concrete (1-5 in Problem Frame) but framed from the framework author's perspective. For the "AI-first / indispensable to .NET devs" goal, an explicit consumer-pain paragraph (or honest acknowledgment that this is internal-API refactoring) would strengthen the framing. *(product-lens, anchor 75)*
- [Affects Key Decisions] Cherry-picking from five sources may raise cognitive load instead of lowering it. A dev coming from MassTransit / NServiceBus / Wolverine sees something familiar + three things that aren't. AI training corpora and Stack Overflow answers will match no single framework cleanly. Alternative: pick the closest single framework (likely MassTransit) and match its idioms faithfully; document deliberate divergences as exceptions, not as the design. *(product-lens, anchor 75)*
- [Affects Problem Frame] Opportunity cost vs #232: this RFC commits to but doesn't implement #232's extension point. Has the existing filter pipeline been shown unable to accept #232 non-breakingly? If not, sequencing pipeline reshape before #232 may defer user-facing capability for an internal refactor. *(product-lens, anchor 75)*
- [Affects Problem Frame, Key Decisions] "Five-framework consensus" claim conflates middleware shape (russian-doll, on which all agree) with cancellation/exception semantics (on which they materially diverge). MassTransit's `TContext` is not "object-typed vs typed-T on the same interface" — MassTransit has distinct context families. The decisions may still be correct, but the cited consensus is overstated. Worth restating as "synthesis informed by five frameworks" rather than "consensus." *(adversarial, anchor 75)*
- [Affects R6, R7, R15] Rename "filter" → "middleware" churns existing user vocabulary (READMEs, snippets, AI training data) with collision-avoidance and triad-disassociation rationale that is author-felt, not consumer-felt. Alternative: keep "filter" as the word and change only the shape. *(product-lens, anchor 75)*
- [Affects R3, R7] TContext polymorphism is clever but a learning cliff: same interface, different `T`, different fire semantics depending on registration scope. Two clearer alternatives: (a) one object-typed interface with a typed adapter base class, or (b) two separate interfaces (object-typed and typed) with explicit names. *(product-lens, anchor 75)*
