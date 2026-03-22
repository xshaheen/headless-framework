---
title: "feat: Add orchestration-based saga pattern support"
type: feat
date: 2026-03-18
status: active
origin: docs/brainstorms/2026-03-18-saga-pattern-brainstorm.md
branches:
  base: main
  feature: xshaheen/saga-pattern
verification_command: dotnet test headless-framework.slnx
---

> **Verification gate:** Before claiming any task or story complete — run `dotnet test headless-framework.slnx` and confirm PASS. Do not mark complete based on reading code alone.

# feat: Add orchestration-based saga pattern support

## Overview

Add orchestration-based saga support to headless-framework. A saga is a sequence of steps that either all complete or compensate in reverse. Steps can invoke services directly (DI) or send commands via messaging and wait for replies. The implementation follows the framework's established abstraction + provider pattern, reusing existing messaging infrastructure for transport and storage.

## Problem Statement / Motivation

headless-framework provides messaging infrastructure (outbox, transport, storage) but lacks multi-step workflow orchestration. Consumers building distributed systems need saga/compensation patterns for operations spanning multiple services. Without this, teams must hand-roll state machines, compensation logic, and timeout handling — error-prone work that belongs in the framework.

## Proposed Solution

Builder DSL (inspired by Eventuate Tram) where step sequence + compensation pairs are visible in a single definition class. Three step types: local/direct (`Step`), command/reply (`Command`), and external event wait (`WaitFor`). All compile to a single internal runtime shape. Timeouts are first-class durable records. Compensation is explicit and reverse-order. Four NuGet packages following the abstraction + provider pattern.

See brainstorm for full API contract and rationale: [docs/brainstorms/2026-03-18-saga-pattern-brainstorm.md](../brainstorms/2026-03-18-saga-pattern-brainstorm.md)

## Technical Approach

### Architecture

```
┌──────────────────────────────────────────────────────┐
│  Headless.Sagas.Abstractions                         │
│  ISagaDefinition, ISagaBuilder, ISagaContext,         │
│  ISagaOrchestrator, ISagaManagement,                  │
│  ISagaStore, ISagaTimeoutStore, ISagaSerializer,      │
│  ISagaIdGenerator, ISagaExecutionObserver             │
│  State types, enums                                   │
└────────────────────────┬─────────────────────────────┘
                         │
┌────────────────────────▼─────────────────────────────┐
│  Headless.Sagas                                      │
│  Orchestrator runtime, step engine, compensation     │
│  engine, timeout polling, builder DSL, default impls │
│  Depends: Abstractions + Messaging.Core              │
└────────────────────────┬─────────────────────────────┘
                         │
     ┌───────────────────┼───────────────────┐
     │                                       │
┌────▼────────────────┐  ┌──────────────────▼──────────┐
│  .OpenTelemetry     │  │  .Testing                    │
│  ISagaExecution-    │  │  SagaTestHarness, in-memory  │
│  Observer impl,     │  │  runtime, fluent assertions  │
│  metrics + traces   │  │  Depends: Abstractions       │
│  Depends: Abstractions│ └────────────────────────────┘
└─────────────────────┘
```

**Dependency graph:**
```
Saga ──→ Messaging (transport + storage)
Saga ──→ Jobs      (timeout polling)
Messaging ╳ Saga
Jobs      ╳ Saga
```

Zero lines touched in existing messaging or jobs packages.

### Key Design Decisions (from brainstorm)

