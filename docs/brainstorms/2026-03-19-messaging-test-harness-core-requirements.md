---
date: 2026-03-19
topic: messaging-test-harness-core
---

# Messaging Test Harness Core

## Problem Frame

Application developers consuming Headless.Messaging have no first-class way to test their consumers, publishers, and message flows without running real broker infrastructure (Docker + Testcontainers). The existing `InMemoryQueue` is a transport implementation, not a test harness — it lacks message capture, assertion primitives, fault observation, and test isolation. Developers resort to `Thread.Sleep`, manual `TaskCompletionSource` wiring, and ad-hoc `TestSubscriber` clones.

**Primary audience:** Downstream application developers testing their own `IConsume<T>` handlers, pub-sub flows, and filter pipelines.

## Requirements

### Harness Lifecycle

- R1. Ship as a single NuGet package: `Headless.Messaging.Testing`
- R2. Harness setup mirrors how apps configure messaging in `Program.cs`:
  ```
  var harness = await MessagingTestHarness.CreateAsync(services => {
      services.AddMessaging(o => { ... });
      services.AddConsumer<OrderCreatedConsumer, OrderCreatedEvent>("orders.created");
  });
  ```
- R3. Each `MessagingTestHarness` instance creates its own isolated in-memory transport and consumer registry. No singleton sharing. Parallel tests with separate harness instances are automatically isolated.
- R4. Harness implements `IAsyncDisposable`. Disposal stops background tasks, drains channels, and releases resources.
- R5. Harness runs the **full Dispatcher pipeline** — channels, background tasks, serialization/deserialization, filter pipeline. Messages flow through the same code path as production.

### Observable Collections

- R6. Harness exposes three thread-safe, queryable collections:
  - `Published` — every message sent via `IMessagePublisher`
  - `Consumed` — every message successfully handled by an `IConsume<T>`
  - `Faulted` — every message where the consumer threw an unhandled exception
- R7. Each collection entry includes the full `ConsumeContext` metadata: `MessageId`, `CorrelationId`, `Headers`, `Topic`, `Timestamp`, plus the deserialized message payload.
- R8. `Faulted` entries additionally include the `Exception` that caused the fault.

### Awaitable Assertions

- R9. Harness provides async wait primitives that complete when a matching message appears:
  - `WaitForConsumed<T>(TimeSpan timeout)` — waits for any message of type `T`
  - `WaitForConsumed<T>(Func<T, bool> predicate, TimeSpan timeout)` — waits for a matching message
  - `WaitForPublished<T>(...)` — same pattern for published messages
  - `WaitForFaulted<T>(...)` — same pattern for faulted messages
- R10. On timeout, the wait method throws a descriptive exception showing:
  - What was expected (type, predicate description if available)
  - What was actually published/consumed/faulted during the wait window
  - Elapsed time
- R11. Wait methods use `TaskCompletionSource` internally — no polling, no `Thread.Sleep`. Signal-based completion.
- R12. Users apply property assertions using AwesomeAssertions (the project's existing assertion library) on the returned `ConsumeContext<T>`. The harness does NOT ship its own fluent assertion DSL.

### Test Consumer

- R13. Provide a `TestConsumer<TMessage>` that implements `IConsume<TMessage>`, captures every `ConsumeContext<TMessage>` into its observable list, and can be registered in the harness.
- R14. `TestConsumer<T>` exposes:
  - `ReceivedContexts` — all captured `ConsumeContext<T>` instances
  - `ReceivedMessages` — projected payloads for convenience
  - `Clear()` — reset captured state (thread-safe)
- R15. Harness provides `GetTestConsumer<T>()` to retrieve the registered test consumer for assertion.

### DI Integration

- R16. The harness builds a real `IServiceProvider` from the user's configuration lambda. Consumers resolve via scoped DI, same as production.
- R17. Users can register mocks/stubs in the configuration lambda for dependencies their consumers need (e.g., `IOrderRepository`).

## Success Criteria

- A developer can write a complete publish → consume → assert test in <10 lines of test code (excluding setup)
- Tests run in <100ms (no real I/O, no Docker)
- Parallel xUnit tests with separate harness instances never interfere
- Timeout failures produce actionable diagnostics, not opaque `TaskCanceledException`

## Scope Boundaries

**In scope:**
- In-memory transport harness for unit/integration tests
- Message capture (published, consumed, faulted)
- Awaitable assertion primitives
- Test consumer fixture
- Full pipeline execution (Dispatcher, filters, serialization)
- Per-harness isolation

**Out of scope (deferred to later ideation ideas):**
- Fault injection / chaos testing (ideation idea #3)
- Deterministic test clock / time control (ideation idea #4)
- Filter pipeline probe / white-box filter testing (ideation idea #5)
- Saga state snapshot assertions (ideation idea #6)
- Message contract / schema verification (ideation idea #7)
- Real broker overlay / spy mode
- Backpressure or load testing

## Key Decisions

- **Single package** over abstraction+provider split: test harness has one implementation (in-memory). No need for provider abstraction.
- **Full pipeline** over synchronous shortcut: production fidelity matters more than determinism. Async nature handled by WaitFor* methods.
- **Thin assertion API** over fluent DSL: return data, let AwesomeAssertions handle property checks. Fewer APIs to maintain, consistent with project patterns.
- **Per-instance isolation** over singleton + fork: simpler, no ConsumerRegistry mutation needed. Each harness is self-contained.

## Dependencies / Assumptions

- Assumes `InMemoryQueue` can be instantiated non-singleton (may need factory refactor)
- Assumes `ConsumerRegistry` can be created per-harness without the frozen-singleton constraint
- Assumes `Dispatcher` can be scoped to a harness instance
- The package will depend on `Headless.Messaging.Core` and `Headless.Messaging.InMemoryQueue`

## Outstanding Questions

### Resolve Before Planning
(none — all product decisions resolved)

### Deferred to Planning
- [Affects R3][Needs research] Can `ConsumerRegistry`, `Dispatcher`, and `InMemoryQueue` be instantiated per-harness today, or do they need refactoring to remove singleton assumptions?
- [Affects R5][Technical] Should the harness configure Dispatcher with parallelism disabled (single-threaded) by default for more predictable test behavior, while still using the real pipeline?
- [Affects R6][Technical] What is the concrete type for observable collections — `ConcurrentBag<T>`, custom `ObservableMessageCollection<T>`, or `Channel<T>` that drains into a list?
- [Affects R9][Technical] Should `WaitFor*` methods accept `CancellationToken` in addition to `TimeSpan timeout`?
- [Affects R13][Technical] Should `TestConsumer<T>` support configurable behavior (e.g., throw on Nth message, add delay) or stay simple and defer programmable behavior to the fault injection package?

## Next Steps

`/dev:plan` for structured implementation planning
