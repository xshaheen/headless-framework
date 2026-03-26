---
title: "feat: Add messaging test harness for application developers"
type: feat
status: active
date: 2026-03-19
origin: docs/brainstorms/2026-03-19-messaging-test-harness-core-requirements.md
---

> **Verification gate:** Before claiming any task or story complete — run the plan's `verification_command` and confirm PASS. Do not mark complete based on reading code alone.

# feat: Add messaging test harness for application developers

## Overview

Add `Headless.Messaging.Testing` — a single NuGet package that lets downstream application developers test their `IConsume<T>` handlers, pub-sub flows, and filter pipelines against a fully in-memory messaging pipeline without Docker or real brokers. The harness runs the full Dispatcher pipeline (channels, background tasks, serialization, filters) so tests exercise the same code paths as production.

## Problem Frame

Application developers consuming Headless.Messaging have no first-class way to test their consumers and message flows without broker infrastructure. The existing `InMemoryQueue` is a transport implementation, not a test harness — it lacks message capture, assertion primitives, fault observation, and test isolation. Developers resort to `Thread.Sleep`, manual `TaskCompletionSource` wiring, and ad-hoc `TestSubscriber` clones. (see origin: `docs/brainstorms/2026-03-19-messaging-test-harness-core-requirements.md`)

## Requirements Trace

From origin document:

- R1. Single NuGet package: `Headless.Messaging.Testing`
- R2. Setup mirrors `Program.cs` via `MessagingTestHarness.CreateAsync(services => { ... })`
- R3. Per-instance isolated transport and consumer registry — no singleton sharing
- R4. `IAsyncDisposable` with clean shutdown
- R5. Full Dispatcher pipeline — channels, background tasks, serialization, filters
- R6. Observable collections: `Published`, `Consumed`, `Faulted`
- R7. Collection entries include full `ConsumeContext` metadata + deserialized payload
- R8. `Faulted` entries include the `Exception`
- R9. Async `WaitFor*` primitives with timeout + predicate
- R10. Descriptive timeout exception showing expected vs actual
- R11. Signal-based completion via `TaskCompletionSource`
- R12. Users assert with AwesomeAssertions — no built-in fluent DSL
- R13. `TestConsumer<TMessage>` capturing `ConsumeContext`
- R14. `TestConsumer<T>` exposes `ReceivedContexts`, `ReceivedMessages`, `Clear()`
- R15. `GetTestConsumer<T>()` retrieval
- R16. Real `IServiceProvider` from user config lambda — scoped DI like production
- R17. Users register mocks/stubs in config lambda

## Scope Boundaries

**In scope:** In-memory transport harness, message capture (published/consumed/faulted), awaitable assertion primitives, test consumer fixture, full pipeline execution, per-harness isolation, package README + XML docs.

**Out of scope:** Fault injection/chaos testing, deterministic test clock, filter pipeline probe, saga state snapshots, message contract verification, real broker overlay, backpressure testing. (see origin: scope boundaries)

## Context & Research

### Relevant Code and Patterns

**DI lifetime analysis — all singletons, no static state:**
- `ConsumerRegistry` — instance fields (`_consumers`, `_frozen`), freeze-on-first-read pattern. Safe per-test via fresh `ServiceCollection`.
- `Dispatcher` — constructor DI, no static state. Requires `ILogger<Dispatcher>`, `IMessageSender`, `IOptions<MessagingOptions>`, `ISubscribeExecutor`, `IDataStorage`, `TimeProvider`.
- `MemoryQueue` — instance dictionaries for subscriptions. `internal sealed`.
- All services registered as singletons in `Setup.cs` via `TryAddSingleton`.

**Key setup entry point:** `services.AddHeadlessMessaging(options => { ... })` in `src/Headless.Messaging.Core/Setup.cs:65` creates a fresh `ConsumerRegistry`, configures options, registers all core services. Consumer registration happens during this call via `MessagingOptions.Subscribe<T>()` and `MessagingOptions.AddConsumer<T, TMsg>()`.

**Existing in-memory support:**
- `UseInMemoryMessageQueue()` — registers `MemoryQueue`, `InMemoryConsumerClientFactory`, `InMemoryQueueTransport` (`src/Headless.Messaging.InMemoryQueue/Setup.cs`)
- `UseInMemoryStorage()` — registers `InMemoryDataStorage`, `InMemoryStorageInitializer`, `InMemoryOutboxTransaction` (`src/Headless.Messaging.InMemoryStorage/Setup.cs`)
- Together they provide a fully in-memory pipeline with no external dependencies.

