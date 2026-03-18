---
title: "feat: Add orchestration-based saga pattern support"
type: feat
date: 2026-03-18
origin: docs/brainstorms/2026-03-18-saga-pattern-brainstorm.md
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

> Full story details in companion PRD: [`2026-03-18-001-feat-saga-pattern-support-plan.prd.json`](./2026-03-18-001-feat-saga-pattern-support-plan.prd.json)

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
