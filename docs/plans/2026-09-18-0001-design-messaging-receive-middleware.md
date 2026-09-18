---
title: Messaging Receive Middleware (Pre-Deserialization Boundary) - Design
type: design
date: 2026-09-18
issue: https://github.com/xshaheen/headless-framework/issues/276
status: proposed
---

# Messaging Receive Middleware (Pre-Deserialization Boundary) - Design

## Overview

`Headless.Messaging.Core` has two middleware stages: publish (typed object, before serialization) and consume (typed object, after deserialization). Nothing user-owned runs between the broker handing over a `TransportMessage` and the framework deserializing it, so envelope validation, upcasting, header normalization, and deserialization-failure handling are impossible without replacing `ISerializer` wholesale.

This design adds **one** inbound stage, **receive middleware**, that runs on the raw envelope after the consumer is resolved and before deserialization, with exactly four deterministic outcomes owned by the framework. It also finishes the response contract on `ConsumeContext` so `MessageHeader` becomes a pure inbound snapshot, and it aligns the second (dispatch-side) deserialization point with the first so a malformed payload has one outcome regardless of where it is detected.

No outbound envelope stage is added. Symmetric byte codecs (compression, encryption, signing) are served by decorating `ISerializer`; the trigger for revisiting that is recorded under Deferred.

## Current State (evidence)

Inbound path per transport delivery, `src/Headless.Messaging.Core/Internal/IConsumerRegister.cs`:

| Step | Line | Behavior |
| --- | --- | --- |
| Transport builds `TransportMessage` | provider | RabbitMQ terminally rejects (`requeue: false`) envelopes missing `MessageId`/`MessageName` before Core sees them (`RabbitMqBasicConsumer.cs:191-232`). |
| Circuit-breaker probe admission | 940-974 | Probe denied → transport reject. |
| Subscriber lookup | 990 | `(name, group, lane)` → `ConsumerExecutorDescriptor`. Not found → poison-on-arrival. |
| Contract version check | 1014 | Registered version ≠ received → throws → poison-on-arrival. |
| **Stage A deserialization** | 1016 | `ISerializer.DeserializeAsync(TransportMessage, MessageValueType)`. Throws → poison-on-arrival. Empty body → `Message.Value = null` (`JsonUtf8Serializer.cs:36-39`), **not** treated as a failure here. |
| Poison-on-arrival | 1052-1154 | Stamp `Headers.Exception`, store received-exception row with a `data:` URI of the original bytes, **commit (ack) transport**, invoke `RetryPolicy.OnExhausted` with `StorageId = Guid.Empty`, `RetryCount = 0`. Never dispatched, never retried. |
| Accept | 1156-1263 | Inbox admission (dedupe), ack, enqueue to dispatcher. |
| Unexpected exception (storage, etc.) | 1265-1285 | Transport reject (requeue) unless already settled; OCE ends the span without error. |

Dispatch path, `ISubscribeInvoker.cs:41-102` (**Stage B deserialization**): `Origin.Value` is the CLR instance on fresh dispatch but a `JsonElement` on persisted retries (storage round-trip via `ISerializer.Deserialize(string)`). Conversion failure, the "Unsupported message value type" branch, and `Value == null` all throw `InvalidOperationException`, which `SubscribeExecutor` wraps as `SubscriberExecutionFailedException` and classifies **retryable** (`ISubscribeExecutor.cs:304-314`). A deterministic payload defect therefore burns the inline and persisted retry budget before reaching `OnExhausted`.

Consume pipeline, `ConsumeMiddlewarePipeline.cs:107-170`: builds a `MessageHeader` snapshot per consume; reads `Headers.CallbackName` and `ResponseHeader` back out of that snapshot to build `ConsumerExecutedResult`. `MessageHeader.RemoveCallback`/`RewriteCallback` mutate the snapshot's backing dictionary (`MessageHeader.cs:47-60`). Their only callers are tests (`SubscribeInvokerTests.cs:954,964`). `ConsumeContext` already owns `SetResponse<T>` and `SetResponseCallbackName` (`ConsumeContext.cs:74-107`).

