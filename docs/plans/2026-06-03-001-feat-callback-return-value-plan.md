---
title: "feat: Restore callback return-value capture via context.SetResponse"
status: completed
date: 2026-06-03
type: feat
origin: docs/brainstorms/2026-06-03-callback-return-value-requirements.md
---

# feat: Restore callback return-value capture via `context.SetResponse`

## Summary

Add `context.SetResponse<TResponse>(value)` so a consumer can produce a typed response body that the framework publishes to the message's `CallbackName` on the durable bus path. This restores the documented-but-broken "consumer return value is published to the callback" behavior (the callback body is permanently `null` today). The response serializes as its concrete type, works for bus- and queue-originated requests, preserves the existing `RemoveCallback` / `RewriteCallback` / `AddResponseHeader` controls, and isolates response-serialization failures so they fail the consume cleanly rather than corrupting the request's terminal state.

---

## Problem Frame

The callback feature documents that a consumer's return value is published to `CallbackName`, but `IConsume<TMessage>.ConsumeAsync` returns `ValueTask` (no return value) and `ConsumeMiddlewarePipeline` hardcodes `result: null` in both construction branches (`src/Headless.Messaging.Core/Internal/ConsumeMiddlewarePipeline.cs:119-120`). The callback leg at `src/Headless.Messaging.Core/Internal/ISubscribeExecutor.cs:474` then publishes a null body; only response *headers* carry data today.

This is vestigial DotNetCore.CAP plumbing. CAP reflection-invoked subscriber methods whose `return` value populated `ConsumerExecutedResult.Result`. The port to expression-compiled `IConsume<T>` dispatch (5–8× faster, no boxing) dropped the return-value capture but kept the `Result` property and the `PublishAsync(ret.Result)` call — so it publishes a permanently-empty payload. An interim doc fix walked the language back to "headers-only"; this work restores real payload chaining and reverts that wording.

The semantics are CAP's (one-way async chaining, caller never awaits, single `CallbackName`, the inherited control surface), not MassTransit's request/reply (no `requestId` await, no request client, no `Fault<T>`). The API ergonomics borrow MassTransit's typed setter shape (`RespondAsync<T>` → `SetResponse<TResponse>`) but the name signals "capture a value to chain," not "reply now."

---

## Key Technical Decisions

- **Imperative setter, not a typed interface.** Response is declared via `context.SetResponse<TResponse>(value)`, not a new `IConsume<TMessage, TResult>`. Callbacks are optional/conditional async chaining; an imperative setter matches that and joins the existing `RemoveCallback` / `RewriteCallback` / `AddResponseHeader` family. It also leaves `CompiledMessageDispatcher` (keyed on `TMessage` alone, no `TResult` at its generic call site) and the `ValueTask` dispatch path untouched. (see origin: `docs/brainstorms/2026-06-03-callback-return-value-requirements.md`)

- **Generic `SetResponse<TResponse>` that captures `typeof(TResponse)`.** The reference implementations both treat the response type as first-class: CAP knew the subscriber's `MethodInfo.ReturnType` via reflection; MassTransit uses generic `RespondAsync<T>` and carries the type on the wire. We re-capture it explicitly because expression-compiled dispatch lost the reflective type info.

- **Type capture fixes `Headers.Type`, not body bytes.** Research confirmed the response *body* already serializes as its concrete type today: `JsonUtf8Serializer` binds STJ's `SerializeToUtf8Bytes<object>(...)`, and STJ serializes a boxed `object` by its runtime type; the response consumer deserializes off its `IConsume<ConcreteType>` registration, not the wire type header (`src/Headless.Messaging.Core/Internal/IConsumerRegister.cs:655`). The real gap is the `Headers.Type` wire header, which reads `"Object"` on callbacks because `MessagePublishRequestFactory` infers `T = object` from the `object?`-typed `ret.Result`. Capturing `typeof(TResponse)` and threading it to the publish leg makes the contract explicit and correct for dead-letter/observability, rather than resting on an STJ boxed-object accident no test guards.

