---
date: 2026-05-19
type: feat
status: active
origin: docs/brainstorms/2026-05-19-messaging-middleware-pipeline-requirements.md
---

# feat: Typed middleware pipeline for messaging (replace filter triad)

## Summary

Replace `IPublishFilter` / `IConsumeFilter` (executing/executed/exception triad) with `IPublishMiddleware<TContext>` / `IConsumeMiddleware<TContext>` russian-doll middleware across `Headless.Messaging.Core`. The work lands as six sequential units: (U1) contracts and unsealed `ConsumeContext`, (U2) runtime pipelines with the three R5 framework guarantees — (a) token-identity OCE detection via recursive `AggregateException` walk, (b) post-inner-ring log-and-suppress (only after `innerRingCompleted == true`) using `CancellationToken.None`, (c) post-return cancellation re-check that rethrows OCE when middleware swallowed outer cancellation undetected, (U3) registration API plus FluentValidation startup rule, (U4) first-party tenant middleware, (U5) atomic swap split into three bisectable commits (5a publishers, 5b deletions, 5c test migration), (U6) docs sync. Greenfield deletion of all old types; no compatibility shim.

---

## Problem Frame

`IPublishFilter`/`IConsumeFilter`'s executing/executed/exception triad forces filter authors to carry state on instance fields (e.g., `TenantPropagationConsumeFilter._scope` plus defensive double-dispose) and provides no pre-publish short-circuit primitive. Ordering is implicit by registration; typed `T` access requires casts. The codebase is the only consumer of these interfaces (verified: zero provider packages reference them), greenfield posture allows clean deletion. (see origin: `docs/brainstorms/2026-05-19-messaging-middleware-pipeline-requirements.md`)

---

## Requirements Trace

Every brainstorm requirement maps to one or more implementation units.

| Origin | Covered by |
|---|---|
| R1 (separate `IPublishMiddleware<TContext>` / `IConsumeMiddleware<TContext>` interface families) | U1 |
| R2 (constraint `TContext : PublishContext` / `ConsumeContext`) | U1 |
| R3 (`TContext` polymorphism — object-typed and typed-T on the same interface) | U1 |
| R4 (cancellation on context, `Func<ValueTask>` next, `context.WithCancellationToken(...)`) | U1, U2 |
| R5 (try/catch + token-identity OCE guarantee + post-inner-ring log-and-suppress + post-return OCE re-check) | U2 |
| R6 (two scopes: bus + per-(T, group); endpoint deferred) | U3 |
| R7 (typed only at per-(T, group); typed-at-bus is startup error) | U3 |
| R8 (numeric priority, `public const int Priority` on middleware classes, ties resolve in registration order) | U3, U4 |
| R9 (anchor-relative ordering deferred — out of scope) | n/a |
| R10 (mutable pre-`next`, runtime-enforced read-only post-`next` via `_isCompleted` flag) | U1, U2 |
| R12 (unseal `ConsumeContext<TMessage>` keeping `record class`, introduce non-generic `ConsumeContext` base) | U1 |
| R13 (`TenantPropagationPublishMiddleware`) | U4 |
| R14 (`TenantPropagationConsumeMiddleware`) | U4 |
| R15 (`MessagingBuilder` scope-aware registration methods + descriptor registry) | U3 |
| R20 (no compat shim; enumerate migration surface) | U5 |

> **Footnote on requirement IDs:** R11, R17, R18, R19 were removed during the origin requirements-doc review (content folded into Dependencies / Key Decisions in the origin doc). R16 was never assigned — gap in origin numbering. The trace above is complete with respect to surviving R-IDs.