Outbound bytes are produced at two sites only, `DirectPublisherCore.cs:30` and `IMessageSender.cs:184`, both through `ISerializer.SerializeToTransportMessageAsync`, which is a consumer-replaceable DI contract.

Registration and matching, `MessagingBuilder.cs:113-231` and `IMiddlewareDescriptorRegistry.cs:112-157`: one `MiddlewareDescriptorRegistry` keyed by `(Direction, Scope, MiddlewareType, ContextType, MessageType, GroupName, Lane)`; global-first-then-typed ordering, `Priority` then registration order; middleware services are `Scoped` and resolved from the consume scope.

## Use Cases (recorded)

Use cases that need the raw envelope and consumer identity, and cannot be met by consume middleware or an `ISerializer` decorator:

| # | Use case | Needs | Served by |
| --- | --- | --- | --- |
| U1 | Reject an envelope by policy before any storage write: body-size cap, required custom header (signature, producer id), disallowed producer. | consumer identity, raw headers/body, deterministic reject | Receive middleware: `Reject` |
| U2 | Skip envelopes this consumer does not want without deserializing or writing an inbox row (header-based fan-out filtering on a shared topic). | headers, cheap ack-and-drop | Receive middleware: `Skip` |
| U3 | Upcast an older contract version to the registered version (v1 JSON → v2 JSON) and stamp `ContractVersion` before the version check. | consumer's registered version, body rewrite, header rewrite, run before validation | Receive middleware: `ReplaceBody` + `SetHeader` |
| U4 | Normalize headers from a producer that uses different custom header names (not the identity headers, which the transport already requires). | header rewrite | Receive middleware: `SetHeader`/`RemoveHeader` |
| U5 | Observe or classify a deserialization failure with consumer context before the poison store (custom quarantine sink, enriched diagnostics). | catch the failure with consumer identity | Receive middleware: `try/catch` around `next()` |
| U6 | Verify an HMAC/signature over the exact received bytes. | original bytes, consumer identity | Receive middleware (verification); signing side via `ISerializer` decorator |
| U7 | Compress/encrypt on the wire, symmetric on both sides. | bytes on publish and receive | `ISerializer` decorator, both directions in one type (not this stage) |

The `OnExhausted` callback already receives poison-on-arrival envelopes; it is a terminal sink, not an interception point, and stays as is.

## Design

### Placement

Receive middleware runs inside the existing poison-on-arrival `try` in `onMessageCallback`, at this position:

```
probe admission → subscriber lookup → [receive middleware ring → contract-version check → Stage A deserialization → null-payload check] → inbox admission → ack → dispatch
```

The ring's `next` covers the bracketed inner steps. Placing the ring after subscriber lookup gives every middleware the consumer's identity (matching, U1–U6); placing it before the version check lets an upcaster rewrite `ContractVersion` (U3). Subscriber-not-found stays a poison-on-arrival outcome that runs no middleware: it is a configuration defect, and no consumer exists to match against.

### Public API (Headless.Messaging.Core, namespace `Headless.Messaging`)

```csharp
[PublicAPI]
public interface IReceiveMiddleware
{
    ValueTask InvokeAsync(ReceiveContext context, Func<ValueTask> next);
}

[PublicAPI]
public sealed class ReceiveContext
{
    // Identity — immutable, enforced on header writes.
    public string MessageId { get; }
    public string MessageName { get; }
    public string GroupName { get; }
    public MessageLane Lane { get; }

    // Consumer contract metadata from the matched descriptor.
    public Type MessageType { get; }               // consumer's CLR payload type
    public string? ConsumerContractVersion { get; } // null for runtime subscriptions

    // Current envelope as the next component will see it. Read-only views.
    public IReadOnlyDictionary<string, string?> Headers { get; }
    public ReadOnlyMemory<byte> Body { get; }

    public CancellationToken CancellationToken { get; }
    public void SetCancellationToken(CancellationToken cancellationToken);

    // Explicit transformation. Copy-on-write: the received envelope is never mutated.
    public void ReplaceBody(ReadOnlyMemory<byte> body);
    public void SetHeader(string key, string? value);
    public void RemoveHeader(string key);

    // Explicit short-circuit outcomes. Valid only while the inner ring has not completed.
    public void Skip(string reason);
    public void Reject(string reason, Exception? cause = null);
}
```