- **Atomic-rollback failure semantics — already provided, verified not built.** A response value that fails to serialize must fail the consume (request never marked Succeeded → clean retry), not commit the request and silently drop the callback. This mirrors MassTransit's transactional outbox ("if an exception is thrown, the buffered messages are discarded"; the consume rolls back) and avoids the silent-loss class of bug that issue #392 itself is. Review verified (~95%) that serialization is **already eager**: every storage provider's `StoreMessageAsync` (`InMemoryDataStorage.cs:186`, `PostgreSqlDataStorage.cs:206`, `SqlServerDataStorage.cs:203`) calls `serializer.Serialize` synchronously inside `OutboxMessageWriter.PublishAsync`, which runs inside the consume try-block at `ISubscribeExecutor.cs:445-517`. A serialization throw therefore propagates through the existing `catch → e.ReThrow()`, prevents `_SetSuccessfulState`, and rolls back — the chosen semantic with **no production code change**. We only verify it with a test (no `ISubscribeExecutor` edit).

- **Response leg always rides the durable bus path (`IOutboxBus`)**, even for queue-originated requests — durability over point-to-point symmetry. This is the existing behavior; no change.

- **Multi-hop chaining is emergent, verified not built.** A consumer setting `AddResponseHeader(Headers.CallbackName, "next")` plus the existing per-hop `CorrelationSequence + 1` increment (`ISubscribeExecutor.cs:462-464`) already produces A→B→C chains. We add a test to prove it and lock it, but write no code targeting it. If the test fails, the *fix* is deferred (test skipped + limitation documented), not folded into this PR.

---

## High-Level Technical Design

The captured value travels: consumer → same `ConsumeContext` instance → middleware reads it post-`next()` → `ConsumerExecutedResult` → executor publish leg → outbox → callback consumer. The failure boundary (eager serialize) is the load-bearing reliability seam.

```mermaid
sequenceDiagram
    participant C as Consumer (IConsume<T>)
    participant Ctx as ConsumeContext (same instance)
    participant Pipe as ConsumeMiddlewarePipeline
    participant Exec as ISubscribeExecutor
    participant OB as IOutboxBus / Outbox
    participant RC as Response Consumer (IConsume<TResponse>)

    C->>Ctx: SetResponse<TResponse>(value)
    Note over Ctx: store boxed value + typeof(TResponse),<br/>guarded by _isCompleted
    C-->>Pipe: ConsumeAsync returns (ValueTask, void)
    Pipe->>Ctx: read Response + ResponseType (after next())
    Pipe->>Exec: ConsumerExecutedResult(result, resultType, callbackName, headers)
    alt CallbackName present
        Exec->>OB: PublishAsync(value, MessageName=CallbackName,<br/>Headers.Type=resultType, correlation headers)
        Note over OB: StoreMessageAsync serializes eagerly (existing)
        alt serialize OK
            Note over Exec,OB: consume marked Succeeded after this returns
            OB-->>RC: deliver (independent retry on broker failure)
        else serialize throws
            Exec->>Exec: existing catch → e.ReThrow() — consume fails, NOT Succeeded
            Note over Exec: request retried cleanly; no false success
        end
    else no CallbackName
        Note over Exec: response dropped silently
    end
```

Prose is authoritative where the diagram and text disagree.

---

## Requirements Traceability

| Req (origin) | Covered by |
|---|---|
| R1, R2 SetResponse API, optional | U1 |
| R3 controls compose with SetResponse | U1, U5, U8 |
| R4, R5 publish to callback as concrete type | U2, U3 |
| R6 correlation headers preserved | U3 (existing), U5 |
| R7 no SetResponse + CallbackName → headers-only | U2, U5 |
| R8 SetResponse + no CallbackName → dropped | U2, U5 |
| R9 bus and queue intents | U5 |
| R10 fan-out (N subscribers → N responses) | U5, U8 |
| R11 demo | U6 |
| R12 integration tests + test-plan doc first | U5 |
| R13 docs revert + README sync | U7 |

---

## Implementation Units

### U1. `SetResponse<TResponse>` on `ConsumeContext`

- **Goal:** Let a consumer capture a typed response value and its static type on the context.
- **Requirements:** R1, R2, R3
- **Dependencies:** none
- **Files:**
  - `src/Headless.Messaging.Abstractions/ConsumeContext.cs` (modify)
  - `tests/Headless.Messaging.Core.Tests.Unit/ConsumeContextTests.cs` (create or extend)
