---
date: 2026-06-03
topic: callback-return-value
---

# Callback Return-Value Capture

## Summary

Give a consumer a way to produce a typed response body — `context.SetResponse(value)` — that the framework publishes to the message's `CallbackName` on the durable bus path, serialized as its concrete type so the response consumer can deserialize it. This restores the "the consumer's return value is published to the callback" promise that the docs claim but the code never delivers (the callback body is always `null`). Works for both bus and queue requests and leaves the existing callback controls intact.

## Problem Frame

The callback / async-response-routing feature documents that the consumer's return value is automatically published to the callback message name. That is false in the current code. `IConsume<TMessage>.ConsumeAsync` returns `ValueTask` — there is no return value to capture — and `ConsumeMiddlewarePipeline` constructs `ConsumerExecutedResult` with `result: null` in both branches. The callback leg then publishes a null-bodied message; only response *headers* (via `AddResponseHeader`) carry data today.

This is vestigial DotNetCore.CAP plumbing. CAP reflection-invoked consumer methods whose `return` value populated `Result`; the port to the compile-time `IConsume<T>` interface dropped the return-value capture but kept the `Result` property and the `PublishAsync(ret.Result)` call. The cost is a shipped, documented capability that silently does nothing — a consumer wiring up a callback gets an empty payload and no error. The interim fix walks the docs back to "headers-only"; this work restores real payload chaining and reverts that wording.

## Key Decisions

- **Response is declared imperatively via `context.SetResponse(value)`, not via a typed `IConsume<TMessage, TResult>` return.** Callbacks are optional, conditional async chaining — a consumer often decides at runtime whether to respond. An imperative setter matches that, and joins the existing context-method family (`RemoveCallback`, `RewriteCallback`, `AddResponseHeader`). It also avoids surgery on `CompiledMessageDispatcher`, which is keyed on `TMessage` alone and has no `TResult` at its generic call site; a typed-return interface would force a parallel dispatch path and an awkward "no response" case. The existing `ValueTask` dispatch path stays unchanged.

- **The response leg always rides the durable bus path (`IOutboxBus`), even for queue-originated requests.** This preserves today's behavior and prioritizes durability of the response over point-to-point symmetry with the request intent.

- **`SetResponse<T>` captures the static type `T`, not just the boxed value.** The captured value flows through `ConsumerExecutedResult.Result` (typed `object?`). To satisfy the "publish as the concrete type, not `object`" requirement, the type must be carried alongside the value so the publish leg serializes correctly. This is the load-bearing constraint for planning.

- **Two edge defaults preserve the existing headers-only path:** `CallbackName` set with no `SetResponse` call publishes a null body plus headers (unchanged); `SetResponse` called with no `CallbackName` drops the response silently (nowhere to send it).

## Requirements

**Consumer API**

- R1. A consumer can set a typed response body from inside `ConsumeAsync` via a `SetResponse` method on the consume context. The method is generic over the response type so the static type is captured.
- R2. Calling `SetResponse` is optional. A consumer that never calls it behaves exactly as today (see R7).
- R3. The existing consumer-side controls — `RemoveCallback`, `RewriteCallback`, `AddResponseHeader` — continue to work and compose with `SetResponse` (e.g. set a response body *and* rewrite the callback target).

**Response publishing**

- R4. When a consumer set a response and the message has a `CallbackName`, the framework publishes the response body to that callback name on the durable bus path.
- R5. The response message is serialized using the captured response type, not `object`, so the response consumer can deserialize it to the concrete type.
- R6. Existing callback correlation behavior is preserved: `CorrelationId`, incremented `CorrelationSequence`, and propagated `TraceParent` are set on the callback message.
- R7. When a consumer did not set a response but a `CallbackName` is present, the callback is still published with a null body and the response headers — the current headers-only behavior is unchanged.
- R8. When a consumer set a response but no `CallbackName` is present, the response is discarded without error.

**Intents and fan-out**

- R9. Return-value chaining works for requests originating from both the bus path (`IBus` / `IOutboxBus`) and the queue path (`IQueue` / `IOutboxQueue`). The response leg stays on the bus path in both cases.
- R10. In the bus fan-out case (N subscribers to one message), each subscriber independently produces its own response; N subscribers that each set a response yield N callback messages.

**Demo, tests, docs**

- R11. A messaging demo (e.g. `demo/Headless.Messaging.Console.Demo`) shows the round trip: a request consumer sets a typed result, a response consumer deserializes the body.
- R12. Integration coverage exists for the return-value round trip on both bus and queue, including the fan-out case and `RemoveCallback` / `RewriteCallback`. A `dev-test-plan` document is produced before the tests are written.
- R13. `docs/llms/messaging.md` and the matching `src/Headless.Messaging.*/README.md` are updated to describe real payload chaining, reverting the interim headers-only wording, per the authoring-lockstep rule in `CLAUDE.md`.