`ReceiveContext` is non-generic: there is no typed payload before deserialization, so a `ReceiveContext<TMessage>` would carry nothing a typed registration does not already express. This is the one deliberate departure from the `IConsumeMiddleware<TContext>` shape.

Registration on `MessagingBuilder`, returning the existing `MiddlewareRegistration` handle (`WithPriority` applies unchanged):

```csharp
public MiddlewareRegistration AddReceiveMiddleware<T>()
    where T : class, IReceiveMiddleware;                       // global: every consumer, both lanes

public MiddlewareRegistration AddReceiveMiddlewareFor<TMiddleware, TMessage>(string groupName, MessageLane lane)
    where TMiddleware : class, IReceiveMiddleware
    where TMessage : class;                                     // typed: one payload type, group, lane
```

New exception, `Headless.Messaging.Exceptions`:

```csharp
public sealed class MessageDeserializationException(string message, Exception? innerException = null)
    : Exception(message, innerException);
```

Thrown by Stage A (wrapping the serializer's exception, and for a null payload) and by Stage B. It is the type U5 middleware catches.

### Matching and ordering

- Matching key is the same triple the consume pipeline uses: consumer `MessageValueType`, prefixed `GroupName` (ordinal), `Lane`. The lane is registration-derived from the consumer client that delivered the envelope, never from the `headless-intent` header; the header stays a consume-side compatibility check.
- Order: global registrations first, then typed; within each, `Priority` ascending, then registration order. Identical to consume and publish.
- Resolution is **registry-only**. There is no DI-scan fallback for `IReceiveMiddleware`: the builder is the only registration path, so nothing can be registered outside it. This avoids the lane-sensitive fallback the consume and publish pipelines carry today (see Findings).
- Global receive middleware is lane-agnostic. Envelope concerns (size, signature, upcasting) do not differ by lane; a middleware that needs to branch reads `context.Lane`.

### DI lifetime and scope

- Middleware services register as `Scoped` via `TryAddEnumerable`, like consume and publish middleware.
- The receive ring resolves middleware from a **per-delivery scope** that is created only when at least one descriptor matches (registry lookup cached per `(MessageType, GroupName, Lane)`) and disposed when the ring returns, **before** inbox admission, storage writes, ack, and dispatch. The zero-middleware path allocates nothing new.
- Receive-scope services do not flow into the consume scope. Anything that must cross the boundary travels as a header (`SetHeader`), which is then persisted with the message.
- No tenant scope is opened at receive time; tenancy is resolved at consume. Middleware that needs the raw tenant reads `Headers[Headers.TenantId]`.

### Cancellation

- `ReceiveContext.CancellationToken` starts as the consumer group's host-shutdown token (the token the register already passes to `DeserializeAsync`).
- `SetCancellationToken` is permitted until the inner ring completes; the pipeline re-reads the context token at each boundary and rechecks `IsCancellationRequested` after each middleware returns, mirroring the consume pipeline.
- An `OperationCanceledException` whose token matches `context.CancellationToken` is the **Cancelled** outcome, never a rejection. Any other OCE (for example an `HttpClient` timeout) is a middleware failure and is **Rejected**, matching the consume rule that a foreign cancellation is a failure.

### Transformation rules

- `Headers` and `Body` always show the envelope the **next** component will see. The first write copies the received dictionary; the received envelope (headers and bytes) is retained for the poison store.
- `SetHeader`/`RemoveHeader` reject the identity keys `Headers.MessageId`, `Headers.MessageName`, `Headers.Group`, and the framework-owned `Headers.Exception`, with `InvalidOperationException`. Every other header, including `ContractVersion`, `TenantId`, and lineage headers, may be rewritten; the consequences (inbox admission key, tenant resolution, lineage) are the author's responsibility and are documented.
- Transformation applies **once per delivery**. The accepted, transformed `Message` is what inbox admission persists as `Content`, so dispatch-side retries and Stage B see the transformed payload and never re-run receive middleware.
- `ReplaceBody`, `SetHeader`, `RemoveHeader`, `Skip`, `Reject`, and `SetCancellationToken` throw `InvalidOperationException` after the inner ring has completed, mirroring `PublishContext` post-completion rules.
- `next` is single-shot: a second call throws `InvalidOperationException`. Repair happens **before** `next` (inspect, transform, then continue), not by catching and re-running.

### Outcomes and ownership

`ConsumerRegister` remains the sole owner of transport settlement, storage writes, circuit-breaker reporting, and `OnExhausted`. Middleware never acks or rejects the transport and never writes storage. Every ring termination maps to exactly one row:

| Ring result | Outcome | Transport | Storage | `OnExhausted` | Circuit breaker | Span |
| --- | --- | --- | --- | --- | --- | --- |
| Inner ring completed; no `Skip`/`Reject` | **Accept** | commit after admission (as today) | inbox admission (as today) | no | probe transferred to dispatch (as today) | ok |
| Middleware returned without `next`, called `Skip(reason)` | **Skip** | commit | none | no | probe released; neither success nor failure | ok, `messaging.receive.outcome=skipped` |
| Middleware called `Reject(reason, cause)`, **or** threw a non-cancellation exception, **or** `next` threw `MessageDeserializationException`/version mismatch and the middleware rethrew or did not catch | **Reject** (poison-on-arrival) | commit | received-exception row with the **received** (untransformed) envelope as `data:` URI; `Headers.Exception` = cause type | yes, `Exception = cause`, `StorageId = Guid.Empty`, `RetryCount = 0` | failure reported (as today) | error |
| Middleware returned without `next` and declared **no** outcome (including swallowing an inner-ring exception) | **Reject** with `ReceiveOutcomeUndeclaredException` naming the middleware type | commit | as Reject | yes | failure reported | error |
| OCE bound to `context.CancellationToken` | **Cancelled** | reject (requeue) unless already settled (as today) | none | no | probe released | disposed without error (as today) |
| Middleware threw **after** the inner ring completed | **Accept** (post-success failure) | as Accept | as Accept | no | as Accept | ok; `ConsumePostSuccessMiddlewareFailed`-style log |

Rationale for the undeclared case being Reject rather than Skip: a silently dropped message is the worst failure mode this stage can produce; a loud terminal row with a named middleware is recoverable from the dashboard.

Rationale for no `Requeue`/`Defer` outcome: transport requeue semantics diverge (RabbitMQ requeues one delivery, Kafka cannot requeue a single offset, SQS relies on visibility timeout), and a retry loop at the receive boundary bypasses the dispatch-side retry budget and circuit breaker. A middleware with a transient dependency wraps it in its own resilience pipeline; if that fails, Reject is the deterministic answer and operators redeliver from storage.

### Stage B alignment

`SubscribeInvoker` throws `MessageDeserializationException` for conversion failure, the unsupported-value-type branch, and a null payload. `SubscribeExecutor` classifies it (including when wrapped in `SubscriberExecutionFailedException`) as **terminal at the first attempt**: `MessagingRetryDecision.Exhausted`, so the row lands `Failed` and `OnExhausted` fires, with no inline or persisted retries and regardless of `RetryStrategy.ShouldHandle`. The framework-side outcome for "this payload cannot become this contract" is then the same at both stages: terminal, acked, `OnExhausted`.

Stage A additionally rejects a **null payload** (empty body) for every consumer, since `ConsumeContext<TMessage>.Message` is contractually non-null; today this defect surfaces only at Stage B as a retryable failure.

### Response contract (inbound metadata read-only)

`MessageHeader` becomes a pure snapshot: remove `AddResponseHeader`, `RemoveCallback`, `RewriteCallback`, and the internal `ResponseHeader`. All response decisions live on `ConsumeContext`, all throwing `InvalidOperationException` after completion:

| Member | Status | Replaces |
| --- | --- | --- |
| `SetResponse<TResponse>(TResponse value)` | existing | — |
| `SetResponseCallbackName(string callbackName)` | existing | — |
| `SetResponseHeader(string key, string? value)` | new | `MessageHeader.AddResponseHeader` |
| `SetResponseDestination(string messageName)` | new | `MessageHeader.RewriteCallback` |
| `SuppressResponse()` | new | `MessageHeader.RemoveCallback` |

`ConsumeMiddlewarePipeline` builds `ConsumerExecutedResult` from context state only: `CallbackName = Suppressed ? null : Destination ?? Headers[CallbackName]`; `CallbackHeader` from the context's response headers. Reserved-key rejection for response headers is unchanged (the publish request factory already rejects them).

Consume middleware can inspect and set the response contract exactly as handlers do; `ConsumeContext.Headers` is read-only for both.

## Requirements

- R1. A receive middleware registered globally MUST run for every delivery on both lanes whose consumer was resolved; a typed registration MUST run only for deliveries whose consumer payload type, prefixed group, and lane match.
- R2. Receive middleware MUST run after subscriber lookup and before contract-version validation and Stage A deserialization; `next` MUST cover version validation, deserialization, and the null-payload check.
- R3. The receive ring MUST resolve middleware only through the descriptor registry, from a per-delivery scope created lazily and disposed before admission.
- R4. `ReceiveContext.Headers`/`Body` MUST be read-only views; all changes MUST go through `ReplaceBody`/`SetHeader`/`RemoveHeader`; identity headers and `Headers.Exception` MUST be rejected; the received envelope MUST be retained unmodified for the poison store.
- R5. Every ring termination MUST map to exactly one outcome in the table above; `ConsumerRegister` MUST remain the only component that settles the transport, writes storage, reports to the circuit breaker, or invokes `OnExhausted`.
- R6. `Skip`, `Reject`, transformation, and token replacement MUST throw after the inner ring completes; `next` MUST be single-shot.
- R7. Cancellation bound to `context.CancellationToken` MUST produce the Cancelled outcome, never Reject; a foreign OCE MUST produce Reject.
- R8. `MessageDeserializationException` MUST be terminal at both stages (poison-on-arrival at Stage A; `Exhausted` at attempt 1 at Stage B) and MUST invoke `OnExhausted` once.
- R9. `MessageHeader` MUST expose no mutation members; response metadata MUST be settable only through `ConsumeContext`.
- R10. Existing publish and consume middleware contracts, ordering, and guarantees MUST be unchanged.
- R11. Each outcome MUST be observable: a log event per outcome carrying message id, name, group, lane, and (for Skip/Reject) the middleware type and reason; a `messaging.receive.outcome` span tag; a counter with an `outcome` tag. Reasons are operator text and MUST pass through `LogSanitizer`.

## Deferred and Non-Goals

- **Outbound envelope stage.** Not added. Symmetric codecs (U7) decorate `ISerializer` so both directions live in one type. Revisit when a use case needs per-lane or per-type outbound byte access **with scoped services or ordering** that a singleton decorator cannot express (for example, signing with a per-tenant key resolved from a scoped provider). That trigger, not symmetry, justifies the stage.
- **Requeue/defer outcome.** Not added (rationale above).
- **Foreign envelope adoption** (CloudEvents and similar producers that do not set `MessageId`/`MessageName`). The transport client rejects those before Core; adoption is a transport-provider concern.
- **Reviving `IConsumeFilter`.** Not done; the ring-with-`next` shape is kept for all three stages.
- **Runtime subscriptions** (`IRuntimeSubscriber`) receive the same treatment as durable consumers at this stage; `ConsumerContractVersion` is null for them.

## Findings Outside This Issue

- **Global middleware lane filter.** `AddBusConsumeMiddleware`/`AddBusPublishMiddleware` register `Lane = Bus` (`MessagingBuilder.cs:126,149`). The registry filters by lane, so on the Queue lane a "global" middleware runs only through the untracked DI-scan fallback, and only when no Queue-lane typed descriptor exists (`ConsumeMiddlewarePipeline.cs:259-279`, `PublishMiddlewarePipeline.cs:250-268`). `TenantPropagationConsumeMiddleware` is registered this way, while `SubscribeExecutor.cs:238-246` derives `propagateTenant` for the transactional tier from the lane-filtered registry. Inferred from code, unconfirmed by a test. Recommendation: separate issue; make `MiddlewareScope.Bus` lane-agnostic (the docs already say "every publish or consume") and drop the DI-scan fallback. This design does not depend on that fix but is written so the receive stage never inherits the defect.

## Implementation Notes

- Registry: add `MiddlewareDirection.Receive` and `TryGetReceiveDescriptors(Type messageType, string groupName, MessageLane lane)`; global receive descriptors ignore the lane filter.
- Startup validation: extend `_ValidateMiddlewareDescriptors` so a receive descriptor's `MiddlewareType` implements `IReceiveMiddleware` (typed path is compile-time constrained; global path validates at registration like consume).
- `ConsumerRegister.onMessageCallback`: extract the bracketed inner steps into a local `next`; build `ReceiveContext` from `transportMessage` and `executor`; map ring termination to the outcome table; keep the received `TransportMessage` for the poison `data:` URI.
- `Headless.Messaging.Testing`'s `RecordingTransport` calls `ISerializer.DeserializeAsync` directly (`RecordingTransport.cs:128`); confirm whether recorded deliveries flow through `ConsumerRegister` so the harness observes receive middleware, or document that they do not.
- Documentation sync (public API and consumer-visible behavior change): `docs/llms/messaging.md` Middleware section (three stages, outcome table, response contract), `src/Headless.Messaging.Core/README.md`, `src/Headless.Messaging.Abstractions/README.md` (`MessageHeader` is read-only), and `CONCEPTS.md` if "poison-on-arrival" is not yet a named concept.

## Acceptance Criteria (test plan)

Unit, `Headless.Messaging.Core.Tests.Unit`:

- Registration/matching: global runs on Bus and Queue; typed runs only on matching `(type, group, lane)`; prefixed group matches; ordering global→typed, priority, registration order; a non-`IReceiveMiddleware` type is rejected at registration.
- Scope: middleware resolved from a fresh scope per delivery; scope disposed before admission; zero-middleware path creates no scope.
- Transformation: `Headers`/`Body` reflect prior middleware writes; identity-header writes throw; received envelope stored on Reject; transformed `Message` persisted on Accept; post-completion writes throw; second `next` throws.
- Outcomes, one test per row of the table: Accept, Skip (ack, no storage, no `OnExhausted`, probe released), Reject via `Reject()`, Reject via exception, Reject via rethrown `MessageDeserializationException`, Reject via undeclared outcome, Cancelled via bound OCE (transport reject, no storage), foreign OCE → Reject, post-success throw → Accept with log.
- Stage A null payload → Reject; version mismatch surfaces through `next` and can be caught (U5).
- Stage B: `MessageDeserializationException` → `Failed` terminal at attempt 1, `OnExhausted` once, no retries, `ShouldHandle` not consulted.
- Response contract: `SetResponseHeader`, `SetResponseDestination`, `SuppressResponse` drive `ConsumerExecutedResult`; all throw after completion; `MessageHeader` has no mutation members (update `MessageHeaderTests`, `SubscribeInvokerTests`).

Harness, `Headless.Messaging.Core.Tests.Harness`: one conformance scenario per outcome (Accept, Skip, Reject, Cancelled) through the in-memory transport, asserting transport settlement and storage state, so provider integration suites inherit it.

Documentation matches the resulting API; `make quality-analyzers-project` clean for `Headless.Messaging.Core`.

## Open Decisions

- D1. Stage name `Receive` (matches `StoreReceivedMessageAsync`, `LeaseReceiveAsync`, "received message" throughout Core). Alternatives considered: `Envelope`, `Inbound`, `Transport`.
- D2. Include the `Skip` outcome (U2). It is the only outcome that drops a message without a storage row; the case for it is avoiding inbox writes on high-volume shared topics. Dropping it leaves Accept/Reject/Cancelled.
- D3. Stage B classification as `Exhausted` at attempt 1 (fires `OnExhausted`) rather than `Stop` (terminal without the callback). `Exhausted` keeps one dead-letter sink for every deterministic payload defect.
- D4. Remove `MessageHeader` mutators outright (greenfield, test-only callers) rather than obsoleting them.