- **Approach:** `ConsumeContext` is a non-sealed `record` (line 15) carrying mutable state guarded by `_isCompleted` (the `MarkCompleted()` precedent). Add `internal object? Response { get; private set; }` and `internal Type? ResponseType { get; private set; }`, plus `public void SetResponse<TResponse>(TResponse value)` that stores the boxed value and `typeof(TResponse)`, guarded by the existing `_isCompleted` check (mirror `AddResponseHeader` / `SetCancellationToken`). Last-write-wins. `null` value is allowed (clears to a typed-null response). The same instance flows through the pipeline, so the captured value is visible to the middleware after the consumer returns.
- **Patterns to follow:** `src/Headless.Messaging.Abstractions/MessageHeader.cs:11-22` (`AddResponseHeader`), `ConsumeContext.MarkCompleted()`.
- **Test scenarios:**
  - Happy: `SetResponse(value)` then read `Response`/`ResponseType` → boxed value and `typeof(TResponse)` (assert static type capture: `SetResponse<IFoo>(concreteFoo)` records `typeof(IFoo)`, not the runtime type).
  - Edge: not calling `SetResponse` → `Response` is null, `ResponseType` is null.
  - Edge: calling `SetResponse` twice → last value/type wins.
  - Error: calling `SetResponse` after completion (`_isCompleted` true) → throws/no-ops consistently with the existing guard's behavior.
- **Verification:** New unit tests pass; `Response`/`ResponseType` correctly populated and guarded.

### U2. Capture the response in the middleware pipeline

- **Goal:** Replace the hardcoded `result: null` with the consumer's captured value and carry its type into `ConsumerExecutedResult`.
- **Requirements:** R4, R5, R7, R8
- **Dependencies:** U1
- **Files:**
  - `src/Headless.Messaging.Core/Internal/ConsumeMiddlewarePipeline.cs` (modify lines ~116-120)
  - `src/Headless.Messaging.Core/Internal/ConsumerExecutedResult.cs` (modify — add `ResultType`)
  - `tests/Headless.Messaging.Core.Tests.Unit/SubscribeInvokerTests.cs` (extend)