1. **Builder DSL** — step sequence + compensation visible in one definition (see brainstorm: decision #1)
2. **Single runtime shape** — `Command()` is syntactic sugar, not a separate engine (#3)
3. **Explicit compensation only** — never auto-generated (#4)
4. **Durable timeouts** — all three kinds compile to `SagaTimeoutRegistration` (#5)
5. **Optimistic concurrency** — `version` column, retry on `SagaConcurrencyException` (#13, #40)
6. **At-least-once execution** — steps are idempotent by contract (#19)
7. **Saga storage alongside messaging** — same schema, `UseSagaStorage()` extension (#7)
8. **Two correlation modes** — runtime headers for `Command()`, business keys for `WaitFor()` (#8)
9. **Reply type matching is exact CLR type** — no inheritance (#32)
10. **Fire-and-forget compensation commands** — `CompensateWith<T>()` does not wait for reply (#37)

### DI Registration Pattern

Follow existing `AddHeadlessMessaging` → `MessagingBuilder` pattern:
- Entry: `services.AddHeadlessSagas(options => { ... })` returns `SagaBuilder`
- Storage: `options.UseSagaStorage()` on existing messaging storage extension
- Extension mechanism: `ISagaOptionsExtension.AddServices(IServiceCollection)`
- Marker services for validation: `SagaMarkerService`
- Namespace: `Microsoft.Extensions.DependencyInjection`

Reference: `src/Headless.Messaging.Core/Setup.cs`, `src/Headless.Messaging.Core/Configuration/MessagingBuilder.cs`

### Saga Headers

Add saga-specific headers following existing naming convention (`headless-` prefix):
- `headless-saga-id` — saga instance ID
- `headless-saga-step` — current step index

Added to `src/Headless.Messaging.Abstractions/Headers.cs` alongside existing headers.

## Stories

| ID | Title | Size | Phase |
|----|-------|------|-------|
| US-001 | Create saga package scaffolding | S | 1. Foundation |
| US-002 | Define core abstractions (interfaces + types) | M | 1. Foundation |
| US-003 | Define persistence and extension abstractions | S | 1. Foundation |
| US-004 | Implement builder DSL + step graph compilation | M | 2. Builder |
| US-005 | Build-time validation rules | S | 2. Builder |
| US-006 | Orchestrator core — start + forward local step execution | L | 3. Runtime |
| US-007 | Compensation engine | M | 3. Runtime |
| US-008 | Conditional steps (When) + retry system | S | 3. Runtime |
| US-009 | Saga headers + command/reply step execution | L | 4. Messaging |
| US-010 | WaitFor step execution + event delivery | M | 4. Messaging |
| US-011 | Timeout system (registration, store, polling, firing) | L | 5. Timeout |
| US-012 | Cancellation + lifecycle hooks | M | 6. Lifecycle |
| US-013 | Management operations (retry/skip/resolve) | M | 6. Lifecycle |
| US-014 | DB schema + ISagaStore implementation | L | 7. Persistence |
| US-015 | Saga events audit system | M | 7. Persistence |
| US-016 | DI registration + messaging integration | M | 8. Registration |
| US-017 | ISagaExecutionObserver + structured logging | S | 9. Observability |
| US-018 | Headless.Sagas.OpenTelemetry package | M | 9. Observability |
| US-019 | SagaTestHarness — local step testing | M | 10. Testing |
| US-020 | SagaTestHarness — command/reply + waitfor + timeout | M | 10. Testing |

### Phase 1: Foundation

#### US-001 — Create saga package scaffolding [S]

- [ ] Complete

Sequential: create 4 .csproj files → add to solution → add package refs to Directory.Packages.props → verify build.

Create project structure for Headless.Sagas.Abstractions, Headless.Sagas, Headless.Sagas.OpenTelemetry, Headless.Sagas.Testing. Follow Messaging package layout: RootNamespace, InternalsVisibleTo, package dependencies. Add entries to Directory.Packages.props for any new dependencies. Add projects to headless-framework.slnx.

**Files to study:**
- `src/Headless.Messaging.Abstractions/Headless.Messaging.Abstractions.csproj`
- `src/Headless.Messaging.Core/Headless.Messaging.Core.csproj`
- `src/Headless.Messaging.OpenTelemetry/Headless.Messaging.OpenTelemetry.csproj`
- `Directory.Packages.props`
- `headless-framework.slnx`

**Acceptance criteria:**
- [ ] Four .csproj files created with correct dependencies and RootNamespace
- [ ] Projects added to headless-framework.slnx and build succeeds with dotnet build

---

#### US-002 — Define core abstractions (interfaces + types) [M]

- [ ] Complete

TDD order: write failing test → implement minimal code → verify PASS.

Define in Headless.Sagas.Abstractions: ISagaDefinition\<TState>, ISagaBuilder\<TState>, ISagaContext\<TState>, IStepOptionsBuilder\<TState>, ICommandStepBuilder\<TState>, IWaitStepOptionsBuilder\<TState>, ISagaOrchestrator, ISagaManagement. State types: SagaInstance, SagaStepLogEntry, SagaStatusInfo, SagaTimeoutRegistration. Enums: SagaRuntimeStatus, StepLogStatus, SagaTimeoutKind. All per brainstorm API contract.

**Files to study:**
- `src/Headless.Messaging.Abstractions/IMessagePublisher.cs`
- `src/Headless.Messaging.Abstractions/IConsume.cs`
- `src/Headless.Messaging.Abstractions/ConsumeContext.cs`
- `docs/brainstorms/2026-03-18-saga-pattern-brainstorm.md`

**Acceptance criteria:**
- [ ] All interfaces from brainstorm §API Contract defined with correct signatures and XML docs
- [ ] State records (SagaInstance, SagaStepLogEntry, SagaStatusInfo, SagaTimeoutRegistration) match brainstorm §Execution Model and §Timeout Design
- [ ] Enums (SagaRuntimeStatus, StepLogStatus, SagaTimeoutKind) match brainstorm definitions
- [ ] Package compiles with zero warnings; CSharpier formatted

---

#### US-003 — Define persistence and extension abstractions [S]

- [ ] Complete

Sequential: define interfaces → verify compile.

Define ISagaStore (GetAsync, SaveAsync with optimistic concurrency, FindWaitingAsync, GetByStatusAsync), ISagaTimeoutStore (ScheduleAsync, CancelAsync, CancelAllForSagaAsync, GetDueAsync), ISagaSerializer, ISagaIdGenerator, ISagaExecutionObserver. Define SagaConcurrencyException with primary constructor. Add SagaOptions class stub for configuration.

**Files to study:**
- `src/Headless.Messaging.Core/Persistence/IDataStorage.cs`
- `src/Headless.Messaging.Core/Persistence/IStorageInitializer.cs`
- `src/Headless.Messaging.Core/Configuration/IMessagesOptionsExtension.cs`
- `docs/brainstorms/2026-03-18-saga-pattern-brainstorm.md`

**Acceptance criteria:**
- [ ] ISagaStore, ISagaTimeoutStore, ISagaSerializer, ISagaIdGenerator, ISagaExecutionObserver defined per brainstorm §Runtime Extension Points
- [ ] SagaConcurrencyException carries SagaId, ExpectedVersion, ActualVersion
- [ ] ISagaTimeoutStore includes CancelAllForSagaAsync(sagaId) for terminal state cleanup

---

### Phase 2: Builder

#### US-004 — Implement builder DSL + step graph compilation [M]

- [ ] Complete

TDD order: write failing test → implement minimal code → verify PASS.

Implement SagaBuilder\<TState> that collects step definitions into an immutable compiled step graph. Internal step descriptor model: StepDescriptor with execute, compensate, transition handler, options. StepOptionsBuilder, CommandStepBuilder, WaitStepOptionsBuilder as fluent builder implementations. Compiled graph is a sealed, immutable IReadOnlyList\<CompiledStep>. Build() called once at startup; runtime never mutates the graph.

**Files to study:**
- `src/Headless.Messaging.Core/Configuration/MessagingOptions.cs`
- `docs/brainstorms/2026-03-18-saga-pattern-brainstorm.md`

**Acceptance criteria:**
- [ ] SagaBuilder\<TState> implements ISagaBuilder\<TState> with all three step types (Step, Command, WaitFor)
- [ ] Compiled step graph is immutable (sealed record/class, IReadOnlyList)
- [ ] Lifecycle hooks (Timeout, Completed, Failed) captured in compilation
- [ ] Unit tests verify builder produces correct step descriptors for each step type

---

#### US-005 — Build-time validation rules [S]

- [ ] Complete

TDD order: write failing test → implement minimal code → verify PASS.

Implement all build-time validation rules from brainstorm §Build-Time Validation: unique saga Name, unique step names, at most one global timeout, at most one Completed/Failed hook, Command steps require OnReply/OnFailure, no duplicate reply types within same Command step, exact CLR type matching rejects overlapping handler types, non-empty destinations, no dual compensation (Compensate + CompensateWith), WaitFor requires both sagaKey and eventKey. Throw descriptive exceptions on violation.

**Files to study:**
- `docs/brainstorms/2026-03-18-saga-pattern-brainstorm.md`

**Acceptance criteria:**
- [ ] Each validation rule from brainstorm §Build-Time Validation has a dedicated failing test + implementation
- [ ] Validation exceptions include saga name, step name, and specific rule violated
- [ ] Reply type overlap detection rejects assignable types (base/derived)

---

### Phase 3: Runtime

#### US-006 — Orchestrator core — start + forward local step execution [L]

- [ ] Complete

TDD order: write failing test → implement minimal code → verify PASS.

Implement SagaOrchestrator.StartAsync: create SagaInstance, persist initial state, execute forward steps sequentially. For local Step(): invoke lambda, persist step log on success, advance step index. On exception: transition to compensation. Optimistic concurrency: catch SagaConcurrencyException from ISagaStore.SaveAsync, reload saga, retry (max configurable retries). State serialization via ISagaSerializer. ID generation via ISagaIdGenerator. ISagaContext\<TState> implementation with Services, PublishAsync, SetStepData/GetStepData, FailAsync.

**Files to study:**
- `src/Headless.Messaging.Core/Internal/OutboxPublisher.cs`
- `src/Headless.Messaging.Core/Internal/ISubscribeExecutor.cs`
- `src/Headless.Messaging.Abstractions/IMessagePublisher.cs`
- `docs/brainstorms/2026-03-18-saga-pattern-brainstorm.md`

**Acceptance criteria:**
- [ ] StartAsync creates SagaInstance, persists, returns saga ID
- [ ] Forward execution loop runs local steps sequentially, persisting step log after each
- [ ] Step exception triggers transition to Compensating status
- [ ] SagaConcurrencyException triggers reload + retry with configurable max (default 10)
- [ ] ISagaContext provides Services, PublishAsync (via IMessagePublisher), SetStepData/GetStepData, FailAsync
- [ ] GetStatusAsync returns SagaStatusInfo with step log

---

#### US-007 — Compensation engine [M]

- [ ] Complete

TDD order: write failing test → implement minimal code → verify PASS.

Implement reverse-order compensation: walk saga_step_log in LIFO order, skip entries with Status=Skipped, invoke compensate handler for Completed entries. GetStepData\<T> loads CompensationDataJson from step log. On compensation success: write Compensated entry. On compensation failure: retry per CompensationRetry config (per-step or global default). After retry exhaustion: Stuck status + OnSagaStuck callback. Failed lifecycle hook fires only after successful compensation.

**Files to study:**
- `docs/brainstorms/2026-03-18-saga-pattern-brainstorm.md`

**Acceptance criteria:**
- [ ] Compensation walks completed steps in reverse LIFO order
- [ ] Skipped steps are ignored during compensation
- [ ] CompensationRetry (per-step and global) retries with configurable backoff
- [ ] After retry exhaustion, saga transitions to Stuck and OnSagaStuck fires
- [ ] Failed lifecycle hook fires after successful compensation; never on Stuck

---

#### US-008 — Conditional steps (When) + retry system [S]

- [ ] Complete

TDD order: write failing test → implement minimal code → verify PASS.

When() predicate: evaluated once on step entry, persisted as Skipped in step log if false, never re-evaluated on retry. Retry system: in-memory attempt counter, Task.Delay for delay, step timeout spans all attempts. Command retry = send phase only. Reset on process restart (safe: idempotent). Saga events record StepRetried.

**Files to study:**
- `docs/brainstorms/2026-03-18-saga-pattern-brainstorm.md`

**Acceptance criteria:**
- [ ] When() false → step log entry with Status=Skipped, step index advances
- [ ] When() not re-evaluated on retry of same step
- [ ] Step retry uses in-memory counter with Task.Delay; step timeout spans all attempts
- [ ] StepRetried saga event emitted per retry attempt

---

### Phase 4: Messaging

#### US-009 — Saga headers + command/reply step execution [L]

- [ ] Complete

TDD order: write failing test → implement minimal code → verify PASS.

Add headless-saga-id and headless-saga-step headers to Headers.cs. Command step: build command via lambda, publish via IMessagePublisher with saga headers + destination, persist saga as WaitingForReply. Reply consumer: receive reply, route by SagaId + StepIndex headers, match CLR type to OnReply/OnFailure handlers (exact type only), dispatch handler, advance or compensate. CompensateWith\<T>: fire-and-forget send during compensation. Reply safety: ignore late, duplicate, stale, unrecognized replies per brainstorm §Reply and Event Safety. Provide ConsumeContext extension for participant reply helper.

**Files to study:**
- `src/Headless.Messaging.Abstractions/Headers.cs`
- `src/Headless.Messaging.Abstractions/PublishOptions.cs`
- `src/Headless.Messaging.Abstractions/IRuntimeSubscriber.cs`
- `src/Headless.Messaging.Core/Transport/IDispatcher.cs`
- `docs/brainstorms/2026-03-18-saga-pattern-brainstorm.md`

**Acceptance criteria:**
- [ ] Headers.SagaId and Headers.SagaStepIndex constants added following existing naming convention
- [ ] Command step publishes command with saga headers to specified destination
- [ ] Reply routing matches SagaId + StepIndex from headers, then exact CLR type to handler
- [ ] OnFailure handler runs, mutates state, then transitions to compensation
- [ ] CompensateWith sends compensating command fire-and-forget; publish failure → Stuck
- [ ] Late, duplicate, stale, and unrecognized replies are ignored per safety rules
- [ ] Participant reply helper extension copies saga headers into reply

---

#### US-010 — WaitFor step execution + event delivery [M]

- [ ] Complete

TDD order: write failing test → implement minimal code → verify PASS.

WaitFor step: persist saga as WaitingForEvent with type + key. PublishEventAsync: extract event key via compiled registry (event CLR type → sagaDefinition key extractor), query ISagaStore.FindWaitingAsync, deliver to each matching saga independently. RaiseEventToSagaAsync: direct delivery by saga ID, validate event type matches WaitingForEventType (ignore if mismatch). Apply handler: invoke, advance saga. Event safety: ignore events for non-waiting sagas.

**Files to study:**
- `src/Headless.Messaging.Abstractions/IRuntimeSubscriber.cs`
- `src/Headless.Messaging.Abstractions/ConsumeContext.cs`
- `docs/brainstorms/2026-03-18-saga-pattern-brainstorm.md`

**Acceptance criteria:**
- [ ] WaitFor step persists WaitingForEventType + WaitingForEventKey on saga instance
- [ ] PublishEventAsync uses compiled registry to extract event key and queries FindWaitingAsync
- [ ] Multiple matching sagas each receive independent delivery; one failure doesn't block others
- [ ] RaiseEventToSagaAsync delivers by saga ID; ignores if event type doesn't match
- [ ] Apply handler failure triggers compensation

---

### Phase 5: Timeout

#### US-011 — Timeout system (registration, store, polling, firing) [L]

- [ ] Complete

TDD order: write failing test → implement minimal code → verify PASS.

SagaTimeoutRegistration persisted via ISagaTimeoutStore. Three timeout kinds: Step (registered on step entry, cancelled on completion), Wait (registered on WaitingForEvent/WaitingForReply, cancelled on reply/event), Saga (registered at StartAsync, cancelled on terminal state). Polling hosted service: GetDueAsync in configurable interval, fire each, validate staleness before executing. Step timeout: cooperative CancellationTokenSource linked to duration. Wait/Command timeout: persisted timer, check by poller. CancelAllForSagaAsync on terminal state transitions.

**Files to study:**
- `src/Headless.Messaging.Core/Internal/OutboxPublisher.cs`
- `docs/brainstorms/2026-03-18-saga-pattern-brainstorm.md`

**Acceptance criteria:**
- [ ] SagaTimeoutRegistration persisted for all three timeout kinds at correct lifecycle points
- [ ] Polling hosted service calls GetDueAsync and processes due timeouts
- [ ] Stale timeout validation: ignore if saga terminal, step index mismatch, or state mismatch
- [ ] Step timeout uses cooperative CancellationTokenSource; non-cooperative steps still transition to Compensating
- [ ] CancelAllForSagaAsync called when saga reaches terminal state
- [ ] Timeout firing emits TimeoutFired saga event

---

### Phase 6: Lifecycle

#### US-012 — Cancellation + lifecycle hooks [M]

- [ ] Complete

TDD order: write failing test → implement minimal code → verify PASS.

CancelAsync: transition to Cancelling, run compensation in reverse, terminal state Cancelled (success) or Stuck (failure). Completed hook: fires after all forward steps succeed. Failed hook: fires after successful compensation (not on Stuck). Saga-wide timeout handler invocation. FailAsync on ISagaContext: valid during forward execution only, throws InvalidOperationException during compensation.

**Files to study:**
- `docs/brainstorms/2026-03-18-saga-pattern-brainstorm.md`

**Acceptance criteria:**
- [ ] CancelAsync transitions Running/WaitingForEvent/WaitingForReply → Cancelling → compensation → Cancelled or Stuck
- [ ] Completed hook fires exactly once on successful completion of all forward steps
- [ ] Failed hook fires exactly once after successful compensation (never on Stuck)
- [ ] FailAsync during compensation throws InvalidOperationException

---

#### US-013 — Management operations (retry/skip/resolve) [M]

- [ ] Complete

TDD order: write failing test → implement minimal code → verify PASS.

Implement ISagaManagement: RetryCompensationAsync (resume from failed compensation step, audit event with actor/reason), SkipFailedCompensationAsync (skip stuck compensation step, continue reverse, compensation only — never forward), MarkResolvedAsync (terminal Resolved state with reason/actor/notes/timestamp audit), GetStuckSagasAsync (query by status, optional olderThan filter). All operations emit OperatorRetry/OperatorSkip/OperatorResolved saga events.

**Files to study:**
- `docs/brainstorms/2026-03-18-saga-pattern-brainstorm.md`

**Acceptance criteria:**
- [ ] RetryCompensationAsync resumes compensation from failed step; emits OperatorRetry event
- [ ] SkipFailedCompensationAsync skips stuck compensation step; continues reverse; rejects if not in Stuck status
- [ ] MarkResolvedAsync sets Resolved terminal state with audit (reason, actor, notes, timestamp)
- [ ] GetStuckSagasAsync queries by Stuck status with optional olderThan filter
- [ ] All operations record audit details in saga_events

---

### Phase 7: Persistence

#### US-014 — DB schema + ISagaStore implementation [L]

- [ ] Complete

TDD order: write failing test → implement minimal code → verify PASS.

DB tables: saga_instances, saga_step_log, saga_events, saga_timeouts (per brainstorm §DB Schema). ISagaStore implementation for PostgreSQL (primary) with optimistic concurrency (version column check in UPDATE WHERE). IStorageInitializer extension to create saga tables alongside messaging tables (IF NOT EXISTS). ISagaTimeoutStore default implementation using saga_timeouts table. Indexes per brainstorm + additional ix_saga_name_status composite index. Integration tests with Testcontainers PostgreSQL.

**Files to study:**
- `src/Headless.Messaging.PostgreSql/PostgreSqlStorageInitializer.cs`
- `src/Headless.Messaging.PostgreSql/PostgreSqlDataStorage.cs`
- `src/Headless.Messaging.PostgreSql/Setup.cs`
- `docs/brainstorms/2026-03-18-saga-pattern-brainstorm.md`

**Acceptance criteria:**
- [ ] Four saga tables created with correct schema matching brainstorm DDL
- [ ] ISagaStore.SaveAsync uses optimistic concurrency (version check in UPDATE WHERE)
- [ ] ISagaStore.FindWaitingAsync queries by waiting_event + waiting_key
- [ ] Storage initializer creates saga tables alongside messaging tables (IF NOT EXISTS, idempotent)
- [ ] Integration tests verify CRUD operations, concurrency conflict detection, and query methods

---

#### US-015 — Saga events audit system [M]

- [ ] Complete

TDD order: write failing test → implement minimal code → verify PASS.

Append-only saga_events recording: StatusChanged, StepCompleted, StepFailed, StepRetried, CompensationStarted, CompensationCompleted, CompensationFailed, TimeoutFired, OperatorRetry, OperatorSkip, OperatorResolved, Cancelled. Each event has typed detail JSON per brainstorm §detail column shape. Events batched with step log writes — no additional persistence round-trips. Query methods for dashboard timeline.

**Files to study:**
- `docs/brainstorms/2026-03-18-saga-pattern-brainstorm.md`

**Acceptance criteria:**
- [ ] All 12 event types from brainstorm recorded with correct detail JSON shape
- [ ] Events appended atomically alongside saga instance + step log saves
- [ ] Query by saga_id returns chronological event stream for dashboard timeline
- [ ] No additional persistence round-trips — events batch with existing writes

---

### Phase 8: Registration

#### US-016 — DI registration + messaging integration [M]

- [ ] Complete

TDD order: write failing test → implement minimal code → verify PASS.

AddHeadlessSagas(options): assembly scanning (AddSagasFromAssembly), individual registration (AddSaga\<T,S>), CompensationRetry global config, OnSagaStuck callback, OnIgnoredMessage callback. ISagaOptionsExtension pattern matching IMessagesOptionsExtension. UseSagaStorage() on messaging storage providers. SagaMarkerService for validation. Saga reply consumer registration. Event key registry compilation at startup. Default implementations: System.Text.Json serializer, Guid ID generator, no-op observer.

**Files to study:**
- `src/Headless.Messaging.Core/Setup.cs`
- `src/Headless.Messaging.Core/Configuration/MessagingBuilder.cs`
- `src/Headless.Messaging.Core/Configuration/MessagingOptions.cs`
- `src/Headless.Messaging.Core/Configuration/IMessagesOptionsExtension.cs`

**Acceptance criteria:**
- [ ] AddHeadlessSagas registers orchestrator, management, default implementations
- [ ] Assembly scanning discovers ISagaDefinition\<T> implementations and registers them
- [ ] UseSagaStorage() extension adds saga table initialization to existing storage providers
- [ ] Event key registry compiled at startup from all registered saga definitions with WaitFor steps
- [ ] Options validation rejects configuration without storage provider

---

### Phase 9: Observability

#### US-017 — ISagaExecutionObserver + structured logging [S]

- [ ] Complete

Sequential: implement no-op observer → add structured logging → verify.

Default no-op ISagaExecutionObserver implementation. Structured log entries with SagaId, SagaName, StepName, StepIndex as scoped properties. Log levels: Information (normal flow), Warning (retries/timeouts), Error (failures/stuck). Multiple observers supported via composite pattern.

**Files to study:**
- `src/Headless.Messaging.Core/Diagnostics/MessageDiagnosticListenerNames.cs`

**Acceptance criteria:**
- [ ] Default no-op ISagaExecutionObserver registered as fallback
- [ ] Structured logging uses ILogger with SagaId/SagaName/StepName/StepIndex scoped properties
- [ ] Log levels follow brainstorm spec: Info for normal, Warning for retries/timeouts, Error for failures

---

#### US-018 — Headless.Sagas.OpenTelemetry package [M]

- [ ] Complete

TDD order: write failing test → implement minimal code → verify PASS.

ISagaExecutionObserver implementation emitting OTel metrics + traces per brainstorm §Observability. Metrics: saga.started, saga.completed, saga.failed, saga.cancelled, saga.stuck, saga.step.duration, saga.step.retries, saga.compensation.count, saga.timeout.fired, saga.reply.ignored. Traces: saga.execute, saga.step, saga.command.send, saga.reply.handle, saga.event.handle, saga.compensate, saga.timeout.fire. Setup via TracerProviderBuilder.AddSagaInstrumentation(). Follow Headless.Messaging.OpenTelemetry patterns.

**Files to study:**
- `src/Headless.Messaging.OpenTelemetry/Setup.cs`
- `src/Headless.Messaging.OpenTelemetry/MessagingMetrics.cs`
- `src/Headless.Messaging.OpenTelemetry/MessagingInstrumentation.cs`
- `docs/brainstorms/2026-03-18-saga-pattern-brainstorm.md`

**Acceptance criteria:**
- [ ] All 10 metrics from brainstorm §Observability emitted with correct tags
- [ ] All 7 trace spans emitted with correct parent-child relationships and attributes
- [ ] AddSagaInstrumentation() extension on TracerProviderBuilder registers observer
- [ ] Follows Headless.Messaging.OpenTelemetry pattern (DiagnosticListener, metrics class)

---

### Phase 10: Testing

#### US-019 — SagaTestHarness — local step testing [M]

- [ ] Complete

TDD order: write failing test → implement minimal code → verify PASS.

In-memory saga runtime for unit testing. SagaTestHarness.For\<TSaga, TState>(initialState) → WithService\<T>(mock) → RunToCompletionAsync(). Result object: Status, State, CompletedSteps, CompensatedSteps, PublishedMessages. No real messaging or persistence. Fluent assertions for step execution order, state mutations, compensation. Test compensation flow, conditional step skip (When), step failure.

**Files to study:**
- `docs/brainstorms/2026-03-18-saga-pattern-brainstorm.md`

**Acceptance criteria:**
- [ ] SagaTestHarness.For\<TSaga,TState> creates in-memory test runtime
- [ ] WithService\<T> registers mock services for DI resolution in step lambdas
- [ ] RunToCompletionAsync executes all steps and returns result with Status, State, CompletedSteps
- [ ] Compensation testing: step failure triggers reverse execution; CompensatedSteps populated
- [ ] PublishedMessages captures all messages sent via ctx.PublishAsync

---

#### US-020 — SagaTestHarness — command/reply + waitfor + timeout [M]

- [ ] Complete

TDD order: write failing test → implement minimal code → verify PASS.

Extend SagaTestHarness for messaging step simulation. Fluent API: Start() → ExpectCommand\<T>(destination) → ReplyWith\<T>(reply) / ReplyWithFailure\<T>(failure). ExpectCompensationCommand\<T>(destination). ExpectWaitFor\<T>() → RaiseEvent\<T>(event). SimulateTimeout(). SimulateCancel(). CompleteAsync(). Test stuck-saga scenarios (compensation failure). Test When() conditional skip in mixed flows.

**Files to study:**
- `docs/brainstorms/2026-03-18-saga-pattern-brainstorm.md`

**Acceptance criteria:**
- [ ] ExpectCommand + ReplyWith simulates command/reply round-trip
- [ ] ReplyWithFailure triggers OnFailure handler then compensation
- [ ] ExpectCompensationCommand captures compensating commands
- [ ] ExpectWaitFor + RaiseEvent simulates external event delivery
- [ ] SimulateTimeout triggers timeout flow for current waiting step
- [ ] SimulateCancel triggers cancellation flow

## Final Acceptance Criteria

### Functional Requirements

- [ ] Saga definitions compile at startup; invalid definitions throw with clear messages
- [ ] Local steps execute sequentially, advancing on success, compensating on failure
- [ ] Command/reply steps send commands with saga headers, route replies by `SagaId + StepIndex`
- [ ] WaitFor steps block until matching external event (type + business key)
- [ ] Compensation runs in reverse LIFO order for completed steps only
- [ ] Three timeout kinds (step, wait, saga) fire correctly with stale validation
- [ ] Stuck sagas support retry, skip, and manual resolution via ISagaManagement
- [ ] Cancellation transitions through Cancelling → Cancelled (or Stuck)
- [ ] All step types support When() conditional skip
- [ ] PublishEventAsync delivers to all matching waiting sagas independently

### Non-Functional Requirements

- [ ] Zero changes to existing Messaging or Jobs packages
- [ ] Optimistic concurrency prevents dual execution; no distributed locks
- [ ] All public APIs have XML documentation
- [ ] README.md exists for each package
- [ ] Package versions managed in Directory.Packages.props only

### Quality Gates

- [ ] Line coverage ≥85%, branch ≥80% for new packages
- [ ] All unit tests pass (SagaTestHarness + direct tests)
- [ ] Integration tests with PostgreSQL via Testcontainers
- [ ] Build-time validation has dedicated test coverage for every rule
- [ ] CSharpier formatting passes

## System-Wide Impact

### Interaction Graph

`ISagaOrchestrator.StartAsync()` → builds saga context → executes step lambdas sequentially → each step may call `IMessagePublisher.PublishAsync()` for domain events or `ctx.PublishAsync()` → step log + saga events written per transition → on `Command()` step, message published with saga headers → reply consumed by saga reply consumer → routed back to orchestrator → state advanced.

### Error & Failure Propagation

Step lambda throws → orchestrator catches → compensation starts in reverse → if compensation step throws → retry per `CompensationRetry` config → if exhausted → `Stuck` status → `OnSagaStuck` callback. `SagaConcurrencyException` from `ISagaStore.SaveAsync` → orchestrator reloads + retries internally.

### State Lifecycle Risks

Partial failure between step execution and step log persistence: step re-executes on restart (safe: idempotent by contract). `SetStepData` during step → lost if process crashes before save → compensation gets null data. Documented trade-off.

### API Surface Parity

`ISagaOrchestrator` is the runtime API. `ISagaManagement` is the operations API. No overlap with existing `IMessagePublisher` — saga uses it internally. `ISagaStore` parallels `IDataStorage` for messaging.

### Integration Test Scenarios

1. Full saga lifecycle: start → 3 mixed steps → complete → verify step log + saga events
2. Command/reply with timeout: send command → timeout fires → compensation runs → verify `Failed` status
3. Concurrent execution: two threads process same saga → one wins optimistic concurrency → both eventually consistent
4. Stuck recovery: compensation fails → `Stuck` → `RetryCompensationAsync` → succeeds → `Failed`
5. WaitFor multi-saga: two sagas waiting same event type + key → `PublishEventAsync` → both advance independently

## Alternative Approaches Considered

See brainstorm §Why Builder DSL for full analysis. Rejected: state machines (MassTransit — ceremony), handler interfaces (Rebus/NServiceBus — scattered), convention methods (Wolverine — magic), code-as-workflow (Temporal/Dapr — replay complexity).

## Dependencies & Prerequisites

- Existing `Headless.Messaging.Abstractions` + `Headless.Messaging.Core` packages
- `Headless.Hosting` for hosted services (timeout poller)
- `Headers.cs` must be extended with saga headers
- Storage providers (PostgreSQL, SQL Server) need `UseSagaStorage()` extension

## Risk Analysis & Mitigation

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| Optimistic concurrency hot spots under high load | Medium | Medium | Documented trade-off; distributed locks can be added later |
| Reply routing race with timeout | Low | High | Stale timeout validation; ignore replies after compensation |
| Step data loss on crash between execute and persist | Low | Medium | Idempotency contract; compensation handles null step data |
| Complex build-time validation missing edge cases | Medium | Low | Comprehensive test coverage per validation rule |

## Identified Gaps (from SpecFlow Analysis)

The following gaps were identified during specification analysis. They should be resolved during implementation:

### Critical (Address in Stories)

1. **ISagaStore contract needs step log + event methods** — `SaveAsync` must batch step log entries and saga events atomically. Consider `SaveAsync(SagaInstance, IEnumerable<SagaStepLogEntry>, IEnumerable<SagaEvent>)` overload or navigation properties.
2. **Event key extraction at runtime** — `PublishEventAsync` needs a compiled registry mapping event CLR types → `(sagaDefinitionName, eventKeyExtractor)`. Build during startup from all registered saga definitions with `WaitFor<T>` steps.
3. **IdempotencyKey runtime semantics** — Define how the key is used: store in step log, check before re-execution, skip step if matching key found.

### Important (Document in Implementation)

4. **Participant reply helper** — Provide `ConsumeContext` extension or `SagaReplyPublisher` to auto-copy saga headers into replies. Zero-ceremony for participant services.
5. **`RaiseEventToSagaAsync` type validation** — If event CLR type doesn't match `WaitingForEventType`, ignore (consistent with reply safety table).
6. **Failed hook invocation rules** — `Failed` hook fires only after successful compensation. Not on `Stuck`. Document clearly.
7. **Timeout bulk cancel** — Add `CancelAllForSagaAsync(sagaId)` to `ISagaTimeoutStore` for terminal state cleanup.
8. **Concurrency retry cap** — Add configurable max internal retries (default: 10) with `SagaConcurrencyRetriesExhaustedException`.
9. **`FailAsync` valid states** — Only valid during forward execution. Throws `InvalidOperationException` during compensation.
10. **WaitFor apply failure** — `apply` handler throws → saga fails → compensation starts (consistent with local step failure).
11. **WaitFor is non-compensable** — Document explicitly. `IWaitStepOptionsBuilder` intentionally has no `Compensate`.

### Completeness (Nice to Have)

12. **When() on WaitFor** — Not supported in v1. Document intentional absence.
13. **Test harness gaps** — Add `SimulateCancel()`, stuck-saga testing, `When()` skip testing in US-020.
14. **`OnIgnoredMessage` callback** — Add to `SagaOptions` in DI registration.
15. **Missing `saga_name` composite index** — Add `CREATE INDEX ix_saga_name_status ON saga_instances (saga_name, status)`.

## Documentation Plan

- README.md for each of the 4 packages
- XML docs on all public APIs
- Update CLAUDE.md with saga package conventions

## Research Findings

- DI registration follows `AddHeadlessMessaging` → `MessagingBuilder` → `ISagaOptionsExtension.AddServices` pattern (`src/Headless.Messaging.Core/Setup.cs:65-80`)
- Storage: `IStorageInitializer` creates tables idempotently; `IDataStorage` for CRUD (`src/Headless.Messaging.Core/Persistence/`)
- Headers: `headless-*` prefix convention; saga adds `headless-saga-id` + `headless-saga-step` (`src/Headless.Messaging.Abstractions/Headers.cs`)
- OpenTelemetry: `DiagnosticListener` + `DiagnosticSourceSubscriber` pattern (`src/Headless.Messaging.OpenTelemetry/`)
- Package layering: Abstractions (interfaces only) → Core (runtime + config) → Provider (storage/transport)
- `IRuntimeSubscriber` for dynamic subscriptions (reply listener mechanism) at `src/Headless.Messaging.Abstractions/IRuntimeSubscriber.cs`
- C# 14 extension methods syntax used for `MessagingOptions` extensions (`src/Headless.Messaging.SqlServer/Setup.cs`)
- Test harness: abstract base classes with `ConfigureTransport`/`ConfigureStorage` overrides (`tests/Headless.Messaging.Core.Tests.Harness/`)
- No existing saga code — greenfield implementation. No prior solutions in `docs/solutions/`
- Marker services for validation: `MessagingMarkerService`, `MessageStorageMarkerService` pattern

## Sources & References

### Origin

- **Brainstorm document:** [docs/brainstorms/2026-03-18-saga-pattern-brainstorm.md](../brainstorms/2026-03-18-saga-pattern-brainstorm.md) — Key decisions carried forward: builder DSL over state machines (#1), single runtime shape (#3), durable timeouts (#5), optimistic concurrency without distributed locks (#40), explicit compensation only (#4). All 43 decisions + 9 resolved questions incorporated.

### Internal References

- DI registration pattern: `src/Headless.Messaging.Core/Setup.cs`
- Options extension pattern: `src/Headless.Messaging.Core/Configuration/IMessagesOptionsExtension.cs`
- Storage initializer: `src/Headless.Messaging.PostgreSql/PostgreSqlStorageInitializer.cs`
- Headers: `src/Headless.Messaging.Abstractions/Headers.cs`
- OpenTelemetry pattern: `src/Headless.Messaging.OpenTelemetry/Setup.cs`
- Diagnostics: `src/Headless.Messaging.Core/Diagnostics/MessageDiagnosticListenerNames.cs`
- Runtime subscriber: `src/Headless.Messaging.Abstractions/IRuntimeSubscriber.cs`

### External References

- Eventuate Tram Sagas (builder DSL inspiration)
- MassTransit Automatonymous (state machine — rejected approach)
- NServiceBus Sagas (enterprise reference)