**Bootstrap lifecycle:** `Bootstrapper` is registered as singleton + `HostedService`. Calls `IStorageInitializer.InitializeAsync()`, then starts all `IProcessingServer` instances (Dispatcher, ConsumerRegister, MessageProcessingServer). For testing, we need to bootstrap manually instead of relying on `HostedService`.

**Existing package pattern to follow:** `Headless.Testing` (`src/Headless.Testing/`) — ships as NuGet from `src/`, `IsTestProject=false`, `IsTestableProject=false`, references xunit + AwesomeAssertions.

**Filter system:** Single `IConsumeFilter` per scope, resolved via `GetService<IConsumeFilter>()` (nullable). `ConsumeExecutionPipeline` creates a DI scope per message, runs filter hooks around handler invocation. Filter has access to deserialized message via `ExecutingContext.DeliverMessage`.

**Publisher hierarchy:** `IOutboxPublisher` persists to storage then dispatches; `IDirectPublisher` sends directly to transport. Both flow through `IMessageSender` → `ITransport`. The harness will use `IOutboxPublisher` (default) with `InMemoryDataStorage` for production-faithful flow.

### Institutional Learnings

No prior documented solutions exist for messaging test harness patterns. This is greenfield.

## Key Technical Decisions

- **Fresh `ServiceCollection` per harness** (not singleton fork): Research confirmed all messaging services use instance state, no statics. Each `MessagingTestHarness.CreateAsync()` builds a completely independent DI container with its own `ConsumerRegistry`, `Dispatcher`, `MemoryQueue`. This gives isolation for free without requiring registry mutation or forking. (see origin: "Per-instance isolation" decision)

- **Single-threaded Dispatcher by default**: Configure `EnablePublishParallelSend = false`, `EnableSubscriberParallelExecute = false`, `ConsumerThreadCount = 1`. This preserves the real pipeline (channels, background tasks) while making test behavior more predictable. Users can override via the options lambda. Resolves deferred question on R5.

- **`RecordedMessage` record type + `MessageObservationStore`**: Use a custom `MessageObservationStore` backed by `ConcurrentQueue<RecordedMessage>` with a list of `TaskCompletionSource` waiters for `WaitFor*` methods. When a message is added, complete all matching waiters. Simple, thread-safe, allocation-friendly. Resolves deferred question on R6.

- **`WaitFor*` accepts both `TimeSpan` and optional `CancellationToken`**: Standard .NET async pattern. Default `CancellationToken.None`. Resolves deferred question on R9.

- **`TestConsumer<T>` stays simple**: No configurable failure behavior. Captures messages, exposes data, done. Programmable behavior deferred to fault injection package. Resolves deferred question on R13.

- **Interception via decorators, not filters**:
  - **Published**: Decorate `ITransport` with `RecordingTransport` — wraps `SendAsync` to record the `TransportMessage` (headers contain MessageId, CorrelationId, Topic; body is serialized bytes) then forward to real `InMemoryQueueTransport`.
  - **Consumed/Faulted**: Decorate `IConsumeExecutionPipeline` with `RecordingConsumeExecutionPipeline` — wraps `ExecuteAsync`, records success to `Consumed` or exception to `Faulted`. Uses `ConsumerExecutorDescriptor.MessageType` for typed recording, deserializes body via `ISerializer` for the payload.
  - This avoids consuming the single `IConsumeFilter` slot, leaving it free for user-registered filters.

- **Package lives in `src/`**: Following `Headless.Testing` pattern — `src/Headless.Messaging.Testing/`, `IsTestProject=false`, shipped as NuGet.

## Open Questions

### Resolved During Planning

- **Can services be instantiated per-harness?** → Yes. All use instance fields, no static state. Fresh `ServiceCollection` = full isolation.
- **Observable collection type?** → Custom `MessageObservationStore` with `ConcurrentQueue` + TCS waiters.
- **CancellationToken on WaitFor*?** → Yes, both TimeSpan + CancellationToken.
- **TestConsumer configurable?** → No, keep simple. Defer to fault injection.
- **Single-threaded Dispatcher?** → Yes, default disabled parallelism with real pipeline.