- **Approach:** After `await next()` (line ~114), read `consumeContext.Response` and `consumeContext.ResponseType` and pass them into `ConsumerExecutedResult` in **both** branches (replacing the two `null` literals). Add `Type? ResultType { get; set; }` to `ConsumerExecutedResult` alongside `Result`. When no response was set, `Result` stays null and `ResultType` stays null — this preserves the headers-only path (R7). When a response was set but `CallbackName` is empty, the value is carried but never published (R8, enforced by the existing `if (!string.IsNullOrEmpty(ret.CallbackName))` guard in U3's site).
- **Patterns to follow:** existing `ConsumerExecutedResult` construction; the `should_propagate_response_headers_added_by_consumer_to_callback_result` test at `SubscribeInvokerTests.cs:260` is the direct template.
- **Test scenarios:**
  - Covers AE1. Happy: consumer calls `SetResponse(concrete)` with a `CallbackName` present → `result.Result` is the concrete value, `result.ResultType` is the captured type.
  - Covers AE2. Edge: consumer never calls `SetResponse`, `CallbackName` present → `result.Result` is null, `result.ResultType` null (headers-only preserved).
  - Edge: `SetResponse` set, no `CallbackName` → `result.Result` populated but `result.CallbackName` empty.
  - Integration-ish: response headers added via `AddResponseHeader` coexist with a set response (both present on the result).
- **Verification:** Unit tests assert `Result`/`ResultType` populated exactly when `SetResponse` was called; headers-only path unchanged.

### U3. Type-correct callback publish leg

- **Goal:** Publish the response to `CallbackName` with `Headers.Type` set to the captured concrete type, not `"Object"`.
- **Requirements:** R4, R5, R6
- **Dependencies:** U2
- **Files:**
  - `src/Headless.Messaging.Core/Internal/ISubscribeExecutor.cs` (modify ~458-478)
  - `src/Headless.Messaging.Core/Internal/MessagePublishRequestFactory.cs` (modify — honor an explicit response/serialize type)
  - `src/Headless.Messaging.Abstractions/MessagePublishOptionsBase.cs` or `PublishOptions` (modify — add an explicit message-type override if needed)
  - `tests/Headless.Messaging.Core.Tests.Unit/` (extend) and harness assertion in U5
- **Approach:** The publish at `ISubscribeExecutor.cs:471-478` forwards `ret.Result` (an `object?`), so the factory infers `T = object` and writes `headers[Headers.Type] = "Object"` (`MessagePublishRequestFactory.cs:108`). Thread `ret.ResultType` so the factory sets `Headers.Type` from the captured type. Cleanest seam: add an explicit message-type to `MessagePublishOptionsBase` as **`internal init`** (not public — `Headless.Messaging.Abstractions` already declares `InternalsVisibleTo Headless.Messaging.Core`, so the executor can set it and the factory can read it without public API bloat) and have `MessagePublishRequestFactory`'s type logic prefer it over `typeof(T)`. Routing `MessageName` already comes from `CallbackName` and is unaffected. Do not change happy-path body serialization (already concrete via boxed STJ).
- **Patterns to follow:** `MessagePublishRequestFactory._ResolveMessageName` (the existing "explicit option overrides inferred type" precedent for the name); apply the same shape to the type header.
- **Test scenarios:**
  - Covers AE1. Concrete-type round-trip (in U5 harness): response published with `Headers.Type` == captured type name; response consumer deserializes to the concrete type.
  - Unit: factory given an explicit response type writes that into `Headers.Type` rather than `"Object"`.
  - Edge: `RewriteCallback` changes the target name; `Headers.Type` still reflects the response type.
  - Confirm no transport keys off `Headers.Type="Object"` on the happy path (assertion noted at ~80% confidence in research).
- **Verification:** `Headers.Type` carries the concrete type; round-trip deserialization in U5 passes.

### U4. Verify atomic-rollback on response-serialization failure (test-only)

- **Goal:** Prove that a response that fails to serialize fails the consume (request not marked Succeeded), never a silent post-commit drop. **No production code** — review confirmed the existing eager-serialize path already provides this (see Key Decision "Atomic-rollback").
- **Requirements:** R4 (reliability facet); origin Key Decision (atomic-rollback)
- **Dependencies:** U2, U3
- **Files:** integration test only, in the harness/provider project from U5. No `src/` changes.
- **Approach:** No code change. The response is serialized eagerly during `StoreMessageAsync` inside `OutboxMessageWriter.PublishAsync`, which runs inside the consume try-block at `ISubscribeExecutor.cs:445-517`; a throw reaches the existing `catch → e.ReThrow()` (line 516) and prevents `_SetSuccessfulState`. The unit exists solely to lock that behavior with a test so a future change can't regress it into a silent drop.
- **Execution note:** Start with a failing integration test that sets an unserializable response and asserts the request is not marked Succeeded.
- **Test suite design:** Integration (needs the real consume→outbox→storage path); lives in the harness/provider project from U5, not a unit mock — the failure semantics depend on real storage transaction ordering.
- **Test scenarios:**
  - Error: consumer `SetResponse(unserializable)` with `CallbackName` present → request message is retried/failed, **not** marked Succeeded; no callback delivered.
  - Error: confirm `OnExhausted` is not double-fired by a response-serialization failure (guards the prior `terminal-state-overwrite-on-redelivery` regression).
  - Happy contrast: serializable response → request Succeeded and callback delivered exactly once.
- **Verification:** Unserializable-response test proves clean rollback; no false-Succeeded row; existing retry/exhaustion behavior intact.

### U5. Integration tests (round-trip, fan-out, controls, multi-hop)

- **Goal:** Prove the full chain on bus and queue, including emergent multi-hop, fan-out, the control surface, and typed-null responses.
- **Requirements:** R9, R10, R12 (plus AE3, AE4, AE5)
- **Dependencies:** U2, U3, U4, U8
- **Files:**
  - `tests/Headless.Messaging.Core.Tests.Harness/` (shared conformance scenarios + `MessagingIntegrationTestsBase`)
  - one provider integration project for a concrete run, e.g. `tests/Headless.Messaging.RabbitMq.Tests.Integration/` (or `Headless.Messaging.Nats.Tests.Integration`)
  - a `dev-test-plan` document produced before writing tests (R12)
- **Approach:** Add round-trip scenarios to the harness base (so every provider inherits them) following the existing `ConsumerClientTestsBase` / `MessagingIntegrationTestsBase` pattern. Use `Bogus`-generated response payloads with only the asserted fields hardcoded. Cover both intents; the response leg always asserts arrival on the bus path regardless of request intent.
- **Execution note:** Produce the `dev-test-plan` document first (R12), then implement.
- **Test suite design:** Bulk integration via Testcontainers per the testing-diamond; the harness owns the cross-provider scenarios, the provider project supplies the concrete `Configure*` overrides. No new harness infrastructure beyond response-aware test consumers and a callback-collector consumer.
- **Test scenarios:**
  - Covers AE1. Bus round-trip: request consumer `SetResponse<TResponse>` → response consumer receives concrete `TResponse` (assert deserialized value + `Headers.Type`).
  - Queue round-trip: same, request via `IQueue`/`IOutboxQueue`, response arrives on bus path (R9).
  - Covers AE5. Fan-out: N bus subscribers each `SetResponse` with distinct values → N callback messages, one per subscriber's value (R10).
  - Covers AE3. `SetResponse` with no `CallbackName` → no callback published, no error.
  - Covers AE4. `RewriteCallback` + `SetResponse` → response published to the rewritten name. Plus `RemoveCallback` → no callback despite a set response.
  - Multi-hop: A→B (B sets response + `AddResponseHeader(Headers.CallbackName, "C")`) → C receives, and `CorrelationSequence` increments per hop. If this fails, mark skipped + document (do not fix here).
  - Typed-null: consumer `SetResponse<TResponse>(null)` with `CallbackName` present → callback published with a null body and `Headers.Type` still the captured type; the response consumer handles a null payload without throwing.
  - Concurrent fan-out isolation (pairs with U8): one message, N concurrent in-process subscribers where one calls `RemoveCallback`/`RewriteCallback` → each subscriber's callback decision is independent (no cross-contamination). This test is the trigger that confirms or refutes U8's shared-mutation risk.
- **Verification:** All scenarios green on at least one provider; harness scenarios available to all providers.

### U6. Callback demo

- **Goal:** Show the round trip end-to-end in a runnable demo.
- **Requirements:** R11
- **Dependencies:** U2, U3
- **Files:** `demo/Headless.Messaging.Console.Demo/` (add a request consumer that `SetResponse`s a typed result, a response consumer that deserializes it, and wire-up in the demo entry point)
- **Approach:** Mirror the existing `EventConsumer` registration shape; publish a request with a `CallbackName`, have the request consumer set a typed response, and log the deserialized response in the callback consumer.
- **Test scenarios:** `Test expectation: none -- demo project, behavior is covered by U5 integration tests.`
- **Verification:** Demo runs and prints the typed response received via the callback.

### U7. Docs revert and README sync

- **Goal:** Restore the "return value is published" language and sync the package README.
- **Requirements:** R13
- **Dependencies:** U1–U3 (document the shipped API)
- **Files:**
  - `docs/llms/messaging.md` (revert interim headers-only wording → real payload chaining via `SetResponse`)
  - `src/Headless.Messaging.Core/README.md` (and any other `src/Headless.Messaging.*/README.md` describing callbacks) per the authoring-lockstep rule
- **Approach:** Replace the interim "headers-only" callback description with the `SetResponse<TResponse>` flow, the concrete-type guarantee, the two edge defaults (no-SetResponse → headers-only; SetResponse-no-callback → dropped), and the atomic-rollback failure note. Follow `docs/authoring/AUTHORING.md` drift checks.
- **Test scenarios:** `Test expectation: none -- documentation.`
- **Verification:** Docs describe shipped behavior; `docs/llms/messaging.md` and the README agree; no stale headers-only claim remains.

### U8. Fan-out header isolation (verify-then-fix)

- **Goal:** Ensure that under concurrent in-process fan-out, one subscriber's `RemoveCallback` / `RewriteCallback` cannot alter another subscriber's callback. Surfaced by review as a pre-existing risk that this PR's fan-out + control-surface tests would otherwise mask.
- **Requirements:** R3, R10 (correctness under concurrency)
- **Dependencies:** none (independent of U1–U3); paired with the U5 isolation test
- **Files:**
  - `src/Headless.Messaging.Core/Internal/ConsumeMiddlewarePipeline.cs` (~line 57, construction site `new MessageHeader(originHeaders)`) and/or `src/Headless.Messaging.Abstractions/MessageHeader.cs` (constructor) — only if verification confirms the risk
- **Approach:** First **verify** whether concurrent in-process subscribers to one message actually share the same `originHeaders` dictionary reference. `MessageHeader` (`MessageHeader.cs:27-39`) mutates the wrapped dictionary in place via `RemoveCallback`/`RewriteCallback`; if that dictionary is the shared broker `originHeaders`, concurrent subscribers cross-contaminate. If confirmed, clone the headers dictionary at `MessageHeader` construction (or at the pipeline construction site) so each subscriber gets an isolated copy. If subscribers already receive independent deserialized copies, this collapses to the U5 isolation test alone with no `src/` change — document the finding and close.
- **Test suite design:** Integration; the confirming/refuting test is the concurrent fan-out scenario in U5.
- **Test scenarios:** covered by the U5 "Concurrent fan-out isolation" scenario; if a `src/` change is made, that scenario must fail before the fix and pass after.
- **Verification:** Concurrent fan-out isolation test passes; if no code change was needed, the finding (subscribers already isolated) is documented in the PR description.

---

## Scope Boundaries

### Outside this product's identity

- `IConsume<TMessage, TResult>` typed-return interface — `SetResponse` is the single mechanism.
- Request/reply (RPC): caller awaiting a response, request client, `Fault<T>` response routing. Callbacks stay fire-and-forget chaining.
- CAP's `ControlCapHeaderResponse` opt-in gate — our controls are honored unconditionally; no evidence a gate is needed.

### Deferred for later

- CAP-style in-response re-chaining as an explicit feature beyond the emergent multi-hop verified in U5.

### Deferred to Follow-Up Work

- Strict single-transaction atomicity between the outbox response-write and the request success-mark (eliminating a duplicate callback if the process crashes between the two writes). This is consistent with the framework's existing at-least-once outbox guarantees; document the characteristic rather than re-architect the executor's transaction boundary in this PR.
- Any multi-hop *fix* if the U5 chained-callback test fails (the test is in scope; the fix is not).

---

## Risks & Dependencies

- **Serialization timing (U4) — RESOLVED.** Review verified (~95%) that serialization is eager: `StoreMessageAsync` in every provider calls `serializer.Serialize` synchronously inside `OutboxMessageWriter.PublishAsync`, within the consume try-block. Atomic-rollback is the existing behavior; U4 is test-only, no production change.
- **`Headers.Type` consumers (U3).** ~80% confident no transport keys off `Headers.Type` on the happy path (deserialize uses the consumer param type). One integration assertion confirms it; if a transport does, U3 widens.
- **Multi-hop emergent behavior (U5).** ~75% confident A→B→C works untouched. The fork is whether a consumer-set `CallbackName` via `AddResponseHeader` cleanly overrides the executor's own callback resolution at the next hop.
- **Fan-out header mutation (U8).** ~75% confident concurrent in-process subscribers share the broker `originHeaders` dictionary, making `RemoveCallback`/`RewriteCallback` cross-contaminate. The U5 isolation test confirms or refutes; the clone fix lands only if confirmed.
- **Prior regression (`docs/solutions/logic-errors/terminal-state-overwrite-on-redelivery-2026-05-16.md`).** U4 must not re-introduce terminal-state corruption or `OnExhausted` double-fire; the error-path tests guard this.
- **Doc-sync trigger.** This is a consumer-visible behavior change (callback body null → typed payload), so U7's doc + README sync is mandatory, not optional.

---

## Sources & Research

- Origin: `docs/brainstorms/2026-06-03-callback-return-value-requirements.md`.
- Issue #392.
- Prior art: DotNetCore.CAP callback model (subscriber `return` → callback, reflection-captured; `CapHeader` controls; `CallbackName`/`CorrelationSequence` headers we inherited). MassTransit request/response (`RespondAsync<T>`, response address + `requestId`, transactional outbox: buffer-then-deliver, discard on exception, independent delivery retry). Our model is CAP's semantics with MassTransit-flavored typed ergonomics.
- Code: `ConsumeContext.cs`, `MessageHeader.cs:11-22`, `ConsumeMiddlewarePipeline.cs:114-120`, `ConsumerExecutedResult.cs`, `ISubscribeExecutor.cs:447-478`, `MessagePublishRequestFactory.cs:60,108,258`, `JsonUtf8Serializer.cs:18-23`, `IConsumerRegister.cs:655`, `OutboxMessageWriter.cs`.
- Tests: `tests/Headless.Messaging.Core.Tests.Harness` (`MessagingIntegrationTestsBase`, `ConsumerClientTestsBase`), `SubscribeInvokerTests.cs:260` (callback-header test template).
- Learnings: `docs/solutions/guides/messaging-transport-provider-guide.md` (header contract), `docs/solutions/logic-errors/terminal-state-overwrite-on-redelivery-2026-05-16.md` (isolation/regression), `docs/solutions/messaging/transport-wrapper-drift-and-doc-sync.md` (doc-sync discipline).

---

## Unresolved Questions

- Does any configured transport read `Headers.Type` on the happy path? (drives U3 scope) — answer via the U5 assertion.
- Do concurrent in-process fan-out subscribers share the broker `originHeaders` dictionary? (drives whether U8 needs a `src/` change) — answer via the U5 isolation test.

_Resolved during planning: `OutboxMessageWriter` serializes eagerly during `StoreMessageAsync` — atomic-rollback is the existing behavior, so U4 is test-only (verified ~95% in review)._