## Key Flows

- F1. Bus request with typed response
  - **Trigger:** A message published via `IBus` / `IOutboxBus` with a `CallbackName` is dispatched to a subscriber.
  - **Steps:** Consumer runs business logic; consumer calls `context.SetResponse(result)`; middleware captures the value and its type into `ConsumerExecutedResult.Result`; the executor publishes `result` to `CallbackName` on `IOutboxBus` with correlation headers; the response consumer receives and deserializes the typed body.
  - **Covered by:** R1, R4, R5, R6, R9

- F2. Queue request with typed response
  - **Trigger:** A message enqueued via `IQueue` / `IOutboxQueue` with a `CallbackName` is dispatched to the winning worker.
  - **Steps:** Same as F1, except the request arrives point-to-point; the response leg still publishes on the durable bus path.
  - **Covered by:** R1, R4, R5, R9

- F3. Bus fan-out
  - **Trigger:** A bus message with a `CallbackName` has N registered subscribers.
  - **Steps:** Each subscriber is dispatched independently with its own context; each may call `SetResponse`; each response is published to `CallbackName` independently, producing up to N callback messages.
  - **Covered by:** R10

## Acceptance Examples

- AE1. **Covers R4, R5.** Given a consumer that calls `SetResponse(new OrderReceipt(...))` and a message with `CallbackName = "order.receipt"`, when the consumer completes, then a message is published to `order.receipt` whose body deserializes to `OrderReceipt` (not `object`).
- AE2. **Covers R7.** Given a consumer that never calls `SetResponse` and a message with a `CallbackName`, when the consumer completes, then the callback is published with a null body and any response headers the consumer added — matching current behavior.
- AE3. **Covers R8.** Given a consumer that calls `SetResponse(value)` on a message with no `CallbackName`, when the consumer completes, then no callback is published and no error is raised.
- AE4. **Covers R3.** Given a consumer that calls `SetResponse(value)` and `RewriteCallback("audit.topic")`, when the consumer completes, then the response body is published to `audit.topic`.
- AE5. **Covers R10.** Given two subscribers to one bus message that each call `SetResponse` with different values, when both complete, then two callback messages are published to the callback name, one per subscriber's value.

## Scope Boundaries

**Outside this product's identity**

- Request/reply where the caller awaits a response (RPC). Callbacks are fire-and-forget async message chaining; the caller never awaits. This stays true.
- A typed `IConsume<TMessage, TResult>` return-value interface. `SetResponse` is the single mechanism; a second, type-returning contract is explicitly rejected to keep one way to do this.

**Deferred / not needed**

- Changes to `CallbackName` plumbing. It already lives on the shared `MessageOptions` base and is available on both `PublishOptions` and `EnqueueOptions` — no change required.
- Changes to the core `ValueTask` dispatch path in `CompiledMessageDispatcher`. The chosen API leaves it untouched.

## Dependencies / Assumptions

- The response value travels through `ConsumerExecutedResult.Result`, which is typed `object?`. The implementation must carry the captured static type separately (or change that slot) so R5 holds — this is an assumption planning must resolve concretely.
- The interim doc change (walking `docs/llms/messaging.md` to headers-only) is assumed to be merged or in progress; R13 reverts it.

## Sources

- Issue #392 — `feat(messaging): restore return-value capture for callback/response routing` (option B).
- `src/Headless.Messaging.Abstractions/IConsume.cs` — consumer contract (`ValueTask`, no return).
- `src/Headless.Messaging.Core/Internal/CompiledMessageDispatcher.cs` — expression-compiled dispatch keyed on `TMessage`.
- `src/Headless.Messaging.Core/Internal/ConsumeMiddlewarePipeline.cs` — the two `result: null` branches.
- `src/Headless.Messaging.Core/Internal/ConsumerExecutedResult.cs` — the `Result` slot.
- `src/Headless.Messaging.Core/Internal/ISubscribeExecutor.cs` — callback publish via `IOutboxBus.PublishAsync(ret.Result, …)` with correlation headers.
- `src/Headless.Messaging.Abstractions/MessageHeader.cs` — `RemoveCallback`, `RewriteCallback`, `AddResponseHeader`.
- Related brainstorms: `docs/brainstorms/2026-05-25-messaging-consumer-model-evolution-requirements.md`, `docs/brainstorms/2026-06-02-scanned-consumer-callback-requirements.html`.
