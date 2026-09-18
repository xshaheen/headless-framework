---
title: Messaging Receive Middleware (Issue 276) - Plan
type: feat
date: 2026-09-18
origin: docs/plans/2026-09-18-0001-design-messaging-receive-middleware.md
artifact_contract: x-unified-plan/v1
artifact_readiness: implementation-ready
product_contract_source: x-plan-bootstrap
execution: code
---

# Messaging Receive Middleware (Issue 276) - Plan

## Goal Capsule

- **Objective:** A messaging application developer can intercept an inbound message's raw envelope — headers and body, with the consumer's identity known — before the framework deserializes it, and every way that interception can end produces exactly one deterministic, observable outcome, with deserialization failures dead-lettered the same way wherever they are detected.
- **Means:** One new inbound middleware stage (`IReceiveMiddleware` ring before contract-version validation and deserialization), owned outcome mapping in `ConsumerRegister`, terminal `MessageDeserializationException` classification, and a read-only inbound header snapshot with the response contract moved onto `ConsumeContext` (design doc, `docs/plans/2026-09-18-0001-design-messaging-receive-middleware.md`).
- **Authority:** The origin design document above; the session-settled decisions D1–D4 recorded in its Open Decisions; then repository conventions (`CLAUDE.md`, `docs/authoring/AUTHORING.md`, Makefile).
- **Execution profile:** Implementation units U1–U8 in dependency order on branch `shaheen/messaging-receive-middleware`; ship as one PR to `main`.
- **Stop conditions:** Evidence that a settled decision (design's Open Decisions) cannot work; evidence that the outcome table cannot be implemented without splitting `ConsumerRegister` ownership; CI failing for reasons unrelated to this change after three repair rounds.
- **Tail ownership:** This pipeline (x-autopilot) owns review application, commits, PR, and CI watch through merge-readiness.

---

## Product Contract

### Summary

Add a receive middleware stage to `Headless.Messaging.Core` that runs after subscriber lookup and before contract-version validation and Stage A deserialization, with explicit `Skip`/`Reject` short-circuits, copy-on-write envelope transformation, per-delivery scoped resolution, and registry-only matching by `(message type, prefixed group, lane)`. Outcome ownership stays entirely in `ConsumerRegister`: accept, skip, poison-on-arrival reject, cancelled, and post-success failure each map to exactly one transport/storage/`OnExhausted`/circuit-breaker row. A new `MessageDeserializationException` becomes terminal at both deserialization stages — poison-on-arrival at Stage A, `Exhausted` at attempt 1 at Stage B. `MessageHeader` loses its mutation members; response metadata (`SetResponseHeader`, `SetResponseDestination`, `SuppressResponse`) moves onto `ConsumeContext` beside the existing response members.

### Problem Frame

The two existing middleware stages bracket deserialization but cannot touch it: publish middleware runs on the typed object before serialization; consume middleware runs after deserialization, after inbox admission, and after the transport ack. So envelope validation, upcasting, header normalization, and deserialization-failure interception are impossible without replacing `ISerializer` wholesale. Compounding this, a malformed payload detected at Stage B (dispatch-side deserialization for persisted retries, where `Origin.Value` is a `JsonElement`) throws `InvalidOperationException`, which `SubscribeExecutor` classifies retryable — a deterministic payload defect burns the full inline and persisted retry budget before `OnExhausted` fires. Separately, `MessageHeader` mixes a read-only inbound snapshot with consumer-side response mutations (`AddResponseHeader`, `RemoveCallback`, `RewriteCallback`) that mutate the inbound snapshot's backing dictionary.

### Requirements

**Receive middleware stage**

- R1. A globally registered receive middleware (`AddReceiveMiddleware<T>`) runs for every delivery on both lanes whose consumer was resolved; a typed registration (`AddReceiveMiddlewareFor<TMiddleware, TMessage>(groupName, lane)`) runs only when the consumer payload type, prefixed group, and lane match. Global receive descriptors are lane-agnostic.
- R2. The receive ring runs after subscriber lookup and before contract-version validation and Stage A deserialization; `next` covers version validation, deserialization, and the null-payload check. Subscriber-not-found stays a no-middleware poison-on-arrival path.
- R3. Middleware resolution is registry-only (no DI-scan fallback), from a per-delivery scope created lazily when a descriptor matches and disposed before inbox admission. The zero-middleware path allocates nothing.
- R4. Ordering: global registrations first, then typed; within each, `Priority` ascending then registration order — identical to consume and publish.

**Context, transformation, cancellation**

- R5. `ReceiveContext` exposes immutable identity (`MessageId`, `MessageName`, `GroupName`, `Lane`), consumer metadata (`MessageType`, `ConsumerContractVersion`), read-only views of the current `Headers` and `Body`, and the cancellation token.
- R6. `ReplaceBody`, `SetHeader`, `RemoveHeader` are copy-on-write; writes to the identity headers (`Headers.MessageId`, `MessageName`, `Group`) and `Headers.Exception` throw `InvalidOperationException`; the received envelope (headers and bytes) is retained unmodified for the poison store. `Headers`/`Body` always show what the next component sees.
- R7. `SetHeader`/`RemoveHeader`/`ReplaceBody`/`Skip`/`Reject`/`SetCancellationToken` throw after the inner ring completes; `next` is single-shot. Transformation applies once per delivery: the accepted, transformed `Message` is what admission persists as `Content`, and dispatch retries never re-run receive middleware.
- R8. An `OperationCanceledException` bound to `context.CancellationToken` yields the Cancelled outcome (transport requeue, no storage); a foreign OCE yields Reject.

**Outcome ownership**

- R9. Every ring termination maps to exactly one outcome row of the design's table; `ConsumerRegister` remains the sole owner of transport settlement, storage writes, circuit-breaker reporting, and `OnExhausted`. Middleware never acks, rejects, or writes storage.
- R10. Skip: commit transport, no storage row, no `OnExhausted`, probe released (neither success nor failure), span ok. Reject (via `Reject()`, non-cancellation exception, rethrown deserialization failure, or undeclared outcome — no `next` and no declared outcome): poison-on-arrival with the received (untransformed) envelope as `data:` URI, `OnExhausted` with `Exception = cause`, `StorageId = Guid.Empty`, `RetryCount = 0`; undeclared outcome rejects with a framework exception naming the middleware type. The poison row's `data:` URI is bounded by a framework-side configurable cap (truncate past the cap with a `truncated` marker, omit the body entirely far past it) so a size-cap Reject never writes attacker-controlled bulk to storage. Post-success middleware throw: accept proceeds; failure logged, suppressed.
- R11. Each outcome is observable: one log event per outcome (message id, name, group, lane; middleware type and reason for Skip/Reject, via `LogSanitizer`), a `messaging.receive.outcome` span tag, and a counter with an `outcome` tag.

**Deserialization terminality**

- R12. Stage A wraps serializer failures, and null payloads (empty body) for typed consumers (`MessageValueType != null`), in `MessageDeserializationException` → poison-on-arrival (via the Reject row); untyped consumers keep `Value = null` passing as today. Stage B (`SubscribeInvoker`) throws `MessageDeserializationException` for conversion failure, unsupported value type, and null `Origin.Value`; `SubscribeExecutor` classifies it (including wrapped in `SubscriberExecutionFailedException`) terminal at attempt 1: `MessagingRetryDecision.Exhausted`, row lands `Failed`, `OnExhausted` fires once, no inline/persisted retries, `ShouldHandle` not consulted.

**Response contract**

- R13. `MessageHeader` exposes no mutation members (`AddResponseHeader`, `RemoveCallback`, `RewriteCallback`, internal `ResponseHeader` removed); it is a pure inbound snapshot. Response metadata is settable only through `ConsumeContext`: `SetResponseHeader(key, value)`, `SetResponseDestination(messageName)`, `SuppressResponse()`, beside existing `SetResponse<TResponse>`/`SetResponseCallbackName`, all throwing after completion. `ConsumeMiddlewarePipeline` builds `ConsumerExecutedResult` from context state: `CallbackName = Suppressed ? null : Destination ?? Headers[CallbackName]`; callback headers from context response headers.

**Compatibility**

- R14. Existing publish and consume middleware contracts, ordering, and guarantees are unchanged.

**Documentation**

- R15. `docs/llms/messaging.md` (Middleware section: three stages, outcome table, response contract), `src/Headless.Messaging.Core/README.md`, and `src/Headless.Messaging.Abstractions/README.md` match the resulting API.

### Key Decisions

All four open decisions of the origin design were settled by the user this session; they constrain the requirements above.

- KD1. Stage name "Receive". (session-settled: user-directed — chosen over Envelope/Inbound/Transport: matches `StoreReceivedMessageAsync`/`LeaseReceiveAsync` naming already in Core.) Governs R1–R4.
- KD2. Keep the Skip outcome. (session-settled: user-directed — chosen over dropping it: header-based fan-out filtering on shared topics needs a cheap ack-and-drop without an inbox row.) Governs R10.
- KD3. Stage B deserialization failure is `Exhausted` at attempt 1 and fires `OnExhausted`. (session-settled: user-directed — chosen over Stop (terminal without callback): keeps one dead-letter sink for every deterministic payload defect.) Governs R12.
- KD4. Remove `MessageHeader` mutators outright. (session-settled: user-directed — chosen over obsoleting them: greenfield framework, only test callers today, breaking changes preferred over compatibility shims.) Governs R13.

### Scope Boundaries

Out of scope (separate issue): the global-middleware lane filter fix from the design's Findings Outside This Issue (`AddBusConsumeMiddleware` registers `Lane = Bus`; Queue-lane "global" middleware runs only via the untracked DI-scan fallback).

Deferred per the origin design: outbound envelope stage (revisit trigger recorded there), requeue/defer outcome, foreign-envelope adoption, `IConsumeFilter` revival.

### Acceptance Examples

- AE1. **Covers R6, R9.** Given a receive middleware that calls `SetHeader("x-signature-valid", "true")` then `next()`, when a valid envelope is delivered, then the persisted message's stored `Content` carries the added header and the transport is committed after admission.
- AE2. **Covers R9, R10.** Given a receive middleware that returns without `next()` and without `Skip`/`Reject`, when any envelope is delivered, then the delivery is committed as poison-on-arrival with a framework exception naming that middleware type, and `OnExhausted` receives that exception.
- AE3. **Covers R12.** Given a consumer whose persisted retry row carries a payload that fails Stage B conversion, when dispatch runs, then the row becomes `Failed` on the first attempt with `OnExhausted` invoked exactly once and no retry scheduled.

---

## Planning Contract

### Key Technical Decisions

- KTD1. Registry-only resolution with `MiddlewareDirection.Receive` added to the existing `MiddlewareDescriptorRegistry`; global receive descriptors ignore the lane filter (KD1's lane-agnostic global). This deliberately diverges from consume/publish's DI-scan fallback so the stage never inherits the lane defect in Scope Boundaries.
- KTD2. The receive ring is inlined into `ConsumerRegister.onMessageCallback`'s existing poison `try`: the bracketed inner steps (version check → deserialize → null-payload check) become the ring's `next`. No new pipeline class; `ConsumerRegister` keeps single ownership of settlement.
- KTD3. `ReceiveContext` is non-generic with copy-on-write envelope views and completion-flag gating (a completion flag flipped when `next` returns), mirroring `PublishContext`'s post-completion mutation rules. State mutation members throw `InvalidOperationException` after completion; identity-header writes always throw.
- KTD4. Stage A wraps failures in `MessageDeserializationException` (public, `Headless.Messaging.Exceptions`); Stage B throws the same type; `SubscribeExecutor` detects it (including unwrapped from `SubscriberExecutionFailedException`) and routes to the exhausted path — the terminal classification is detection-based, not `ShouldHandle`-based (KD3).
- KTD5. Response contract state (`ResponseHeaders` dictionary, `ResponseDestination`, `ResponseSuppressed`) lives on `ConsumeContext` as internal members beside the existing `Response`/`ResponseCallbackName`, exposed through the three new public methods; `ConsumeMiddlewarePipeline` stops reading `MessageHeader.ResponseHeader`/`Headers.CallbackName` for the result and reads context state (KD4).
- KTD6. Observability: `LoggerExtensions` gain receive-outcome events (`LoggerMessage` source-generated, sanitized); a `messaging.receive.outcome` tag on the existing trace handle; an outcome counter through the established `MessagingMetrics` pattern.
- KTD8. Poison-envelope cap: the received-exception row's `data:` URI is bounded by a framework-side option (default a sane cap) with truncate/omit semantics, because explicit policy rejects must not amplify storage writes; explicit `Reject(reason, cause)` decisions release the probe like Skip (no breaker report), while thrown-exception and undeclared-outcome rejects still report failure — a deliberate amendment of the origin design's outcome table (session-settled with the user after security review).
- KTD7. Runtime subscriptions (no durable identity) flow through the same receive ring; `ConsumerContractVersion` is `null` for them, matching the design.

### Assumptions

- The origin design doc, authored against `9a6a604d5`, is accurate for the base of this branch (`9f8d0af98`): verified — no diffs on the touched projects between the two commits.
- `Headless.Messaging.Testing`'s `RecordingTransport` does not flow through `ConsumerRegister` (it calls `ISerializer.DeserializeAsync` directly); receive middleware is not exercised by that path. The plan documents this in U7 rather than changing the testing package.
- The null-payload Stage A rejection is a behavior change (empty bodies for typed consumers currently pass through as `Value = null` and fail later at Stage B; untyped consumers are unchanged); the design sanctions it and U4's tests pin it.

### Sequencing

U1 (contract types) → U2 (registry/builder) → U3 (context/ring in ConsumerRegister) → U4 (Stage A wrapping) → U5 (Stage B terminality) → U6 (response contract) → U7 (harness conformance) → U8 (docs). U4–U6 are independent of each other once U3/U1 land; the stated order is the dependency-safe one.

---

## Implementation Units

### U1. Public contract types

- **Goal:** `IReceiveMiddleware`, `ReceiveContext`, and `MessageDeserializationException` exist with the design's exact surface and XML docs.
- **Requirements:** R5, R6 (member surface and throw contracts), plus the exception type for R12.
- **Files:** create `src/Headless.Messaging.Core/IReceiveMiddleware.cs`, `src/Headless.Messaging.Core/ReceiveContext.cs`, `src/Headless.Messaging.Core/Exceptions/MessageDeserializationException.cs`.
- **Approach:** `ReceiveContext` as a sealed class with an internal constructor (built by U3's ring), completion-flag-gated mutators per KTD3, copy-on-write header dictionary (`IReadOnlyDictionary<string, string?>` view). Identity header set = `Headers.MessageId`, `MessageName`, `Group`, `Exception`. Guard clauses via `Headless.Checks` (`Argument.*`).
- **Patterns to follow:** `IPublishMiddleware.cs`/`PublishContext.cs` for middleware/context shape and XML-doc tone; `Exceptions/SubscriberNotFoundException.cs` for the exception file.
- **Test scenarios:**
  - Completed context: `SetHeader`/`RemoveHeader`/`ReplaceBody`/`Skip`/`Reject`/`SetCancellationToken` each throw `InvalidOperationException`; identity-header `SetHeader`/`RemoveHeader` throws regardless of completion.
  - Copy-on-write: first `SetHeader` does not mutate the received headers dictionary; `Headers` view reflects prior writes for a later reader; `ReplaceBody` swaps the view.
  - `Skip`/`Reject` record reason/cause retrievable internally; calling both throws `InvalidOperationException`.
  - `SetCancellationToken` replaces the token before completion.
- **Verification:** `make build-project PROJECT=src/Headless.Messaging.Core/Headless.Messaging.Core.csproj`; new unit tests in `tests/Headless.Messaging.Core.Tests.Unit/ReceiveContextTests.cs` pass via `make test-project-fast`.

### U2. Registry and builder registration

- **Goal:** `MiddlewareDirection.Receive` exists in the descriptor registry with `TryGetReceiveDescriptors(type, group, lane)`; `MessagingBuilder.AddReceiveMiddleware<T>()` and `AddReceiveMiddlewareFor<TMiddleware, TMessage>(groupName, lane)` register descriptors with scoped services and return `MiddlewareRegistration`.
- **Requirements:** R1, R3, R4.
- **Dependencies:** U1.
- **Files:** modify `src/Headless.Messaging.Core/Configuration/IMiddlewareDescriptorRegistry.cs`, `src/Headless.Messaging.Core/Configuration/MessagingBuilder.cs`, `src/Headless.Messaging.Core/Configuration/MessagingOptions.cs` (extend `_ValidateMiddlewareDescriptors`).
- **Approach:** Add the enum value and lookup following `TryGetConsumeDescriptors`' shape; global receive descriptors skip the lane filter (KTD1). Group-prefix via `ApplyGroupNamePrefix` like `AddConsumeMiddlewareFor`. Both overloads carry `where T : class, IReceiveMiddleware`, so no runtime type validation is needed or built (unlike consume's open context-type generic).
- **Patterns to follow:** `AddConsumeMiddlewareFor` in `MessagingBuilder.cs`; `_ValidateMiddlewareDescriptors` in `MessagingOptions.cs`.
- **Test scenarios:**
  - Typed descriptor matches exact `(type, prefixed group, lane)`; non-matching group or lane excluded.
  - Global descriptor returned for both lane values.
  - Ordering global-then-typed, priority then registration order.
  - Group prefix applied when `MessagingOptions.GroupNamePrefix` set.
- **Verification:** unit tests in `tests/Headless.Messaging.Core.Tests.Unit/` (extend the middleware-registry test file or add one) pass; build green.

### U3. Receive ring and outcome mapping in ConsumerRegister

- **Goal:** The receive ring executes inside `onMessageCallback` between subscriber lookup and version validation, with per-delivery scoped resolution and all six outcome rows implemented per the design's table.
- **Requirements:** R2, R3 (scope), R7 (single-shot `next`, post-completion throws), R8, R9, R10, R11.
- **Dependencies:** U1, U2.
- **Files:** modify `src/Headless.Messaging.Core/Internal/IConsumerRegister.cs`, `src/Headless.Messaging.Core/Internal/LoggerExtensions.cs`, `src/Headless.Messaging.Core/MessagingMetrics.cs` (outcome counter), tracing tag on the existing trace handle.
- **Approach:** Per KTD2: extract the bracketed inner steps into a local function used as `next`; build `ReceiveContext` from `transportMessage` + `executor` + host-shutdown token; run ring when descriptors match, else invoke inner directly (zero-middleware fast path unchanged); map termination to the outcome rows. Scope: `serviceScopeFactory.CreateAsyncScope()` before the ring, disposed before admission; receive scope does not flow into dispatch. Keep the received `TransportMessage` (headers + body) for the Reject row's `data:` URI, persisted under the R10 size cap. Foreign OCE → Reject; bound OCE → Cancelled (existing requeue path). Observability per KTD6.
- **Patterns to follow:** the zero-middleware fast path and `StrongBox` ring assembly in `ConsumeMiddlewarePipeline.cs`.
- **Test scenarios:**
  - Accept: middleware calls `next`; admission + commit + dispatch proceed exactly as today (assert stored row, committed transport).
  - Skip: `Skip(reason)` without `next` → commit, no storage row, no `OnExhausted`, probe released; log carries reason.
  - Reject via `Reject()` — commits and stores the capped received-exception row; `OnExhausted` receives cause; probe released (neither success nor failure — a policy decision from attacker-controllable input must not advance the breaker).
  - Reject via thrown exception or undeclared outcome (framework exception names middleware type) — commits and stores the capped received-exception row; `OnExhausted` receives cause; circuit-breaker failure reported.
  - Poison `data:` URI respects the cap: a body at/below the cap stores whole; past it stores truncated with a `truncated` marker; far past it stores headers only.
  - Cancelled: bound OCE → transport reject (requeue), no storage, span disposed un-errored.
  - Foreign OCE → Reject row.
  - Post-success throw: `next` completed then middleware throws → accept proceeds, failure logged, executed result still produced.
  - Scope: middleware resolved from fresh scope; disposed before admission; zero-middleware path creates no scope.
  - Cancellation recheck after each middleware returns (token swapped via `SetCancellationToken` then cancelled → Cancelled).
  - Transformed accept: `SetHeader` before `next` → persisted `Content` carries it. Covers AE1.
  - Second `next()` call throws `InvalidOperationException`.
- **Verification:** unit tests in `tests/Headless.Messaging.Core.Tests.Unit/ConsumerRegisterTests.cs` (extend) pass; `make build-project` green.

### U4. Stage A deserialization wrapping and null-payload rejection

- **Goal:** Serializer failures and empty-body/null payloads at Stage A become `MessageDeserializationException`, routed through the Reject outcome; version-mismatch exceptions surface through `next` unchanged.
- **Requirements:** R2 (null-payload check inside `next`), R10 (reject rows), R12 (Stage A half).
- **Dependencies:** U3.
- **Files:** modify `src/Headless.Messaging.Core/Internal/IConsumerRegister.cs` (Stage A block).
- **Approach:** Wrap `DeserializeAsync` in try/catch rethrowing as `MessageDeserializationException` (preserving inner); after deserialize, `Message.Value is null` when `executor.MessageValueType != null` → throw `MessageDeserializationException`. Both flow to the existing poison path; exception type recorded in `Headers.Exception` as today.
- **Patterns to follow:** existing Stage A catch block (`dispatchBypassException` handling).
- **Test scenarios:**
  - Invalid JSON body for resolved consumer → `MessageDeserializationException`, poison row stored, committed, `OnExhausted` once.
  - Empty body with typed consumer → `MessageDeserializationException` (behavior change pinned).
  - Empty body with untyped (`MessageValueType == null`) consumer → `Value = null` passes as today.
  - Version mismatch inside `next` propagates to middleware's catch and to the poison path when uncaught.
- **Verification:** unit tests pass; build green.

### U5. Stage B terminal classification

- **Goal:** `SubscribeInvoker` throws `MessageDeserializationException` for its three failure branches, and `SubscribeExecutor` makes it terminal at attempt 1 (`Exhausted`, `OnExhausted` once, no retries, `ShouldHandle` not consulted).
- **Requirements:** R12.
- **Dependencies:** U1.
- **Files:** modify `src/Headless.Messaging.Core/Internal/ISubscribeInvoker.cs`, `src/Headless.Messaging.Core/Internal/ISubscribeExecutor.cs`, tests `tests/Headless.Messaging.Core.Tests.Unit/SubscribeInvokerTests.cs`.
- **Approach:** Per KTD4: replace the three `InvalidOperationException` throws with `MessageDeserializationException`. In `SubscribeExecutor`, detect the type inside the retry pipeline's attempt result (unwrapping `SubscriberExecutionFailedException` where its `InnerException` is `MessageDeserializationException`) and route to the exhausted path (`RetryHelper.ResolveNextState` with `Exhausted` decision; `_PersistFailedStateAsync` fires `RunOnExhaustedAsync` on `affected`). Bypass `ShouldHandle` classification for this type — a non-retryable attempt result whose decision is pre-set to Exhausted, or the narrowest equivalent seam, so the Polly `_ShouldHandleAsync` predicate is never asked. Exact seam choice is execution-time.
- **Patterns to follow:** existing `_SetFailedState`/`_PersistFailedStateAsync` exhausted branches; `RetryHelper.RunOnExhaustedAsync`.
- **Test scenarios:**
  - Stage B conversion failure (`JsonElement` → type mismatch): row `Failed` on first attempt; `OnExhausted` once; no `NextRetryAt` scheduled; retry counter not advanced. Covers AE3.
  - Unsupported value type branch and null `Origin.Value` branch throw `MessageDeserializationException`.
  - `SubscriberExecutionFailedException`-wrapped case classified terminal identically.
  - Non-deserialization handler failure still classified per `ShouldHandle` (regression guard for R14).
  - Callback/response flow unaffected when consume succeeds.
- **Verification:** unit tests pass; `make test-project-fast TEST_PROJECT=tests/Headless.Messaging.Core.Tests.Unit/Headless.Messaging.Core.Tests.Unit.csproj`.

### U6. Read-only MessageHeader and ConsumeContext response contract

- **Goal:** `MessageHeader` mutation members removed; response metadata moves to `ConsumeContext` (`SetResponseHeader`, `SetResponseDestination`, `SuppressResponse`); `ConsumeMiddlewarePipeline` builds `ConsumerExecutedResult` from context state.
- **Requirements:** R13.
- **Dependencies:** U1.
- **Files:** modify `src/Headless.Messaging.Abstractions/MessageHeader.cs`, `src/Headless.Messaging.Abstractions/ConsumeContext.cs`, `src/Headless.Messaging.Core/Internal/ConsumeMiddlewarePipeline.cs`, tests `tests/Headless.Messaging.Abstractions.Tests.Unit/MessageHeaderTests.cs`, `tests/Headless.Messaging.Core.Tests.Unit/SubscribeInvokerTests.cs`.
- **Approach:** Per KTD5: delete the three public mutators and the internal `ResponseHeader` property; add internal state + three public methods on `ConsumeContext` with the same completion guard as `SetResponse`. In `ConsumeMiddlewarePipeline.ExecuteInScopeAsync`, replace the `consumeHeaders.TryGetValue(Headers.CallbackName, …)`/`ResponseHeader` reads with context-state reads (`CallbackName = Suppressed ? null : Destination ?? header-value`; `CallbackHeader` from context `ResponseHeaders`). `SubscribeExecutor._InvokeConsumerMethodAsync` reads the result's `CallbackHeader` as today — traceparent stamping continues to ride it.
- **Patterns to follow:** `SetResponseCallbackName`'s guard and XML-doc shape on `ConsumeContext`.
- **Test scenarios:**
  - `SetResponseHeader` accumulates into `ConsumerExecutedResult.CallbackHeader`; duplicate key overwrites; `null` value allowed.
  - `SetResponseDestination("x")` with inbound `Headers.CallbackName = "orig"` → callback published to `x` with `CausationId`/`CorrelationSequence` from the inbound message (unchanged lineage rules).
  - `SuppressResponse()` with inbound callback name → no callback publish.
  - All three throw after `MarkCompleted`.
  - `MessageHeader` no longer exposes mutation members (compile-level; tests updated to the new surface).
  - Existing callback tests rewritten from `RewriteCallback`/`RemoveCallback`/`AddResponseHeader` to the new members, asserting identical published results.
- **Verification:** Abstractions + Core unit tests pass (`make test-project-fast` both projects); `make build-project` for both.

### U7. Harness conformance scenarios

- **Goal:** One conformance scenario per outcome (Accept, Skip, Reject, Cancelled) runs through the in-memory transport end-to-end in the shared harness so provider suites inherit it.
- **Requirements:** R9, R10 (end-to-end observability of the outcome rows).
- **Dependencies:** U3, U4.
- **Files:** create the assert-style receive-outcome conformance suite in `tests/Headless.Messaging.Core.Tests.Harness/`; add the concrete `[Fact]` runners in `tests/Headless.Messaging.InMemory.Tests.Unit/` (which already references the harness and follows `InMemoryProviderConformanceTests`' driver pattern — the harness project itself is `IsTestProject=false` and runs nothing alone).
- **Approach:** Publish through the in-memory transport with a receive middleware forcing each outcome; assert transport settlement (commit/requeue) and storage state (admitted row / received-exception row / none). Follow `TestBase`/`AbortToken` conventions; extend the harness README only if its contract changes. Limitation (per Assumptions): `Headless.Messaging.Testing`'s `RecordingTransport` bypasses `ConsumerRegister`, so recorded deliveries never exercise receive middleware — these scenarios run through the in-memory transport for that reason. Covers AE2 end-to-end (undeclared-outcome reject is the loudest row).
- **Patterns to follow:** existing harness suites (dispatch/outbox scenarios).
- **Test scenarios:**
  - Accept end-to-end: consumer handler invoked; admitted row visible.
  - Skip end-to-end: handler never invoked; no inbox row; transport committed.
  - Reject end-to-end: received-exception row; `OnExhausted` invoked; handler never invoked.
  - Cancelled end-to-end: bound-token cancellation before `next`; no rows; requeue observed.
- **Verification:** `make test-project-fast TEST_PROJECT=tests/Headless.Messaging.InMemory.Tests.Unit/Headless.Messaging.InMemory.Tests.Unit.csproj` (no Docker needed — the in-memory host is process-local).

### U8. Documentation sync

- **Goal:** Both agent-facing doc surfaces match the new API per the repo's AUTHORING rules.
- **Requirements:** R15.
- **Dependencies:** U1–U7.
- **Files:** modify `docs/llms/messaging.md`, `src/Headless.Messaging.Core/README.md`, `src/Headless.Messaging.Abstractions/README.md`, `CONCEPTS.md`.
- **Approach:** Middleware section gains the receive stage (placement, registration, context contract, outcome table, observability); response-contract prose moves to the `ConsumeContext` members; `MessageHeader` documented as a pure inbound snapshot; the origin design doc stays as-is (historical record). CONCEPTS.md: the poison-on-arrival entry is already drafted uncommitted in this branch's working tree — verify it matches the final outcome table and terminology U1-U6 implement, update only on drift, and commit it with this unit.
- **Patterns to follow:** existing Middleware section structure in `docs/llms/messaging.md`; AUTHORING drift rules.
- **Test scenarios:** Test expectation: none — documentation-only unit.
- **Verification:** doc drift checks per AUTHORING (links/anchors verified by inspection; `make format-check` unaffected).

---

## Verification Contract

- Build: `make build-project PROJECT=src/Headless.Messaging.Core/Headless.Messaging.Core.csproj` and the same for `src/Headless.Messaging.Abstractions/Headless.Messaging.Abstractions.csproj`.
- Unit: `make test-project-fast TEST_PROJECT=tests/Headless.Messaging.Core.Tests.Unit/Headless.Messaging.Core.Tests.Unit.csproj`; same for `tests/Headless.Messaging.Abstractions.Tests.Unit`; the receive conformance scenarios run via `make test-project-fast TEST_PROJECT=tests/Headless.Messaging.InMemory.Tests.Unit/Headless.Messaging.InMemory.Tests.Unit.csproj`.
- Analyzers: `make quality-analyzers-project PROJECT=src/Headless.Messaging.Core/Headless.Messaging.Core.csproj` and the Abstractions equivalent.
- Full-solution `make test` is not required by this plan; CI runs `make ci-test` (unit only). Integration suites for touched providers are not modified by this change; per repo learning (2026-07-21), if provider behavior changes surface later, run the affected `*.Tests.Integration` locally.
- Behavioral-skill evaluation: none required.

## Definition of Done

- All units U1–U8 complete with their per-unit verification green; coverage of new code meets the repo targets (≥85% line / ≥80% branch aspiration; ≥80/≥70 floors).
- No regression in existing middleware behavior: consume/publish middleware tests unchanged except where U6's contract change rewrites response-header/callback tests to the new members (R14).
- PR to `main` open with the origin design doc committed alongside the implementation; CI (`CI status` gate) green; `main` requires only that gate per repo learning.
- Cleanup: no dead-end or experimental code left in the diff (e.g., no leftover `MessageHeader.ResponseHeader` readers, no unused overloads); the CONCEPTS.md entry committed and current.