### Deferred to Implementation

- Exact interception point in `IConsumeExecutionPipeline` — may need to check `ExecuteAsync` signature and what data is available at that level
- Whether `Bootstrapper.StartAsync()` can be called directly or if the harness needs to replicate its logic to avoid `HostedService` registration
- How to deserialize `TransportMessage.Body` back to typed objects for `RecordedMessage.Message` — need to verify `ISerializer` API

## Implementation Units

- [x] **Unit 1: Project scaffolding**

**Goal:** Create the `Headless.Messaging.Testing` project with correct structure, dependencies, and package metadata.

**Requirements:** R1

**Dependencies:** None

**Files:**
- Create: `src/Headless.Messaging.Testing/Headless.Messaging.Testing.csproj`
- Modify: `Directory.Packages.props` (add any new package references if needed)
- Modify: `Headless.sln` (add project to solution)

**Approach:**
- Follow `Headless.Testing` pattern: `IsTestProject=false`, `IsTestableProject=false`, `net10.0`
- Project references: `Headless.Messaging.Core`, `Headless.Messaging.InMemoryQueue`, `Headless.Messaging.InMemoryStorage`
- Package references: `xunit.v3.extensibility.core` (for `IAsyncLifetime` if needed), `Microsoft.Extensions.DependencyInjection`
- Add `InternalsVisibleTo` for the test project

**Patterns to follow:**
- `src/Headless.Testing/Headless.Testing.csproj` — package structure
- `src/Headless.Messaging.InMemoryQueue/Headless.Messaging.InMemoryQueue.csproj` — messaging package dependencies

**Test scenarios:**
- Project builds successfully
- Package metadata is correct

**Verification:**
- `dotnet build src/Headless.Messaging.Testing/` succeeds

---

- [x] **Unit 2: RecordedMessage types and MessageObservationStore**

**Goal:** Define the core data types and thread-safe observation store that underpins all capture and assertion functionality.

**Requirements:** R6, R7, R8, R11

**Dependencies:** Unit 1

**Files:**
- Create: `src/Headless.Messaging.Testing/RecordedMessage.cs`
- Create: `src/Headless.Messaging.Testing/MessageObservationStore.cs`
- Test: `tests/Headless.Messaging.Testing.Tests.Unit/MessageObservationStoreTests.cs`

**Approach:**
- `RecordedMessage` — sealed record with `Type MessageType`, `object Message`, `string MessageId`, `string? CorrelationId`, `IReadOnlyDictionary<string, string?> Headers`, `string Topic`, `DateTimeOffset Timestamp`, `Exception? Exception` (null for non-faulted)
- `MessageObservationStore` — three `ConcurrentQueue<RecordedMessage>` (Published, Consumed, Faulted) + waiter lists using `List<(Type, Func<object, bool>?, TaskCompletionSource<RecordedMessage>)>`
- `Record(RecordedMessage, MessageObservationType)` — enqueues and completes matching waiters
- `WaitForAsync<T>(MessageObservationType, Func<T, bool>?, TimeSpan, CancellationToken)` — registers waiter, checks existing queue first (race-safe), returns `Task<RecordedMessage>`
- Thread safety via `lock` on waiter list (low contention in tests)
- On timeout: throw `MessageObservationTimeoutException` with diagnostic payload (what was expected, what exists in the relevant collection, elapsed time) — satisfies R10

**Patterns to follow:**
- `TestSubscriber.WaitForMessageAsync()` pattern in `tests/Headless.Messaging.Core.Tests.Harness/Helpers/TestSubscriber.cs`

**Test scenarios:**
- Record a message → appears in correct collection
- WaitFor completes immediately when matching message already exists
- WaitFor completes when matching message arrives after registration
- WaitFor with predicate — only matches when predicate returns true
- WaitFor times out → throws `MessageObservationTimeoutException` with expected vs actual info
- Multiple concurrent waiters for different types all complete independently
- Thread safety under concurrent Record + WaitFor

**Verification:**
- All unit tests pass
- Timeout exception includes actionable diagnostic info

---

- [x] **Unit 3: Recording infrastructure (transport + pipeline decorators)**

**Goal:** Create decorators that intercept published and consumed/faulted messages and record them into the observation store.