**Origin actors:** A1 (Middleware author), A2 (First-party middleware), A3 (Provider package author), A4 (Source-gen track), A5 (#232 publisher intent split)

**Origin flows:** F1 (typed consume middleware), F2 (object-typed cross-cutting), F3 (numeric-priority ordering), F4 (first-party migration), F5 (saga compensation), F6 (retry-with-state), F7 (error-policy chain)

**Origin acceptance examples:** AE1 (covers R3, R5), AE2 (covers R5), AE2b (covers R5), AE3 (covers R6, R7), AE3b (covers R7), AE4 (covers R8), AE5 (covers R10), AE6 (covers R14), AE7 (covers F5, R5), AE8 (covers F6, R4)

---

## Scope Boundaries

- Source-gen implementation of typed dispatch — separate track post-#232.
- Replacing existing AOT debt in `_BuildConsumeContext` / `_DispatchAsync` — covered by the source-gen track.
- Anchor-relative ordering (`.Before<X>()` / `.After<X>()`) — deferred until first-party middleware count makes it concrete.
- Endpoint registration scope — deferred until a concrete consumer need materializes.
- Publisher intent split (send/broadcast) — #232 owns this; this plan commits to the `PublishContext<T>` extension point only.
- Outbox/inbox redesign and outbox-aware context surfacing (`IsTransactional`) — #232 owns.
- OTel restructuring — already settled via `IActivityTagEnricher` (#275).
- Roslyn analyzer enforcing "don't catch `OperationCanceledException`" — runtime guarantees (R5) cover correctness; analyzer deferred.
- Wolverine-style convention methods (plain `Before()` / `After()`) — explicit interfaces only.

### Deferred to Follow-Up Work

- Source-gen sketch validating R19's "API stable across swap" claim — separate planning track (acknowledged in origin Outstanding Questions).

---

## Output Structure

```
src/Headless.Messaging.Abstractions/
  ConsumeContext.cs                                    (modify: unseal — keep record class, add non-generic record class base)

src/Headless.Messaging.Core/
  IPublishMiddleware.cs                                (new)
  IConsumeMiddleware.cs                                (new)
  PublishContext.cs                                    (new — base + PublishingContext<T> with internal _isCompleted flag for R10)
  Configuration/
    MessagingBuilder.cs                                (modify: delete filter methods, add middleware methods, host descriptor registry)
    MiddlewareRegistration.cs                          (new — fluent .WithPriority handle returning MessagingBuilder)
    MessagingOptions.cs                                (modify — extend inline `MessagingOptionsValidator` at MessagingOptions.cs:412
                                                       with typed-at-bus startup rule; validator reads descriptors via injected
                                                       IMiddlewareDescriptorRegistry — see Key Technical Decisions)
  MessagingConfigurationException.cs                   (new if absent — startup error type)
  Internal/
    PublishMiddlewarePipeline.cs                       (new — replaces IPublishExecutionPipeline; contains inline private static
                                                       _ShouldRethrowOce token-identity helper, no separate guard file)
    ConsumeMiddlewarePipeline.cs                       (new — replaces IConsumeExecutionPipeline; same inline helper)
    MiddlewareDispatchKey.cs                           (new — readonly struct (MiddlewareType, MessageType) key for the dispatch cache)
  MultiTenancy/
    TenantPropagationPublishMiddleware.cs              (new — replaces TenantPropagationPublishFilter; declares `public const int Priority = -1000`)
    TenantPropagationConsumeMiddleware.cs              (new — replaces TenantPropagationConsumeFilter; declares `public const int Priority = -1000`)
  Setup.cs                                             (modify: register middleware pipelines, IMiddlewareDescriptorRegistry)
  SetupMessagingTenancy.cs                             (modify: AddTenantPropagationServices now takes a MessagingBuilder
                                                       parameter so middleware registration threads through the builder)

DELETED in U5b:
  src/Headless.Messaging.Core/IPublishFilter.cs
  src/Headless.Messaging.Core/IConsumeFilter.cs
  src/Headless.Messaging.Core/Internal/IPublishExecutionPipeline.cs
  src/Headless.Messaging.Core/Internal/IConsumeExecutionPipeline.cs (consume-context factory moves into ConsumeMiddlewarePipeline)
  src/Headless.Messaging.Core/MultiTenancy/TenantPropagationPublishFilter.cs
  src/Headless.Messaging.Core/MultiTenancy/TenantPropagationConsumeFilter.cs

NOT INTRODUCED (deliberately not separate files):
  OceCancellationGuard.cs   — single private static method inline in each pipeline; no shared file or dedicated test class.
  MiddlewarePriorities.cs   — framework priority constants live as `public const int Priority` on the middleware classes themselves.
```

The new file shape mirrors current conventions (one type per file, `Internal/` for non-public, `Configuration/` for builder surface, `MultiTenancy/` for tenant concerns). Tree is a scope declaration — implementer may consolidate or split if implementation reveals a better layout; per-unit `**Files:**` sections remain authoritative.

---

## High-Level Technical Design

*This section illustrates the intended approach and is directional guidance for review, not implementation specification. The implementing agent should treat it as context, not code to reproduce.*

**Russian-doll composition (consume side, shape is symmetric for publish):**

```
ConsumeMiddlewarePipeline.ExecuteAsync(messageInstance, messageType, ct)
  ├── CreateAsyncScope (per-message DI scope, matches today's pattern)
  ├── Build ConsumeContext<TMessage> via FastExpressionCompiler factory
  │     (existing _compiledConsumeContextFactories table reused)
  ├── Compose chain: [bus middleware (priority-sorted)] → [per-(T, group) middleware (priority-sorted)] → handler
  │     Each middleware sees the inner ring as a Func<ValueTask> next closure
  └── Invoke outermost middleware, propagate via russian-doll

Per-middleware invariants enforced by the pipeline (R5):
  Let innerRingCompleted = false initially. Set to true ONLY when the handler/publisher
  inner ring returned successfully (handler invoke awaited without throwing).

  await middleware.InvokeAsync(context, next: async () => { await invokeNextInner(); /* on success */ });

  After the await returns to the wrapping pipeline closure:
    (a) **Post-return OCE re-check (R5(c)):**
        if context.CancellationToken.IsCancellationRequested AND middleware returned normally,
        throw new OperationCanceledException(context.CancellationToken). Catches middleware
        that swallowed an outer cancellation undetected (e.g., retry middleware catching OCE
        from a per-attempt token without re-checking the outer context token).
    (b) **Token-identity AggregateException unwrap (R5(b)):**
        if the middleware caught and re-threw something, the pipeline checks the thrown
        exception. Recursive walk of AggregateException.InnerExceptions: if any inner OCE has
        .CancellationToken == context.CancellationToken AND the exception eventually rethrown
        is something else, propagate as OCE instead.
    (c) **Post-inner-ring log-and-suppress (R5(a)) — narrow scope:**
        if innerRingCompleted == true AND middleware threw on the way back out,
        log via _log.LogXxx(CancellationToken.None) and swallow. The inner work already
        succeeded; a post-success throw must not cause caller retry or duplicate publish.
        If innerRingCompleted == false (middleware threw on the way in, OR inner ring failed),
        propagate normally — the failure is real.
```

**Dispatch table for typed consume middleware:**

```
ConcurrentDictionary<MiddlewareDispatchKey, Delegate> _typedMiddlewareInvokers
  where MiddlewareDispatchKey = readonly struct { Type MiddlewareType, Type MessageType }

Note: ConcurrentDictionary, NOT ConditionalWeakTable. CWT requires TKey : class; the dispatch
key is a struct. The dictionary holds compiled invoker delegates keyed on closed-generic Type
pairs, both of which already live for the process lifetime, so weak-keying provides no GC win.

On first dispatch for (MwT, MsgT):
  Compile Expression.Lambda<Func<MwT, ConsumeContext<MsgT>, Func<ValueTask>, ValueTask>>
    using FastExpressionCompiler.CompileFast()
  Cache via GetOrAdd((MiddlewareType, MessageType))

Subsequent dispatches: O(1) lookup → invoke compiled delegate.

Separate from existing _compiledConsumeContextFactories (Type → Delegate for ConsumeContext
construction) — different key, different value shape, same caching discipline.
```

**Two-scope registration with priority:**

```
Bus scope:
  IPublishMiddleware<PublishContext>   (object-typed only, R7)
  IConsumeMiddleware<ConsumeContext>   (object-typed only, R7)

Per-(T, group) scope:
  IConsumeMiddleware<ConsumeContext<T>> with group identifier
  IPublishMiddleware<PublishContext<T>>  (publish has no group; per-T only)

FluentValidation rule at startup:
  forall registered middleware m in IMiddlewareDescriptorRegistry:
    if m.Scope == BusScope AND m.TContext is generic ConsumeContext<T> / PublishContext<T>:
      fail validation with MessagingConfigurationException naming m

Priority sort within each scope:
  ASC int, ties resolve in registration order.
  Lower priority runs first (outer ring).
  Framework constants live on the middleware classes themselves:
    TenantPropagationConsumeMiddleware.Priority = -1000
    TenantPropagationPublishMiddleware.Priority = -1000
  Default user priority = 0.
```

**PublishingContext mutability transition (R10):**

```
PublishContext               (abstract non-generic base — Content, MessageType,
                              CancellationToken, Headers, Topic)
  └── PublishingContext<T>   (sealed; adds mutable Options, DelayTime,
                              IsTransactional, plus an internal _isCompleted flag)

Pipeline flow:
  Construct PublishingContext<T> from caller args + ambient CT
  → middleware sees PublishingContext<T> on the way in; Options/DelayTime setters work
  → on await next() return: pipeline sets _isCompleted = true
  → post-next middleware code that calls context.Options = x or context.DelayTime = y
    throws InvalidOperationException("PublishContext is read-only after next() returned").
    Reads remain valid.

This collapses the originally-planned PublishingContext<T> / PublishedContext<T>
two-type split into a single mutable context with a runtime-enforced completion flag.
R10's compile-time enforcement target shifts to a runtime invariant; the simpler
single-type surface avoids both the access-mechanism question (how middleware sees
the read-only view post-next) and the field-copy allocation at the boundary.

See Outstanding Questions: the broader access-mechanism design (single mutable context
vs separate PublishedContext<T> type) was an open call left to U1 — flagged below.
```

---

## Implementation Units

### U1. Foundation: middleware contracts and context types

**Goal:** Introduce the public contracts (`IPublishMiddleware<TContext>`, `IConsumeMiddleware<TContext>`), the publish-side context hierarchy (`PublishContext` base + `PublishingContext<T>` with `_isCompleted` flag), the unsealed `ConsumeContext` hierarchy (`record class` retained for `with`/equality), `MessagingConfigurationException`, and the `context.WithCancellationToken(...)` mechanism (mutate-style on the context). New types compile; nothing wired into the existing pipeline yet.

**Requirements:** R1, R2, R3, R4, R10, R12.

**Dependencies:** None.

**Files:**
- `src/Headless.Messaging.Core/IPublishMiddleware.cs` (new)
- `src/Headless.Messaging.Core/IConsumeMiddleware.cs` (new)
- `src/Headless.Messaging.Core/PublishContext.cs` (new — `PublishContext` abstract base, `PublishingContext<T>` sealed with mutable `Options`/`DelayTime`/`IsTransactional` guarded by an internal `_isCompleted` flag; pipeline flips the flag after `await next()` returns)
- `src/Headless.Messaging.Abstractions/ConsumeContext.cs` (modify — unseal `ConsumeContext<TMessage>` keeping `public record class`, add non-generic `ConsumeContext` base also as `record class` exposing `object? Message`, `Type MessageType`, plus the shared envelope fields with required init properties)
- `src/Headless.Messaging.Core/MessagingConfigurationException.cs` (new if not already present in the package — grep first)
- `tests/Headless.Messaging.Core.Tests.Unit/ContextTypes/PublishContextTests.cs` (new — shape tests for PublishingContext<T> mutability and post-`_isCompleted` runtime guard)
- `tests/Headless.Messaging.Core.Tests.Unit/ContextTypes/ConsumeContextHierarchyTests.cs` (new — base-class field exposure tests, `with`-expression preservation)

**Approach:**
- `IPublishMiddleware<TContext> where TContext : PublishContext` exposes one method: `ValueTask InvokeAsync(TContext context, Func<ValueTask> next)`.
- `IConsumeMiddleware<TContext> where TContext : ConsumeContext` mirrors.
- Both interfaces are `[PublicAPI]`, in `Headless.Messaging` namespace.
- `context.WithCancellationToken(CancellationToken)` is an **instance method on the context base that mutates `context.CancellationToken` in place** (replaces the existing init-only property with a mutable backing field for this purpose; setter is `internal` to the assembly so middleware cannot bypass the method). The behavior contract: after `context.WithCancellationToken(t)`, the new token is visible to *all subsequent* `context.CancellationToken` reads, including reads by downstream middleware and the inner ring. Middleware authors are documented to **re-read `context.CancellationToken` at each call site** (do not capture into a local at method entry) so token swaps propagate. A debug-mode assertion in the pipeline (`Debug.Assert(context.CancellationToken == lastSeenToken || swapWasObserved, ...)`) is a development aid; not in release builds.
- `PublishContext` abstract non-generic base holds `Content`, `MessageType`, `CancellationToken` (mutable via `WithCancellationToken`), `Headers`, `Topic`.
- `PublishingContext<T>` sealed holds `T? Content` (typed shadow), `Options` (setter guarded by `_isCompleted`), `DelayTime` (setter guarded), `IsTransactional` (init-only, set by pipeline before middleware sees the context). When `_isCompleted == true`, setters throw `InvalidOperationException`. The pipeline sets the flag after `await next()` returns.
- `ConsumeContext<TMessage>` becomes `public record class ConsumeContext<TMessage> : ConsumeContext where TMessage : class` with required init properties. The non-generic base `public record class ConsumeContext` exposes `object? Message`, `Type MessageType`, plus `MessageId`, `CorrelationId`, `TenantId`, `Headers`, `Timestamp`, `Topic` (all init-only). Generic class shadows `Message` with typed `TMessage`. Keeping `record class` on both preserves the `with`-expression and value-equality public API the existing sealed record already exposes — a hidden compat win and a `record` is non-sealed by default in C# 10+.
- `MessagingConfigurationException : Exception` — used by U3's startup validation. Mirror existing `InvalidOperationException`-style messaging used elsewhere in the codebase.
- Source header `// Copyright (c) Mahmoud Shaheen. All rights reserved.` on every new file.
- `[PublicAPI]` on all public types per project convention.
- Argument validation via `Headless.Checks.Argument.*` / `Ensure.*` (not `ArgumentNullException.ThrowIfNull`).

**Patterns to follow:**
- `src/Headless.Caching.Redis/Setup.cs:11-73` for the `[PublicAPI]` + extension-member style (not needed in U1 directly but anchors the convention for U3).
- Existing `ConsumeContext<TMessage>` record at `src/Headless.Messaging.Abstractions/ConsumeContext.cs:19` for required init property shape — the unseal preserves this surface.
- `src/Headless.Messaging.Core/IPublishFilter.cs:93-209` for current context hierarchy patterns (`OptionsCore`, `DelayTimeCore` protected backing) — the new shape collapses those into a single `_isCompleted`-guarded property.

**Technical design:**

```
public interface IPublishMiddleware<in TContext> where TContext : PublishContext
{
    ValueTask InvokeAsync(TContext context, Func<ValueTask> next);
}

public abstract class PublishContext
{
    public object? Content { get; init; }
    public Type MessageType { get; init; }
    public CancellationToken CancellationToken { get; private set; }
    public MessageHeader Headers { get; init; }
    // ... shared envelope fields, all init-only

    public void WithCancellationToken(CancellationToken token) => CancellationToken = token;
}

public sealed class PublishingContext<T> : PublishContext
{
    private bool _isCompleted;
    private PublishOptions? _options;
    private TimeSpan? _delayTime;

    public new T? Content { get; init; }
    public bool IsTransactional { get; init; }

    public PublishOptions? Options
    {
        get => _options;
        set
        {
            if (_isCompleted) throw new InvalidOperationException(
                "PublishingContext is read-only after next() returned (R10).");
            _options = value;
        }
    }

    public TimeSpan? DelayTime { /* same pattern */ }

    internal void MarkCompleted() => _isCompleted = true;
}

public record class ConsumeContext
{
    public required object? Message { get; init; }
    public required Type MessageType { get; init; }
    public required string MessageId { get; init; }
    // ... etc
}

public record class ConsumeContext<TMessage> : ConsumeContext where TMessage : class
{
    public new required TMessage Message { get; init; }   // typed shadow
}
```

Directional only; final API shapes decided in implementation.

**Test scenarios:**

*Context types (PublishContextTests.cs):*
- Constructing `PublishingContext<OrderPlaced>` allows `pc.Options = new(...)` and `pc.DelayTime = TimeSpan.FromSeconds(1)` before `MarkCompleted()` is called.
- After `pc.MarkCompleted()` (simulating post-`next` pipeline state), `pc.Options = ...` throws `InvalidOperationException` with R10 message; reads still succeed.
- `pc.WithCancellationToken(newToken)` updates `pc.CancellationToken` for subsequent reads; both pre- and post-completion swaps land (token swap itself is not gated by completion).
- `PublishingContext<OrderPlaced>` exposes `CancellationToken`, `Headers`, `Topic`, `MessageType`, `IsTransactional` from the base/sealed combination correctly.

*ConsumeContext hierarchy (ConsumeContextHierarchyTests.cs):*
- `ConsumeContext<OrderPlaced>` constructed with `required` init properties exposes typed `Message` of `OrderPlaced` and base `Message` returns the same instance as `object`.
- `with`-expression syntax compiles and produces a copy with the changed field: `var b = a with { MessageId = "new-id" }` — record-class behavior preserved post-unseal.
- Up-cast `ConsumeContext<OrderPlaced>` to `ConsumeContext` exposes `MessageType == typeof(OrderPlaced)`.
- A non-sealed `record class ConsumeContext<TMessage>` can be subclassed (compile-only check via reference test that does not need a passing assertion — the build is the assertion).

**Verification:**
- New types compile in `Headless.Messaging.Abstractions` and `Headless.Messaging.Core`.
- Existing tests still pass (`ConsumeContext<TMessage>` instantiation paths in `_BuildConsumeContext` still work — the FastExpressionCompiler factory at `IConsumeExecutionPipeline.cs:184` must continue to produce valid `ConsumeContext<T>` instances after the unseal).
- No references to `MessagingConfigurationException` yet (introduced in U3).

---

### U2. Runtime middleware pipelines with R5 framework guarantees

**Goal:** Implement internal `PublishMiddlewarePipeline` and `ConsumeMiddlewarePipeline` that compose middleware russian-doll style around the publisher/consumer inner ring. Both pipelines enforce three R5 framework guarantees: (a) post-inner-ring middleware throws are caught and logged via `CancellationToken.None` — only when `innerRingCompleted == true`; (b) `OperationCanceledException` is detected by token-identity comparison (recursively walking `AggregateException.InnerExceptions`) and never silently swallowed; (c) when middleware returns normally but `context.CancellationToken.IsCancellationRequested`, the pipeline rethrows OCE so retry middleware cannot swallow outer cancellation undetected. Token-identity check is an inline `private static bool _ShouldRethrowOce(Exception, CancellationToken)` in each pipeline, not a shared `OceCancellationGuard.cs`.

**Requirements:** R4, R5, R10.

**Dependencies:** U1.

**Execution note:** Implement R5(a), R5(b), R5(c) test-first. All three guarantees are correctness invariants where implementation drift produces silent regressions (duplicate publishes, missed cancellations, retry-induced cancellation hiding) — write the failing test for each guarantee, then implement.

**Files:**
- `src/Headless.Messaging.Core/Internal/PublishMiddlewarePipeline.cs` (new — replaces `IPublishExecutionPipeline.cs` in U5b; contains inline `private static bool _ShouldRethrowOce(...)` method, no separate guard file)
- `src/Headless.Messaging.Core/Internal/ConsumeMiddlewarePipeline.cs` (new — replaces `IConsumeExecutionPipeline.cs` in U5b; absorbs the `_compiledConsumeContextFactories` cache from the old pipeline; same inline `_ShouldRethrowOce` helper)
- `src/Headless.Messaging.Core/Internal/MiddlewareDispatchKey.cs` (new — `readonly record struct MiddlewareDispatchKey(Type MiddlewareType, Type MessageType)` for the `ConcurrentDictionary` key)
- `tests/Headless.Messaging.Core.Tests.Unit/Internal/PublishMiddlewarePipelineTests.cs` (new)
- `tests/Headless.Messaging.Core.Tests.Unit/Internal/ConsumeMiddlewarePipelineTests.cs` (new)

**Approach:**
- Both pipelines are `internal sealed`, registered as Singleton (preserve existing pattern from `Setup.cs:115, 119` where filter pipelines are Singletons with per-call `CreateAsyncScope`).
- Per-publish/per-consume `CreateAsyncScope` is created inside `ExecuteAsync` (matches today's pattern from `IPublishExecutionPipeline.cs:65`).
- Middleware resolution: bus-scope object-typed via `provider.GetServices<IConsumeMiddleware<ConsumeContext>>()`; typed dispatch goes through the cache table (see below).
- Chain composition: build a tail-first closure (innermost wraps `innerPublish` / `innerInvoke`; each subsequent middleware wraps the previous closure's `next` reference). Avoid recursion stack-overflow for large chains by using a loop-based composer.
- Dispatch cache for typed consume middleware: `ConcurrentDictionary<MiddlewareDispatchKey, Delegate>` keyed on `(MiddlewareType, MessageType)`. Lazily populated via `GetOrAdd` calling `Expression.Lambda<>(...).CompileFast()` mirroring `_CompileFactory` from the existing `IConsumeExecutionPipeline.cs:184`. **Note:** `ConditionalWeakTable<TKey,TValue>` requires `TKey : class`; the struct key forces `ConcurrentDictionary`. The delegates and the `Type` keys both live for the process lifetime (closed-generic types are pinned in the type system), so the weak-keying GC win is moot.
- `_ShouldRethrowOce(Exception ex, CancellationToken token)` (inline private static in each pipeline):
  - Returns `true` when `ex` is itself an `OperationCanceledException` whose `.CancellationToken == token`.
  - Returns `true` when `ex is AggregateException agg` and ANY recursive walk of `agg.InnerExceptions` finds an OCE whose `.CancellationToken == token`. **Recursive AggregateException case:** when the wrapping `AggregateException` contains multiple inners (mix of OCE matching the context token AND non-OCE exceptions), the helper returns `true`, but the pipeline propagates the original `AggregateException` rather than constructing a fresh OCE — this preserves the non-OCE inner exception data for diagnostic purposes. A standalone unit test asserts this mixed-case behavior. A separate test asserts that an `OperationCanceledException` whose `.CancellationToken` is *not* the context token (e.g., per-attempt CTS from F6) returns `false` — these are not the framework's responsibility to rethrow.
  - Returns `false` otherwise.
- **R5(c) post-return cancellation re-check:** after each middleware's `await next()` returns (the runtime wrapper closure unwinds), the pipeline checks `context.CancellationToken.IsCancellationRequested`. If true AND the middleware returned normally (did not throw), the pipeline throws `new OperationCanceledException(context.CancellationToken)`. This catches retry middleware that catches a per-attempt OCE and returns normally without re-checking the outer token.
- **R5(a) post-inner-ring log-and-suppress (narrow):**
  - The pipeline maintains an `innerRingCompleted` boolean (initially `false`).
  - The innermost wrapper sets `innerRingCompleted = true` immediately after `await invokeInnerRing()` returns successfully.
  - When a middleware throws on the way back out (after its own `await next()` returned), the pipeline's outer try/catch checks `innerRingCompleted`:
    - If `true` → log via `_log.PostSuccessMiddlewareFailed(ex, middlewareType, CancellationToken.None)` and swallow. Inner work is committed; a post-success throw must not cause caller retry.
    - If `false` → propagate normally. The middleware threw on the way in or the inner ring failed; the failure is real.
  - Use `LoggerMessage.Define` source-generated logging per project convention.
  - `CancellationToken.None` for terminal log writes per learning `docs/solutions/logic-errors/terminal-state-overwrite-on-redelivery-2026-05-16.md`.
- For `PublishingContext<T>` post-`next` mutability lockdown (R10): after each middleware's `await next()` returns, the pipeline calls `context.MarkCompleted()` (internal method on `PublishingContext<T>` from U1) which flips `_isCompleted = true`. Subsequent setter calls throw `InvalidOperationException`. Reads remain valid. **Note:** the original plan envisioned a separate `PublishedContext<T>` type for compile-time read-only enforcement. F8 shifted this to runtime-enforced via the flag — simpler API, no allocation at the boundary, no access-mechanism question. The broader design call (single mutable context with flag vs separate read-only type) is captured in Outstanding Questions.
- Middleware lifetime: middleware instances resolved via `provider.GetServices<...>()` are Scoped per the existing pattern. The per-call scope already enforces fresh instances per message.

**Patterns to follow:**
- `src/Headless.Messaging.Core/Internal/IPublishExecutionPipeline.cs:51-136` — full pipeline shape, scope creation, exception phase mechanics. Adapt to `next`-delegate semantics.
- `src/Headless.Messaging.Core/Internal/IConsumeExecutionPipeline.cs:34-167` — consume side; reuse `_BuildConsumeContext` logic for the typed-T context construction.
- `IConsumeExecutionPipeline.cs:184-284` — `_CompileFactory` template for the new `(MiddlewareType, MessageType)` dispatch table.
- `docs/solutions/logic-errors/terminal-state-overwrite-on-redelivery-2026-05-16.md` — terminal-write `CancellationToken.None` discipline + token-identity OCE check.
- `docs/solutions/api/aspnet-core-cancellation-vs-timeout-differentiation-2026-05-07.md` — recursive `AggregateException` walk pattern.
- `Headless.Hosting` logger conventions (source-generated `LoggerMessage`).

**Technical design:**

```
// Russian-doll composer (consume side; publish is symmetric).
internal sealed class ConsumeMiddlewarePipeline
{
    private static readonly ConcurrentDictionary<MiddlewareDispatchKey, Delegate>
        _typedMiddlewareInvokers = new();

    public async Task<ConsumerExecutedResult> ExecuteAsync(
        ConsumerContext context, object messageInstance, Type messageType, CancellationToken ct)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var consumeContext = _BuildConsumeContext(messageInstance, messageType, ct, /*…*/);
        bool innerRingCompleted = false;

        // Resolve bus-scope object-typed and per-(T, group) typed middleware, priority-sorted.
        var busMiddleware = provider.GetServices<IConsumeMiddleware<ConsumeContext>>()
            .OrderBy(_GetPriority);
        var typedMiddleware = _ResolveTypedMiddleware(provider, messageType,
            context.ConsumerDescriptor.GroupName);

        // Compose tail-first: innermost = invoke handler; each outer wraps previous.
        Func<ValueTask> next = async () =>
        {
            await _InvokeHandlerAsync(provider, context, consumeContext);
            innerRingCompleted = true;
        };

        foreach (var m in typedMiddleware.Reverse())
            next = _Wrap(m, consumeContext, next, () => innerRingCompleted);
        foreach (var m in busMiddleware.Reverse())
            next = _Wrap(m, consumeContext, next, () => innerRingCompleted);

        await next();
        return /* result */;
    }

    private Func<ValueTask> _Wrap(IConsumeMiddleware<ConsumeContext> m, ConsumeContext ctx,
        Func<ValueTask> innerNext, Func<bool> innerCompleted)
    {
        return async () =>
        {
            try
            {
                await m.InvokeAsync(ctx, innerNext);

                // R5(c): post-return OCE re-check.
                if (ctx.CancellationToken.IsCancellationRequested)
                    throw new OperationCanceledException(ctx.CancellationToken);
            }
            catch (Exception ex) when (_ShouldRethrowOce(ex, ctx.CancellationToken))
            {
                // R5(b): token-identity OCE detection.
                if (ex is AggregateException) throw; // preserve AggregateException with non-OCE siblings
                throw new OperationCanceledException(ctx.CancellationToken);
            }
            catch (Exception ex) when (innerCompleted())
            {
                // R5(a): post-inner-ring log-and-suppress. Narrow: only when handler succeeded.
                _log.PostSuccessMiddlewareFailed(ex, m.GetType(), CancellationToken.None);
                // swallow
            }
            // else: rethrow normally (middleware threw on the way in OR inner ring failed)
        };
    }

    private static bool _ShouldRethrowOce(Exception ex, CancellationToken token)
    {
        if (ex is OperationCanceledException oce && oce.CancellationToken == token) return true;
        if (ex is AggregateException agg)
            return agg.InnerExceptions.Any(inner => _ShouldRethrowOce(inner, token));
        return false;
    }
}
```

Directional only.

**Test scenarios:**

*PublishMiddlewarePipelineTests (unit):*
- *Happy path*: registered middleware fires in priority order on the way in, reverse on the way back.
- *Short-circuit*: a middleware that returns without calling `next` causes the publisher inner ring to never invoke; outer middleware on the way back still runs.
- *Covers AE2.* When a middleware catches `OperationCanceledException` (matching `context.CancellationToken`) after `await next()` and returns normally, the runtime guard rethrows the OCE — caller observes cancellation.
- *Covers AE2.* When a middleware catches `OperationCanceledException` from an unrelated token (e.g., per-attempt CTS from F6) and returns normally, the runtime does NOT rethrow — the unrelated OCE was correctly handled.
- *AggregateException OCE-only*: a middleware that does `Task.WhenAll(...)` and produces `AggregateException` with an OCE inner at index > 0 (matching context token) is detected by `_ShouldRethrowOce`; rethrown as plain `OperationCanceledException`.
- *AggregateException mixed*: a middleware throws `AggregateException` with both an OCE matching the context token AND a non-OCE inner. `_ShouldRethrowOce` returns true, but the pipeline propagates the original `AggregateException` (preserves non-OCE inner for diagnostics) — assert thrown type is `AggregateException` with `InnerExceptions.Count >= 2`.
- *OCE-wrapped-without-inner-token*: a middleware throws `new OperationCanceledException("text")` (no CancellationToken constructor arg, so `oce.CancellationToken == default`). With context token != default, `_ShouldRethrowOce` returns `false` — the OCE propagates as-is (treated as a non-framework exception). Asserts the implementation does not over-rewrite OCEs that carry no inner cancellation-token preservation.
- *Covers AE2b.* When a middleware throws `ObjectDisposedException` AFTER `await next()` returns successfully AND the inner ring completed, the runtime catches and logs via `CancellationToken.None`; `PublishAsync` returns successfully to the caller (no duplicate-publish hazard).
- *R5(a) narrow scope*: when a middleware throws on the way IN (before `await next()`), the exception propagates normally — even though it happens to be a post-success-style exception, `innerRingCompleted == false` so log-and-suppress does NOT apply. Caller observes the exception.
- *R5(c) post-return cancellation re-check*: a "retry middleware" fixture that calls `await next()` with a per-attempt CTS, catches the per-attempt OCE on cancellation, and returns normally — when the OUTER `context.CancellationToken` is also signalled, the pipeline's post-return check throws OCE. When only the per-attempt CTS was cancelled and the outer token is healthy, the pipeline returns successfully.
- *Cancellation token swap (R4, F6)*: a middleware that calls `context.WithCancellationToken(perAttemptCts.Token)` before `await next()` causes downstream middleware to see the per-attempt token via `context.CancellationToken`; cancelling the per-attempt CTS aborts only that inner work; the original outer token must be restored or re-checked by the swapping middleware before the wrapping pipeline observes it.
- *Per-call scope*: each `ExecuteAsync` invocation creates a fresh `AsyncScope`; scoped middleware instances are isolated between concurrent publishes.
- *Empty pipeline*: zero middleware registered → `innerPublish` is invoked directly with no wrapper allocations.
- *Mutability lockdown*: a middleware that captures `context` and tries to set `context.Options = x` AFTER `await next()` throws `InvalidOperationException` (R10 runtime enforcement).
- *Dispatch cache hit/miss*: first invocation populates the `ConcurrentDictionary`; second invocation hits the cached delegate (verified via debug counter or by reflection on the dictionary count).
- *Dispatch cache key correctness*: register two typed middleware for different message types; confirm each fires only on its own `MessageType` — no key collision.

*ConsumeMiddlewarePipelineTests (unit):*
- *Covers AE1.* Typed `IConsumeMiddleware<ConsumeContext<OrderPlaced>>` at per-(T, group) scope fires for matching deliveries; returns without `next` short-circuits the handler.
- *Covers AE3.* Same typed middleware registered to group `"checkout-handler"` does not fire when message goes to group `"reporting"`.
- *Mixed object-typed and typed middleware*: bus-scope `IConsumeMiddleware<ConsumeContext>` wraps per-(T, group) typed middleware (priority-aware composition across scopes).
- *Per-message scope isolation*: confirmed via concurrent consume of two messages with scope-tracking fixtures.
- *No-middleware pipeline*: handler invoked directly; no wrapper allocations.
- *R5(a/b/c) symmetric coverage*: each of the R5 cases above repeats on the consume side with the consume-pipeline shape (handler success/throw, OCE token-identity match/mismatch, retry-with-state outer-cancellation hiding).

*Saga (F5), retry-with-state (F6), and error-policy chain (F7) test scenarios live in the integration test suite, not unit tests* — see U2's integration test note below. These flows compose across pipeline + handler invocation + recording fixtures and read cleaner as integration-shaped tests.

**Integration test scenarios (placed in `tests/Headless.Messaging.Core.Tests.Integration/Middleware/`):**
- *Covers AE7 (F5 saga compensation).* A middleware that releases a reserved resource in a `catch` around `await next()` runs the release on handler-throw and propagates the original exception with stack trace intact.
- *Covers AE8 (F6 retry-with-state).* Retry middleware loop with per-attempt CTS; abort observable downstream; overall outer cancel still propagates via R5(c).
- *Error-policy chain (F7).* Two middleware with typed catches (one for `ValidationException`, one for `TransientPublishException`); outer middleware sees inner-uncaught exceptions correctly. Verifies composition end-to-end through the pipeline + a real handler invocation.

**Verification:**
- New pipelines compile and pass their own unit tests.
- The three R5 framework guarantees have direct test coverage from AE2, AE2b, the explicit `_ShouldRethrowOce` cases, and the dedicated R5(c) post-return re-check tests.
- `IPublishExecutionPipeline` and `IConsumeExecutionPipeline` (old) still exist alongside; not yet wired.
- No regressions in existing filter pipeline tests (they still use the old pipelines).

---

### U3. Registration API on `MessagingBuilder` and startup validation

**Goal:** Add `AddBusPublishMiddleware<T>`, `AddBusConsumeMiddleware<T>`, `AddPublishMiddlewareFor<TMw, TMsg>`, `AddConsumeMiddlewareFor<TMw, TMsg>(string group)` to `MessagingBuilder`, each returning a fluent handle with `.WithPriority(int)`. Maintain a per-builder descriptor registry (`IMiddlewareDescriptorRegistry`, singleton) populated at registration time and consumed by both U2's pipelines (for priority sort + dispatch resolution) and the FluentValidation rule (for typed-at-bus rejection). Add a FluentValidation rule on `MessagingOptions` that reads the registry and fails startup with `MessagingConfigurationException` when a typed middleware is registered at bus scope.

**Requirements:** R6, R7, R8, R15.

**Dependencies:** U1, U2.

**Files:**
- `src/Headless.Messaging.Core/Configuration/MessagingBuilder.cs` (modify — add four new registration methods; host the descriptor registry via an internal `IMiddlewareDescriptorRegistry` reference threaded into the builder; existing `AddPublishFilter` / `AddSubscribeFilter` stay in place, deleted in U5b)
- `src/Headless.Messaging.Core/Configuration/MiddlewareRegistration.cs` (new — fluent handle `MiddlewareRegistration` with `.WithPriority(int)`; internal descriptor type `MiddlewareDescriptor(MiddlewareType, Scope, Priority, GroupName?, MessageType?)`)
- `src/Headless.Messaging.Core/Configuration/IMiddlewareDescriptorRegistry.cs` (new — `IMiddlewareDescriptorRegistry` + internal `MiddlewareDescriptorRegistry` implementation; singleton lifetime; populated by `MessagingBuilder` registration methods, consumed by pipelines and the validator)
- `src/Headless.Messaging.Core/Configuration/MessagingOptions.cs` (modify — extend inline `MessagingOptionsValidator` at `MessagingOptions.cs:412` with typed-at-bus rule that reads the descriptor registry. Validator gets the registry via constructor injection per FluentValidation cross-property pattern from `docs/solutions/concurrency/startup-pause-gating-and-half-open-recovery.md`. `MessagingOptions` itself stays free of middleware-list state — the registry is the right home, not the options class.)
- `tests/Headless.Messaging.Core.Tests.Unit/Configuration/MessagingBuilderMiddlewareTests.cs` (new — registration tests for the four new methods)
- `tests/Headless.Messaging.Core.Tests.Unit/Configuration/MessagingOptionsValidatorMiddlewareTests.cs` (new — startup validation tests)

**Approach:**
- All four registration methods use `services.TryAddEnumerable(ServiceDescriptor.Scoped<…, T>())` — matches current filter lifetime and prevents double-registration on repeated `AddHeadlessMessaging` (learning #4 precedent).
- Each method also records a descriptor in `IMiddlewareDescriptorRegistry` (singleton registered once when the builder is constructed). The descriptor carries `(MiddlewareType, Scope, Priority, GroupName?, MessageType?)` — needed by U2's pipelines for priority sorting and per-(T, group) dispatch, and by the FluentValidation rule.
- `MiddlewareRegistration` fluent handle:
  - `.WithPriority(int p)` — sets the descriptor's priority via a reference to the descriptor entry just added. Returns the `MessagingBuilder` to continue chaining.
  - Default priority: `0` (user middleware). Framework middleware uses constants defined on the middleware classes themselves (`TenantPropagationConsumeMiddleware.Priority = -1000` etc.).
- FluentValidation rule via `services.Configure<MessagingOptions, MessagingOptionsValidator>(...)` (use the existing `Headless.Hosting` configure-with-validator extension per CLAUDE.md conventions). The validator constructor takes `IMiddlewareDescriptorRegistry`; the rule walks each descriptor:
  - If `Scope == BusScope` AND `TContext` is a closed generic of `ConsumeContext<>` or `PublishContext<>` (not the non-generic base), reject with a `MessagingConfigurationException` named-pair message ("Middleware `Foo` is registered at bus scope but declares typed `TContext` `ConsumeContext<OrderPlaced>` — typed middleware must use `AddConsumeMiddlewareFor<...>(group)`.").
- `ValidateOnStart()` is wired via the standard `Headless.Hosting` path.

**Patterns to follow:**
- `src/Headless.Messaging.Core/Configuration/MessagingBuilder.cs:104-176` — existing `AddSubscribeFilter` / `AddPublishFilter` shape. Mirror their `TryAddEnumerable(ServiceDescriptor.Scoped<>())` and `XmlDoc` style; rewrite for middleware.
- `src/Headless.Messaging.Core/Configuration/RetryProcessorOptionsValidator.cs` (likely exists; verify) — FluentValidation startup-rule pattern per learning #3.
- `Headless.Hosting` `Configure<TOptions, TValidator>()` — DI registration with `ValidateOnStart()`.
- `docs/solutions/concurrency/startup-pause-gating-and-half-open-recovery.md` — validator-with-injected-dependency cross-property pattern; same shape used here for `IMiddlewareDescriptorRegistry`.
- C# 14 extension members are NOT used here — `MessagingBuilder` uses instance methods per existing convention.

**Test scenarios:**

*MessagingBuilderMiddlewareTests:*
- `AddBusConsumeMiddleware<MyMw>()` registers `MyMw` with `Scoped` lifetime via `TryAddEnumerable`.
- Calling `AddBusConsumeMiddleware<MyMw>()` twice does not double-register (`TryAddEnumerable` idempotence); descriptor registry contains exactly one entry.
- `AddBusPublishMiddleware<MyMw>().WithPriority(-500)` records the priority on the descriptor.
- `AddConsumeMiddlewareFor<MyMw, OrderPlaced>("checkout")` records the descriptor with `MessageType = OrderPlaced`, `GroupName = "checkout"`.
- `AddPublishMiddlewareFor<MyMw, OrderPlaced>()` records the descriptor with `MessageType = OrderPlaced`, no group.
- Default priority is `0` when `.WithPriority` not called.
- Ties resolve in registration order — assert by registering three middleware at priority `0` and observing dispatch order via the pipeline.

*MessagingOptionsValidatorMiddlewareTests:*
- *Covers AE3b.* Registering `IConsumeMiddleware<ConsumeContext<OrderPlaced>>` at bus scope causes startup `ValidateOnStart()` to throw `MessagingConfigurationException` naming the middleware type.
- Registering `IConsumeMiddleware<ConsumeContext>` (non-generic) at bus scope passes validation.
- Registering typed middleware at per-(T, group) scope passes validation.
- The exception message names both the middleware type and the required corrective action.

**Verification:**
- New registration methods compile alongside existing filter methods (no deletion yet).
- Startup validation rejects typed-at-bus with a clear error.
- Test fixtures demonstrate working priority sorting integration with U2's pipeline (cross-unit integration test in `MessagingBuilderMiddlewareTests` is acceptable here).

---

### U4. First-party middleware: tenant propagation

**Goal:** Implement `TenantPropagationPublishMiddleware` and `TenantPropagationConsumeMiddleware` replacing the existing filters. Update `SetupMessagingTenancy.AddTenantPropagationServices` to take a `MessagingBuilder` parameter and register the new middleware through the builder's `AddBusXxxMiddleware<T>().WithPriority(...)` API at bus scope with the priority constant declared on each middleware class. Old filters remain in place; deletion in U5b.

**Requirements:** R13, R14.

**Dependencies:** U1, U2, U3.

**Files:**
- `src/Headless.Messaging.Core/MultiTenancy/TenantPropagationPublishMiddleware.cs` (new — declares `public const int Priority = -1000`)
- `src/Headless.Messaging.Core/MultiTenancy/TenantPropagationConsumeMiddleware.cs` (new — declares `public const int Priority = -1000`)
- `src/Headless.Messaging.Core/SetupMessagingTenancy.cs` (modify — change `AddTenantPropagationServices` signature to thread through a `MessagingBuilder` so middleware registration goes through the builder's descriptor registry. The old filter registrations stay in place until U5b deletes them. See API surface change note below.)
- `tests/Headless.Messaging.Core.Tests.Unit/MultiTenancy/TenantPropagationPublishMiddlewareTests.cs` (new — derived from existing `TenantPropagationPublishFilterTests`)
- `tests/Headless.Messaging.Core.Tests.Unit/MultiTenancy/TenantPropagationConsumeMiddlewareTests.cs` (new — derived from existing `TenantPropagationConsumeFilterTests`)

**API surface change for `AddTenantPropagationServices`:**

The existing helper takes `IServiceCollection` and calls `services.TryAddEnumerable(...)` directly. Because the new middleware registration must flow through `MessagingBuilder` (so the descriptor registry sees the entry and the FluentValidation rule + the pipeline both pick it up), the helper signature changes to `AddTenantPropagationServices(this MessagingBuilder builder, ...)` and chains the builder's `AddBusConsumeMiddleware<TenantPropagationConsumeMiddleware>().WithPriority(TenantPropagationConsumeMiddleware.Priority)` etc.

Call sites of `AddTenantPropagationServices` update from `services.AddTenantPropagationServices(...)` to `builder.AddTenantPropagationServices(...)` within the chained `AddHeadlessMessaging(b => b.AddTenantPropagationServices(...))` block. Greenfield posture; no external callers; one internal call site needs update.

**Approach:**
- `TenantPropagationConsumeMiddleware`:
  - Implements `IConsumeMiddleware<ConsumeContext>` (object-typed; bus scope).
  - Declares `public const int Priority = -1000` (R8 framework-priority constant on the class itself, not in a separate `MiddlewarePriorities.cs`).
  - Single `InvokeAsync`:
    ```
    public async ValueTask InvokeAsync(ConsumeContext context, Func<ValueTask> next)
    {
        if (context.TenantId is { } value)
        {
            using var scope = _currentTenant.Change(value);
            _logger?.TenantContextSwitched(value);
            await next();
        }
        else
        {
            await next();
        }
    }
    ```
  - No `_scope` field, no defensive double-dispose, no executed/exception callback discipline.
- `TenantPropagationPublishMiddleware`:
  - Implements `IPublishMiddleware<PublishContext>` (object-typed; bus scope).
  - Declares `public const int Priority = -1000`.
  - Same shape — propagate tenant from current scope into outbound message headers/options.
  - Read current behavior from `src/Headless.Messaging.Core/MultiTenancy/TenantPropagationPublishFilter.cs` and port the executing-phase logic into the pre-`next` half of `InvokeAsync`; port executed-phase into post-`next` half (if any).
- `SetupMessagingTenancy.AddTenantPropagationServices(this MessagingBuilder builder, ...)`:
  - Chains:
    ```
    return builder
        .AddBusConsumeMiddleware<TenantPropagationConsumeMiddleware>()
            .WithPriority(TenantPropagationConsumeMiddleware.Priority)
        .AddBusPublishMiddleware<TenantPropagationPublishMiddleware>()
            .WithPriority(TenantPropagationPublishMiddleware.Priority);
    ```
  - Old filter registrations are NOT in this helper anymore (deleted as part of the helper rewrite); the old filter types still exist on disk until U5b.

**Patterns to follow:**
- `src/Headless.Messaging.Core/MultiTenancy/TenantPropagationConsumeFilter.cs:30-89` — current implementation; port executing logic to pre-`next`, executed to no-op (`using` handles dispose), exception to no-op.
- `src/Headless.Messaging.Core/SetupMessagingTenancy.cs:34-55` — existing helper signature; switch from `IServiceCollection` to `MessagingBuilder` as the receiver.

**Test scenarios:**

*TenantPropagationConsumeMiddlewareTests:*
- *Covers F4 (migration), AE6.* When `ConsumeContext.TenantId` is set, middleware invokes `_currentTenant.Change(value)` before `await next()`. **Inside the `next` invocation (during the simulated handler call), assert `_currentTenant.Id == expected` — the tenant context must be live for the handler, not merely "set and unset around `await next`".** After `next()` returns, the `using` scope is disposed and `_currentTenant.Id` reverts.
- When `ConsumeContext.TenantId` is null/empty, middleware does not call `_currentTenant.Change` — passes through; `_currentTenant.Id` matches the ambient pre-middleware value during and after `next()`.
- When the inner handler throws, the `using` block disposes the scope on stack unwind; `_currentTenant.Id` is reset to its pre-middleware value. Exception propagates with stack trace intact.
- When the inner handler is cancelled (OCE), scope is still disposed before OCE propagates.
- Logger emits the `TenantContextSwitched` event with the resolved tenant id.

*TenantPropagationPublishMiddlewareTests:*
- Behaviors mirror the consume side per the current `TenantPropagationPublishFilter` semantics. Port each existing test from `TenantPropagationPublishFilterTests` and verify equivalent middleware behavior — including a "tenant value visible during `next()`" assertion analogous to the consume side.

**Verification:**
- New middleware classes compile.
- New tests pass.
- Old `TenantPropagation*Filter` classes still exist (deleted in U5b); old filter registrations no longer present in `SetupMessagingTenancy` after U4.

---

### U5. Atomic swap — split into three bisectable commits

**Goal:** Switch `DirectPublisher` and `OutboxPublisher` to invoke the new pipelines; delete `IPublishFilter`, `IConsumeFilter`, the old execution pipelines, all old context types, both old `TenantPropagation*Filter` classes; rewrite/delete all affected unit and integration tests. The original single-commit "atomic swap" is split into three sequential commits so reviewers and `git bisect` users can isolate failures. Build must be green at the end of each commit (every commit ships a working tree).

**Requirements:** R20.

**Dependencies:** U4.

**Commit split rationale:** The original one-commit U5 spanned publisher rewiring + bulk deletion + ~10 test fixture migrations. A single 1000+ line diff is opaque to reviewers and breaks `git bisect` — if a build regression appears, the bisector lands on one commit covering all three concerns. Three smaller commits give per-area attribution, and the middle commit (deletion-only) is purely subtractive so any regression after it is the deletion's fault. Each commit is independently green; the sequence is U5a → U5b → U5c.

#### U5a. Wire publishers to new pipelines

**Goal:** Switch `DirectPublisher` and `OutboxPublisher` to call the new `IPublishMiddlewarePipeline` and `IConsumeMiddlewarePipeline`. Both old and new pipelines exist in DI temporarily; the old ones become unused after this commit but still compile. Build is green; all existing tests pass against the new pipelines.

**Files:**
- `src/Headless.Messaging.Core/Internal/DirectPublisher.cs` (modify — `ExecuteAsync` call switches from `IPublishExecutionPipeline` to `IPublishMiddlewarePipeline`; `isTransactional: false` continues unchanged)
- `src/Headless.Messaging.Core/Internal/OutboxPublisher.cs` (modify — same, preserving `_IsNonAutoCommitTransactional()` decision at `src/Headless.Messaging.Core/Internal/OutboxPublisher.cs:74` made before pipeline entry)
- `src/Headless.Messaging.Core/Setup.cs` (modify — add `TryAddSingleton<IPublishMiddlewarePipeline, PublishMiddlewarePipeline>` and consume side; keep the old `IPublishExecutionPipeline`/`IConsumeExecutionPipeline` registrations until U5b)

**Verification:**
- `dotnet build` green.
- `dotnet test` green (all old filter tests still pass because the old types still exist; the new pipelines are exercised by the new tests written in U1-U4).
- A grep confirms no production-code callers of `IPublishExecutionPipeline.ExecuteAsync` / `IConsumeExecutionPipeline.ExecuteAsync` remain — only old test fixtures.

#### U5b. Delete old surface (production code)

**Goal:** Delete the seven old types and their files. No new code; pure subtraction. Tests that reference the deleted types fail to compile — those are migrated in U5c. To keep the working tree green at this commit, the test files that reference deleted types are also deleted **here**, and the rewritten replacements are added in U5c. The trade-off: tests that purely encoded triad mechanics get a clean delete in U5b; tests with surviving semantics that need rewriting come back in U5c.

**Files deleted (production):**
- `src/Headless.Messaging.Core/IPublishFilter.cs` (interface, base class, `PublishingContext` non-generic, `PublishedContext` non-generic, `PublishExceptionContext`)
- `src/Headless.Messaging.Core/IConsumeFilter.cs` (interface, base class, `FilterContext`, `ExecutingContext`, `ExecutedContext`, `ExceptionContext`)
- `src/Headless.Messaging.Core/Internal/IPublishExecutionPipeline.cs` (interface + impl)
- `src/Headless.Messaging.Core/Internal/IConsumeExecutionPipeline.cs` (interface + impl; `_BuildConsumeContext` factory and `_DispatchAsync` logic verified moved into `ConsumeMiddlewarePipeline` in U2)
- `src/Headless.Messaging.Core/MultiTenancy/TenantPropagationPublishFilter.cs`
- `src/Headless.Messaging.Core/MultiTenancy/TenantPropagationConsumeFilter.cs`

**Files modified:**
- `src/Headless.Messaging.Core/Setup.cs` (remove the now-unused `IPublishExecutionPipeline` / `IConsumeExecutionPipeline` registrations)
- `src/Headless.Messaging.Core/Configuration/MessagingBuilder.cs` (remove `AddPublishFilter` / `AddSubscribeFilter` methods and their XmlDoc)

**Files deleted (tests, no surviving semantics):**
- `tests/Headless.Messaging.Core.Tests.Unit/ConsumeFilterTests.cs` (tests for deleted context types)
- `tests/Headless.Messaging.Core.Tests.Unit/PublishFilterTests.cs` (tests for deleted context types; coverage already replicated in U1's `PublishContextTests`)
- `tests/Headless.Messaging.Core.Tests.Unit/MultiTenancy/TenantPropagationPublishFilterTests.cs` (replaced by U4's `TenantPropagationPublishMiddlewareTests`)
- `tests/Headless.Messaging.Core.Tests.Unit/MultiTenancy/TenantPropagationConsumeFilterTests.cs` (replaced by U4's `TenantPropagationConsumeMiddlewareTests`)

**Files temporarily deleted, restored in U5c (tests with surviving semantics):**
- `tests/Headless.Messaging.Core.Tests.Unit/ConsumeFilterPipelineTests.cs`
- `tests/Headless.Messaging.Core.Tests.Unit/PublishExecutionPipelineTests.cs`
- `tests/Headless.Messaging.Core.Tests.Unit/PublishedContextIsTransactionalTests.cs`

**Verification:**
- `dotnet build` green.
- `dotnet test` green — coverage temporarily lower because the migrated tests don't return until U5c, but no compile errors and no failing tests.
- `git grep` for `IPublishFilter`, `IConsumeFilter`, `IPublishExecutionPipeline`, `IConsumeExecutionPipeline`, `PublishingContext` (non-generic), `PublishedContext` (non-generic), `PublishExceptionContext`, `ExecutingContext`, `ExecutedContext`, `ExceptionContext`, `FilterContext`, `TenantPropagationConsumeFilter`, `TenantPropagationPublishFilter` returns zero hits in `src/` and `tests/`.
- Before deletion, run a fast sanity grep to confirm no provider package implements the deleted interfaces (origin doc R20 + research finding both confirm zero references, but verify in the commit boundary).

#### U5c. Migrate surviving test semantics to middleware shape

**Goal:** Restore the three test files that encode surviving behaviors, ported to the new middleware contracts. Migrate the ~10 fixture types and ~26 `should_*` test methods from the old fixtures to middleware fixtures. Build green; coverage targets restored.

**Files restored, rewritten:**
- `tests/Headless.Messaging.Core.Tests.Unit/ConsumeFilterPipelineTests.cs` → renamed/rewritten as `tests/Headless.Messaging.Core.Tests.Unit/Internal/ConsumeMiddlewarePipelineMigratedTests.cs` (or merged into U2's pipeline tests). Port the ~13 fixtures (`RecordingFilterA/B`, `TenantObservingFilter`, `HandlingFilter`, `ExecutingThrowingFilter`, `InnerResultMutatingFilter`, `OuterResultObservingFilter`, `HandlingFilterWithFallback`, `RecordingMessageDispatcher`, `ExecutedOceThrowingFilter`, `OceThrowingDispatcher`, `SimpleMessage`, `FilterCallRecorder`) to middleware fixtures. Drop tests whose semantics no longer apply (e.g., `should_invoke_executed_phase_in_reverse_order` — replaced with russian-doll-equivalent tests already in U2).
- `tests/Headless.Messaging.Core.Tests.Unit/PublishExecutionPipelineTests.cs` → rewritten as `tests/Headless.Messaging.Core.Tests.Unit/Internal/PublishMiddlewarePipelineMigratedTests.cs`. (13 `AddPublishFilter<…>` callsites; 10+ fixture types — `RecordingPublishFilterA/B`, `PublishedThrowingFilter`, `PublishedOceThrowingFilter`, `TenantStampingFilter`, `DelayMultiplyingFilter`, `HandlingPublishFilter`, `PublishingThrowingFilter`, `NullingOptionsFilter`, `DelayNullingFilter`, `InstanceTrackingFilter`). Port to middleware fixtures.
- `tests/Headless.Messaging.Core.Tests.Unit/PublishedContextIsTransactionalTests.cs` → rewritten as `tests/Headless.Messaging.Core.Tests.Unit/Internal/IsTransactionalPropagationTests.cs`. 4 test methods + `IsTransactionalCapturingFilter` fixture become middleware that captures `context.IsTransactional` after `await next()` (still readable post-completion; reads are allowed even when `_isCompleted == true`).

**Files modified:**
- `tests/Headless.Messaging.Core.Tests.Unit/MessagingBuilderTests.cs` (filter portions — lines 12-62, fixtures at 736-738) — delete filter-registration tests; replacements live in U3's `MessagingBuilderMiddlewareTests`.
- `tests/Headless.Messaging.Core.Tests.Unit/MultiTenancy/SetupMessagingTenancyTests.cs:34, 43, 112, 119` — update `ServiceType` assertions from `IConsumeFilter` / `IPublishFilter` to the new middleware interfaces; verify Scoped lifetime and idempotent registration.
- `tests/Headless.Messaging.Core.Tests.Unit/IntegrationTests/RuntimeSubscriberIntegrationTests.cs:128` — `RecordingConsumeFilter` port to recording middleware.
- `tests/Headless.Messaging.Core.Tests.Unit/IntegrationTests/SharedConsumeScopeIntegrationTests.cs:79` — `ScopedExecutionFilter` port to scoped middleware.

**Approach:**
- Each fixture port preserves the assertion shape; only the registration callsite and the `InvokeAsync` signature change.
- A pre-merge checklist runs through the old fixture list and confirms each has either a new fixture or a documented deletion reason.

**Verification:**
- `dotnet build` green at HEAD.
- `dotnet test` passes all unit and integration tests.
- Coverage targets from CLAUDE.md (≥85% line, ≥80% branch) maintained on affected modules.
- Pre-commit checklist:
  - Every fixture type from the old test suite has a corresponding new fixture in the new suite OR a documented reason for deletion (no surviving semantics).
  - The 13 named fixtures in `ConsumeFilterPipelineTests` and the 10+ in `PublishExecutionPipelineTests` are individually accounted for.
  - `should_*` test method count in the new pipeline tests >= the relevant count in the old (allowing for legitimate consolidation).
  - Two integration tests (`RuntimeSubscriberIntegrationTests`, `SharedConsumeScopeIntegrationTests`) still pass with their middleware ports.

---

### U6. Documentation sync

**Goal:** Update `docs/llms/messaging.md`, package `README.md` files that reference the old filter shape, and close the brainstorm's Outstanding Questions section.

**Requirements:** Project memory's "ALWAYS keep docs/llms synchronized" rule + learning #6 doc-sync mandate.

**Dependencies:** U5c.

**Files:**
- `docs/llms/messaging.md` (modify — replace filter triad documentation with middleware sections; document the two scopes, numeric priority, the three R5 framework guarantees, the runtime-flag mutability lockdown for `PublishingContext<T>`)
- `src/Headless.Messaging.Abstractions/README.md` (modify — middleware shape replaces filter shape; show `IConsumeMiddleware<TContext>` example)
- `src/Headless.Messaging.Core/README.md` (modify — registration API, priority constants on middleware classes, the three R5 guarantees)
- `docs/brainstorms/2026-05-19-messaging-middleware-pipeline-requirements.md` (modify — mark `Outstanding Questions / Resolve Before Planning` items resolved; update `status: completed` if frontmatter convention supports it)
- Any other `src/Headless.Messaging.*/README.md` that mentions `AddPublishFilter` / `AddSubscribeFilter` — grep first to enumerate.

**Approach:**
- `docs/llms/messaging.md` is the canonical LLM-consumable messaging doc (per CLAUDE.md). Rewrite the filter section to document the middleware seam; preserve other sections (envelope, retry, OTel enrichment, capability matrix).
- Package READMEs: replace the filter-registration snippet with the new middleware registration shape. Show one object-typed example and one typed example.
- Outstanding Questions in the brainstorm: mark the registration-naming RBP as resolved (`AddBusConsumeMiddleware<T>` shape adopted); document the resolution.
- No new pattern docs in `docs/solutions/` in this unit — those land via `/dev-compound` after merge (per recommendations from learning research).

**Test scenarios:**

*Test expectation: none — documentation work has no test surface beyond manual review of the rendered docs.* Verification is editorial; the doc-sync mandate is a consistency check between code and prose, not a behavioral test.

**Verification:**
- Doc preview renders correctly (manual review).
- No stale `AddPublishFilter` / `AddSubscribeFilter` references in any `README.md` under `src/Headless.Messaging.*`.
- `docs/llms/messaging.md` uses the new vocabulary throughout.
- Brainstorm doc has `From 2026-05-19 review` items either marked resolved or kept open with explicit rationale.

---

## System-Wide Impact

- **Public API surface change (intentional, breaking).** `IPublishFilter`, `IConsumeFilter`, and all their context types are deleted from `Headless.Messaging.Core` and `Headless.Messaging.Abstractions`. Greenfield posture per project memory; no external consumers. Internal migration is bounded to first-party tenant middleware and the test surface (enumerated in U5).
- **`ConsumeContext<TMessage>` shape change.** Removing `sealed` is a binary-breaking ABI change. Keeping `record class` preserves the `with`-expression and value-equality public API. Acceptable per greenfield posture; downstream usages in `_BuildConsumeContext` and tests are the only construction sites.
- **`SetupMessagingTenancy.AddTenantPropagationServices` signature change.** Now takes `MessagingBuilder` instead of `IServiceCollection`. One internal call site updates; greenfield posture; no external callers.
- **DI registration shape change.** `services.AddSubscribeFilter` / `services.AddPublishFilter` extensions removed from `MessagingBuilder`. Replaced by four new methods. Internal callers (`SetupMessagingTenancy`) migrated in U4.
- **`PublishingContext<T>` becomes runtime-enforced read-only post-`next` (R10 shift).** Compile-time read-only via a separate `PublishedContext<T>` type is dropped in favor of an `_isCompleted` flag that throws `InvalidOperationException` on post-`next` setter calls. Simpler API; no field-copy allocation at the inner-ring boundary. Outstanding Questions tracks the broader design call.
- **Documentation surface.** `docs/llms/messaging.md` is read by AI agents to drive Headless.Messaging usage; out-of-sync content produces wrong code suggestions. Doc sync (U6) is non-optional and lands in the same merge sequence as the code changes.
- **No provider package impact.** Verified: zero references to deleted interfaces in any `Headless.Messaging.*` provider package.

---

## Key Technical Decisions

- **One seam per direction, two registration scopes, numeric priority.** Carries forward from origin brainstorm; see `docs/brainstorms/2026-05-19-messaging-middleware-pipeline-requirements.md` for the five-framework rationale. (see origin)
- **Verbose-explicit registration method names** (`AddBusConsumeMiddleware<T>()` etc.) over fluent-scope-builder style. Matches existing `MessagingBuilder` instance-method convention (`AddSubscribeFilter` / `AddPublishFilter`); minimum vocabulary churn for users of the current API.
- **Single mutable `PublishingContext<T>` with runtime `_isCompleted` flag** over two-type `PublishingContext<T>` / `PublishedContext<T>` split. R10 enforcement shifts from compile-time to runtime (setter throws when `_isCompleted == true`). Trade-off: loses compile-time guarantee; gains simpler API surface, no field-copy allocation at the inner-ring boundary, no access-mechanism question for middleware on the way back out. The broader design decision (single vs split context) is captured in Outstanding Questions.
- **Separate `ConcurrentDictionary<MiddlewareDispatchKey, Delegate>` dispatch cache** keyed on a `readonly struct (MiddlewareType, MessageType)`. **Not `ConditionalWeakTable`** — CWT requires `TKey : class`, and the struct key forces a dictionary. Since closed-generic `Type` references and the compiled delegates both live for the process lifetime, the GC win from weak-keying is moot. Pattern adapts `src/Headless.Messaging.Core/Internal/IConsumeExecutionPipeline.cs:29-32`.
- **`OperationCanceledException` detection via token-identity, recursive AggregateException walk, inline in each pipeline.** Adopted from `docs/solutions/logic-errors/terminal-state-overwrite-on-redelivery-2026-05-16.md` and `docs/solutions/api/aspnet-core-cancellation-vs-timeout-differentiation-2026-05-07.md`. Comparing `oce.CancellationToken == context.CancellationToken` is the only safe rule when middleware can swap the token (F6 retry case); recursive `AggregateException.InnerExceptions` walk handles `Task.WhenAll` fan-out. **The helper is a `private static` method inside each pipeline class, not a shared `OceCancellationGuard.cs`** — single call site, no shared mutable state, no external use case; a separate file plus dedicated test class is more ceremony than the logic justifies.
- **Mixed-OCE `AggregateException` preserves wrapping.** When `AggregateException` contains both an OCE matching the context token AND other non-OCE inners, `_ShouldRethrowOce` returns `true` but the pipeline propagates the original `AggregateException` (not a fresh `OperationCanceledException`) so non-OCE inner exception data is preserved for diagnostics. Standalone unit test asserts this.
- **Post-return cancellation re-check (R5(c)).** After each middleware returns normally, the pipeline checks `context.CancellationToken.IsCancellationRequested` and throws OCE if true. Catches retry middleware that swallows outer cancellation by handling only the per-attempt OCE.
- **Narrow post-success log-and-suppress (R5(a)).** The wrapper applies only when `innerRingCompleted == true` — i.e., the handler/publisher actually succeeded. If middleware throws on the way in, OR the inner ring failed, the exception propagates normally. This prevents the wrapper from masking real failures during pre-`next` setup work.
- **`CancellationToken.None` for after-success log writes.** Adopted from the terminal-state-overwrite learning. The R5(a) post-success log-and-suppress wrapper must not be torn down by host-shutdown cancellation — the log/observability write completes regardless.
- **`context.WithCancellationToken(...)` is a mutate-style instance method.** Replaces the existing init-only `CancellationToken` property with a mutable backing field; setter is internal so middleware uses the method. Middleware authors documented to **re-read `context.CancellationToken` at each call site** rather than capture into a local — otherwise token swaps are invisible. Debug-mode `Debug.Assert` in the pipeline guards against accidental capture-and-stale-reuse in development.
- **FluentValidation startup rule for typed-at-bus rejection** (AE3b) over runtime check deep in dispatch. Pattern: `services.Configure<MessagingOptions, MessagingOptionsValidator>()` from `Headless.Hosting`, validated at startup via `ValidateOnStart()`. Validator constructor injects `IMiddlewareDescriptorRegistry` per the cross-property pattern from `docs/solutions/concurrency/startup-pause-gating-and-half-open-recovery.md`. Mirrors `RetryProcessorOptionsValidator` per learning #3.
- **Descriptor registry lives on `IMiddlewareDescriptorRegistry`, not on `MessagingOptions`.** Options classes describe declarative configuration; the descriptor registry is a registration-time inventory (singleton lifetime) populated by `MessagingBuilder` and consumed by the pipelines and the validator. Keeping middleware state out of `MessagingOptions` preserves the options pattern's pure-data discipline.
- **Framework priority constants on the middleware classes themselves.** `TenantPropagationConsumeMiddleware.Priority = -1000` etc. — no separate `MiddlewarePriorities.cs`. Each middleware's priority is part of its own contract; co-locating reduces the lookup distance for a maintainer asking "what priority does this middleware run at?"
- **`TryAddEnumerable(ServiceDescriptor.Scoped<>())` for all middleware registration.** Preserves current filter lifetime; idempotent on repeated `AddHeadlessMessaging` calls in test composition (learning #4 precedent).
- **`ConsumeContext` keeps `record class` syntax** (sealed removed; record kept). Records' `with`-expression and value-equality are part of the existing public surface; dropping `record` would silently break call sites that rely on them. The non-generic base is also a `record class` for symmetry; inheritance of `record class` from `record class` is the supported pattern in C# 10+.
- **U5 split into three commits (U5a/U5b/U5c)** over a single atomic-swap commit. Trade-off: more commits per merge; gain: bisectable, smaller diffs per review pass, deletion commit is purely subtractive (so any regression after deletion is attributable to deletion, not to a 1000-line mixed diff). Each commit ships a green working tree.
- **Saga, retry-with-state, and error-policy-chain tests live in integration tests.** F5/F6/F7 scenarios compose across the pipeline + a real handler invocation + recording fixtures, and read cleaner as integration tests (`tests/Headless.Messaging.Core.Tests.Integration/Middleware/`) than unit tests on the pipeline. The pipeline unit tests cover the R5 guarantees in isolation; integration tests verify the same guarantees survive composition.
- **Source-gen sketch not gated as U0.** R19's "API stable across swap" is treated as a forward-looking design constraint rather than a planning blocker (per Phase 5.1.5 call-out). If the source-gen sketch later reveals an obstruction, R19 relaxes; this plan does not rewind.

---

## Risks & Mitigation

- **U5 commit-split discipline.** Three commits instead of one increases the count of "moments at which the tree could be left non-green." *Mitigation:* each sub-commit (U5a/U5b/U5c) has its own `Verification:` section with `dotnet build` + `dotnet test` gates; the pre-merge checklist in U5c is non-negotiable.
- **`ConsumeContext<TMessage>` unseal breaks consumers in unexpected ways.** Records have value-equality semantics that some test setups may rely on. *Mitigation:* the `_BuildConsumeContext` factory is the only production construction site; keeping `record class` preserves both equality and `with`-expression behavior so most existing usages are unaffected. Run `dotnet test` in U1 and check for surprise failures.
- **`context.WithCancellationToken(...)` mutate-style + per-call-site re-read discipline.** Capturing `context.CancellationToken` into a local at method entry hides token swaps from downstream code. *Mitigation:* invariant documented in `docs/llms/messaging.md` and `src/Headless.Messaging.Core/README.md` ("middleware must re-read `context.CancellationToken` at each `await` boundary; do not capture into a local"); debug-mode `Debug.Assert` in the pipeline catches the most common misuse in dev. A dedicated unit test for the retry-with-state scenario (AE8) exercises the swap path.
- **`PublishingContext<T>` runtime read-only enforcement silently regresses to compile.** Without the type system enforcing read-only post-`next`, a careless middleware might assume mutability still works post-`await next()`. *Mitigation:* the setter throws a clear `InvalidOperationException` naming R10; a unit test asserts the exception fires; doc-sync (U6) explicitly calls out the runtime model.
- **Dispatch table cache key correctness.** A `(MiddlewareType, MessageType)` key collision would cause wrong-middleware dispatch (silent correctness bug). *Mitigation:* the key is a `readonly record struct` over two distinct `Type` fields; `Type` is reference-equal for the same closed-generic instance. Add a dedicated unit test that registers two typed middleware for different messages and confirms each fires only on its own type.
- **Post-success log-and-suppress could hide real failures from operators.** The R5(a) wrapper suppresses post-success exceptions silently. *Mitigation:* emit a metric counter for post-success middleware throws (e.g., `messaging.middleware.post_success_failures{middleware="..."}`) so operators see fault rate even though individual calls don't propagate. Per learning #3 ("if a callback controls breaker state, logging is not enough; failures must remain observable"). The narrow scope (only when `innerRingCompleted == true`) further bounds the suppression surface.
- **Doc-sync drift between code and `docs/llms/messaging.md`.** If U6 is deferred past the U5c merge, AI agents reading messaging.md will get wrong code suggestions. *Mitigation:* U6 is dependency-locked to U5c in this plan; ship in the same merge sequence per learning #6.
- **Greenfield posture assumes no external consumers exist.** A new external dependency on filter interfaces between plan-write and merge would invalidate R20. *Mitigation:* the planning grep verified zero references at plan time; U5b re-runs the grep at commit time before deletion (in the U5b verification checklist).

---

## Outstanding Questions

- **(F3 from plan doc-review)** Broader access-mechanism design for `PublishContext` post-`next`: single mutable `PublishingContext<T>` with `_isCompleted` runtime flag (this plan's commitment) vs separate `PublishedContext<T>` type with compile-time read-only enforcement. F8 commits to the single-context runtime-flag approach for `Options`/`DelayTime` setters; this resolves the immediate U1 blocker. The broader call (does the framework benefit from a distinct `PublishedContext<T>` type signalling phase to middleware bodies, e.g., for static analysis or convention enforcement?) remains open and may be revisited if first-party middleware patterns reveal a need. Capture revisit triggers as they arise rather than re-litigating at U1 implementation time.

---

## Deferred to Implementation

- **`context.WithCancellationToken(...)` exact API shape detail.** The plan commits to mutate-style instance method with internal setter; final naming and the debug-mode assertion's exact form (a `Debug.Assert` predicate, a sampled DEBUG-only invariant check, etc.) are an implementation choice.
- **`MiddlewareRegistration` fluent handle implementation type.** Class vs `readonly struct`; how it threads the descriptor back to the registry. Pick whichever integrates cleanly with the existing `MessagingBuilder` shape.
- **Tests integration vs unit boundary for `MessagingBuilderMiddlewareTests`.** Some tests in U3 may need to instantiate the full pipeline (U2) to verify priority sorting works end-to-end. Cross-unit integration is acceptable inside `*.Tests.Unit` since the pipeline doesn't require external infrastructure.
- **Concrete validator class location** (`MessagingOptionsValidator` exists vs new file). U3's implementation decides whether to extend an existing options validator or introduce a new file. Plan's bias: extend the existing inline validator at `MessagingOptions.cs:412`.
- **Logger event names and message templates.** Source-generated logging via `LoggerMessage.Define`; final names decided in U2.
- **Metric counter shape for R5(a) suppressions.** Counter name, tag dimensions, registration with `IMeterFactory`. Implementation choice in U2 after the source-gen logger discipline is settled.

---

## Sources & References

- Origin: `docs/brainstorms/2026-05-19-messaging-middleware-pipeline-requirements.md`
- Codebase entry points:
  - `src/Headless.Messaging.Core/IPublishFilter.cs`, `src/Headless.Messaging.Core/IConsumeFilter.cs` (current contracts)
  - `src/Headless.Messaging.Core/Internal/IPublishExecutionPipeline.cs`, `src/Headless.Messaging.Core/Internal/IConsumeExecutionPipeline.cs` (current pipelines)
  - `src/Headless.Messaging.Core/Configuration/MessagingBuilder.cs` (current registration shape)
  - `src/Headless.Messaging.Core/Internal/DirectPublisher.cs`, `src/Headless.Messaging.Core/Internal/OutboxPublisher.cs` (publisher integration)
  - `src/Headless.Messaging.Abstractions/ConsumeContext.cs` (R12 unseal target)
  - `src/Headless.Messaging.Core/MultiTenancy/TenantPropagationConsumeFilter.cs`, `src/Headless.Messaging.Core/MultiTenancy/TenantPropagationPublishFilter.cs` (R13/R14 migration source)
  - `src/Headless.Messaging.Core/SetupMessagingTenancy.cs` (DI rewiring target)
- Institutional learnings:
  - `docs/solutions/logic-errors/terminal-state-overwrite-on-redelivery-2026-05-16.md` (OCE token-identity, `CancellationToken.None` for terminal writes)
  - `docs/solutions/concurrency/circuit-breaker-transport-thread-safety-patterns.md` (cache discipline, `ex.Message` not echoed to wire)
  - `docs/solutions/concurrency/startup-pause-gating-and-half-open-recovery.md` (FluentValidation cross-property pattern, validator-with-injected-dependency)
  - `docs/solutions/api/aspnet-core-cancellation-vs-timeout-differentiation-2026-05-07.md` (recursive `AggregateException` walk, `TryAddEnumerable` precedent)
  - `docs/solutions/guides/messaging-transport-provider-guide.md` (transport-package contract, zero filter implementations)
  - `docs/solutions/messaging/transport-wrapper-drift-and-doc-sync.md` (greenfield discipline, doc-sync mandate)
- Related issues:
  - GitHub issue #218 (this RFC)
  - GitHub issue #232 (publisher intent split — downstream)
  - GitHub issue #275 (OTel `IActivityTagEnricher` — already merged)