**Requirements:** R6, R7, R8

**Dependencies:** Unit 2

**Files:**
- Create: `src/Headless.Messaging.Testing/Internal/RecordingTransport.cs`
- Create: `src/Headless.Messaging.Testing/Internal/RecordingConsumeExecutionPipeline.cs`
- Test: `tests/Headless.Messaging.Testing.Tests.Unit/RecordingTransportTests.cs`
- Test: `tests/Headless.Messaging.Testing.Tests.Unit/RecordingConsumeExecutionPipelineTests.cs`

**Approach:**
- `RecordingTransport : ITransport` — wraps inner `ITransport`, on `SendAsync`: extract headers (MessageId, CorrelationId, Topic, Timestamp) from `TransportMessage`, deserialize body via `ISerializer` to populate `RecordedMessage.Message`, record to store as Published, then forward to inner transport
- `RecordingConsumeExecutionPipeline : IConsumeExecutionPipeline` — wraps inner pipeline, on `ExecuteAsync`: call inner, on success record to Consumed, on exception record to Faulted with the exception. Use `ConsumerExecutorDescriptor` for message type info, deserialize from `MediumMessage`
- Both decorators hold a reference to the shared `MessageObservationStore`
- Both are `internal sealed` — only exposed through `MessagingTestHarness`

**Patterns to follow:**
- Decorator pattern used in `src/Headless.Messaging.Core/Processor/` processors

**Test scenarios:**
- RecordingTransport records message then forwards to inner
- RecordingTransport preserves all header metadata in RecordedMessage
- RecordingConsumeExecutionPipeline records Consumed on success
- RecordingConsumeExecutionPipeline records Faulted with exception on failure
- RecordingConsumeExecutionPipeline re-throws exception after recording (doesn't swallow)
- Both decorators correctly populate MessageType for typed queries

**Verification:**
- Unit tests pass with mocked inner transport and pipeline

---

- [x] **Unit 4: MessagingTestHarness — core harness class**

**Goal:** Create the main harness class with `CreateAsync`, DI composition, bootstrap, lifecycle management, and public API surface.

**Requirements:** R2, R3, R4, R5, R6, R9, R10, R15, R16, R17

**Dependencies:** Units 2, 3

**Files:**
- Create: `src/Headless.Messaging.Testing/MessagingTestHarness.cs`
- Test: `tests/Headless.Messaging.Testing.Tests.Unit/MessagingTestHarnessTests.cs`

**Approach:**
- `public sealed class MessagingTestHarness : IAsyncDisposable`
- `static async Task<MessagingTestHarness> CreateAsync(Action<IServiceCollection> configure, CancellationToken ct = default)`:
  1. Build `ServiceCollection`
  2. Call user's `configure` lambda (they call `services.AddHeadlessMessaging(o => { ... })`)
  3. Force in-memory transport + storage: `UseInMemoryMessageQueue()`, `UseInMemoryStorage()`
  4. Set single-threaded defaults: `EnablePublishParallelSend = false`, `EnableSubscriberParallelExecute = false`
  5. Replace `ITransport` with `RecordingTransport` (decorator)
  6. Replace `IConsumeExecutionPipeline` with `RecordingConsumeExecutionPipeline` (decorator)
  7. Build `ServiceProvider`
  8. Call `IBootstrapper.BootstrapAsync()` to start background processing
  9. Return harness instance
- **Public API:**
  - `Published` → `IReadOnlyCollection<RecordedMessage>` snapshot from store
  - `Consumed` → `IReadOnlyCollection<RecordedMessage>` snapshot from store
  - `Faulted` → `IReadOnlyCollection<RecordedMessage>` snapshot from store
  - `WaitForPublished<T>(TimeSpan timeout, CancellationToken ct = default)` → delegates to store
  - `WaitForPublished<T>(Func<T, bool> predicate, TimeSpan timeout, CancellationToken ct = default)`
  - `WaitForConsumed<T>(...)` — same overload pattern
  - `WaitForFaulted<T>(...)` — same overload pattern
  - `GetTestConsumer<T>()` → resolves from DI
  - `ServiceProvider` → exposed for advanced scenarios (resolving user services)
  - `Publisher` → `IOutboxPublisher` from DI for publishing in tests
- `DisposeAsync()`:
  1. Stop `Bootstrapper` / all `IProcessingServer`
  2. Dispose `ServiceProvider`
  3. Clear observation store

**Patterns to follow:**
- `MessagingIntegrationTestsBase` in `tests/Headless.Messaging.Core.Tests.Harness/` for bootstrap lifecycle
- MassTransit `InMemoryTestHarness` for API shape inspiration

**Test scenarios:**
- CreateAsync builds isolated DI container with in-memory transport
- CreateAsync bootstraps successfully
- Two concurrent harness instances don't share state
- Published/Consumed/Faulted collections are initially empty
- DisposeAsync cleans up without exceptions
- ServiceProvider resolves user-registered services
- User-configured consumers are registered and discoverable

**Verification:**
- Integration test: publish a message → consumer receives it → appears in both Published and Consumed
- Two harness instances in parallel don't cross-contaminate

---

- [x] **Unit 5: TestConsumer\<T\>**

**Goal:** Provide a generic test consumer that captures `ConsumeContext` for assertion.

**Requirements:** R13, R14, R15

**Dependencies:** Unit 4

**Files:**
- Create: `src/Headless.Messaging.Testing/TestConsumer.cs`
- Test: `tests/Headless.Messaging.Testing.Tests.Unit/TestConsumerTests.cs`

**Approach:**
- `public sealed class TestConsumer<TMessage> : IConsume<TMessage>`
- Thread-safe via `Lock` (following existing `TestSubscriber` pattern)
- `Consume(ConsumeContext<TMessage>, CancellationToken)` → adds context to internal list
- Properties: `ReceivedContexts: IReadOnlyList<ConsumeContext<TMessage>>`, `ReceivedMessages: IReadOnlyList<TMessage>` (projected from contexts)
- `Clear()` — resets internal list (thread-safe)
- Registration: users register via `services.AddConsumer<TestConsumer<MyEvent>, MyEvent>("topic")` or a convenience extension `services.AddTestConsumer<MyEvent>("topic")`
- `GetTestConsumer<T>()` on harness resolves from DI — requires `TestConsumer<T>` to be registered as singleton (not the default scoped). The harness should register a singleton factory or the consumer as singleton.

**Patterns to follow:**
- `TestSubscriber` in `tests/Headless.Messaging.Core.Tests.Harness/Helpers/TestSubscriber.cs`

**Test scenarios:**
- Consumer captures ConsumeContext with full metadata
- ReceivedMessages projects payloads correctly
- Clear() resets all captured data
- Thread-safe under concurrent consumption
- GetTestConsumer<T>() returns the registered instance

**Verification:**
- Publish message → TestConsumer captures it → assert on ReceivedContexts metadata

---

- [x] **Unit 6: Integration test suite**

**Goal:** End-to-end tests proving the harness works for real application testing scenarios.

**Requirements:** All (R1-R17), success criteria

**Dependencies:** Units 4, 5

**Files:**
- Create: `tests/Headless.Messaging.Testing.Tests.Unit/Headless.Messaging.Testing.Tests.Unit.csproj`
- Create: `tests/Headless.Messaging.Testing.Tests.Unit/EndToEnd/PublishConsumeTests.cs`
- Create: `tests/Headless.Messaging.Testing.Tests.Unit/EndToEnd/FaultObservationTests.cs`
- Create: `tests/Headless.Messaging.Testing.Tests.Unit/EndToEnd/IsolationTests.cs`
- Create: `tests/Headless.Messaging.Testing.Tests.Unit/EndToEnd/TimeoutDiagnosticsTests.cs`
- Modify: `Headless.sln` (add test project)

**Approach:**
- Tests exercise the harness as a downstream developer would
- Each test creates its own `MessagingTestHarness` instance
- Use real consumer implementations (not mocks) to validate full pipeline

**Test scenarios:**
- **Publish → consume → assert**: Publish typed message, WaitForConsumed, assert on ConsumeContext properties (MessageId, CorrelationId, Headers, Topic)
- **Multiple message types**: Register multiple consumers, publish different types, each WaitFor resolves correctly
- **Faulted observation**: Consumer throws, WaitForFaulted captures exception type and message
- **Predicate filtering**: WaitForConsumed with predicate on message properties
- **Timeout diagnostics**: No consumer for a type, WaitForConsumed times out with descriptive exception showing what was actually published
- **Isolation**: Two harness instances running in parallel, each publishes — messages never cross-contaminate
- **DI integration**: Consumer with injected dependency (mocked via test setup), verify dependency was called
- **Custom headers**: Publish with custom headers, assert headers appear in ConsumeContext
- **TestConsumer**: Register via convenience method, publish, assert via GetTestConsumer
- **Performance**: Single publish-consume cycle completes in <100ms

**Verification:**
- All end-to-end tests pass
- Parallel tests don't flake

---

- [x] **Unit 7: Package README and XML docs**

**Goal:** Documentation for the NuGet package.

**Requirements:** R1, R2 (API discoverability)

**Dependencies:** Units 4, 5

**Files:**
- Create: `src/Headless.Messaging.Testing/README.md`
- Modify: `src/Headless.Messaging.Testing/MessagingTestHarness.cs` (add XML docs to public API)
- Modify: `src/Headless.Messaging.Testing/TestConsumer.cs` (add XML docs)
- Modify: `src/Headless.Messaging.Testing/RecordedMessage.cs` (add XML docs)

**Approach:**
- README follows existing package README pattern (e.g., `src/Headless.Messaging.InMemoryQueue/README.md`)
- Show quick-start example: create harness, publish, assert
- Document public API surface
- XML docs on all public types and methods

**Patterns to follow:**
- `src/Headless.Messaging.InMemoryQueue/README.md`
- `src/Headless.Messaging.Nats/README.md`

**Test scenarios:**
- README example is compilable (verified by integration tests using same patterns)

**Verification:**
- `dotnet build` with documentation warnings enabled passes

## System-Wide Impact

- **Interaction graph:** The harness creates a complete messaging pipeline internally. No impact on production code paths. The recording decorators wrap `ITransport` and `IConsumeExecutionPipeline` — both are injected only within the harness's own `ServiceProvider`.
- **Error propagation:** `RecordingConsumeExecutionPipeline` must re-throw exceptions after recording to Faulted — it must not swallow errors or change retry behavior.
- **State lifecycle risks:** None. Each harness instance is fully self-contained. No shared static state. Disposal cleans up all resources.
- **API surface parity:** This is a new package. No existing interfaces need changes.
- **Integration coverage:** Unit 6 covers the key cross-layer scenarios (DI → publisher → transport → consumer → filter → assertion).

## Risks & Dependencies

- **`Bootstrapper` as HostedService**: The `Bootstrapper` is registered via `AddHostedService`. The harness needs to call `StartAsync` directly without the generic host. If `Bootstrapper.StartAsync` has host-specific dependencies, may need to replicate its logic or call `IProcessingServer.Start()` individually.
- **`InternalsVisibleTo` for InMemoryQueue**: `MemoryQueue` and `InMemoryConsumerClient` are `internal sealed`. The harness doesn't need to access them directly — it uses the public `UseInMemoryMessageQueue()` extension. But the recording transport decorator needs access to `ITransport` which is registered by the InMemoryQueue setup. This should work through DI without `InternalsVisibleTo`.
- **Deserialization for RecordedMessage**: The `RecordingTransport` intercepts `TransportMessage` which has serialized bytes. Deserializing back to a typed object requires knowing the message type (from `Headers.MessageType` or topic mapping). If type resolution is complex, may need to store raw bytes and deserialize lazily on `WaitFor<T>`.

## Sources & References

- **Origin document:** [docs/brainstorms/2026-03-19-messaging-test-harness-core-requirements.md](../brainstorms/2026-03-19-messaging-test-harness-core-requirements.md) — Key decisions: single package, full pipeline, thin assertions, per-instance isolation.
- Core messaging setup: `src/Headless.Messaging.Core/Setup.cs`
- InMemoryQueue setup: `src/Headless.Messaging.InMemoryQueue/Setup.cs`
- InMemoryStorage setup: `src/Headless.Messaging.InMemoryStorage/Setup.cs`
- Existing test harness: `tests/Headless.Messaging.Core.Tests.Harness/`
- Existing TestSubscriber: `tests/Headless.Messaging.Core.Tests.Harness/Helpers/TestSubscriber.cs`
- Package pattern: `src/Headless.Testing/Headless.Testing.csproj`
- Ideation: `docs/ideation/2026-03-19-messaging-test-harness-ideation.md`
