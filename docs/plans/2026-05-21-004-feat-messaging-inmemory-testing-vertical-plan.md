---
date: 2026-05-21
type: feat
status: completed
depth: deep
origin: docs/plans/2026-05-21-001-feat-messaging-bus-queue-split-plan.md
part_of: messaging-bus-queue-split
sequence: 3
depends_on:
  - docs/plans/2026-05-21-002-feat-messaging-foundation-contracts-plan.md
  - docs/plans/2026-05-21-003-feat-messaging-intent-persistence-drainer-plan.md
---

# feat: Messaging InMemory Vertical Slice and Testing Harness

## Summary

Build the first executable end-to-end vertical slice of the bus/queue model. This slice renames `Headless.Messaging.InMemoryQueue` to `Headless.Messaging.InMemory`, adds both in-process bus and queue transports, and migrates `Headless.Messaging.Testing` so later provider work has a trustworthy intent-aware harness.

Parent design map: [2026-05-21-001-feat-messaging-bus-queue-split-plan.md](2026-05-21-001-feat-messaging-bus-queue-split-plan.md).

## Scope

In scope:

- Parent unit: U6.
- Testing-package portions of U11.
- Requirements: R13, R14, R16, and the test-harness portion of R18.
- Acceptance examples: end-to-end proof for bus and queue intent through in-memory transport/storage.

Out of scope:

- External broker migrations.
- OTel/dashboard tags and projections.
- Full documentation rewrite, except README rename/update for the InMemory package and testing-package XML/docs needed by changed APIs.

## Key Decisions

- Move testing-harness work into this slice, not the final docs slice. Provider migrations should start only after the harness can observe bus and queue independently.
- `Headless.Messaging.InMemory` is a dual-intent provider: `InMemoryBusTransport : IBusTransport` and `InMemoryQueueTransport : IQueueTransport`.
- Queue semantics remain point-to-point; bus semantics fan out to all matching subscribers.
- `MessagingTestHarness` records intent and can wait on bus vs queue observations independently.

## Files

- Rename directory: `src/Headless.Messaging.InMemoryQueue/` -> `src/Headless.Messaging.InMemory/`
- Rename csproj: `src/Headless.Messaging.InMemory/Headless.Messaging.InMemory.csproj`
- Modify/rename: `src/Headless.Messaging.InMemory/InMemoryQueueTransport.cs`
- Create: `src/Headless.Messaging.InMemory/InMemoryBusTransport.cs`
- Modify: `src/Headless.Messaging.InMemory/Setup.cs`
- Modify/create: `src/Headless.Messaging.InMemory/README.md`
- Rename: `tests/Headless.Messaging.InMemoryQueue.Tests.Unit/` -> `tests/Headless.Messaging.InMemory.Tests.Unit/`
- Modify: `tests/Headless.Messaging.Core.Tests.Harness/TransportTestsBase.cs`
- Modify: `tests/Headless.Messaging.Core.Tests.Harness/MessagingIntegrationTestsBase.cs`
- Modify: `tests/Headless.Messaging.Core.Tests.Harness/DataStorageTestsBase.cs`
- Modify: `src/Headless.Messaging.Testing/Headless.Messaging.Testing.csproj`
- Modify: `src/Headless.Messaging.Testing/MessagingTestHarness.cs`
- Modify: `src/Headless.Messaging.Testing/Internal/RecordingTransport.cs`
- Modify: `src/Headless.Messaging.Testing/RecordedMessage.cs`
- Modify: `src/Headless.Messaging.Testing/MessageObservationStore.cs`
- Modify: `tests/Headless.Messaging.Testing.Tests.Unit/`
- Modify: `headless-framework.slnx`
- Update cross-references in `src`, `tests`, `demo`, and docs that mention `Headless.Messaging.InMemoryQueue`.

## Approach

1. Rename the package, test project, namespaces, project references, and solution entries.
2. Keep `InMemoryQueueTransport` as the queue transport and migrate it to `IQueueTransport`.
3. Add `InMemoryBusTransport` with in-process fan-out semantics.
4. Register both transports from one `AddInMemory(...)` setup path.
5. Update harness base classes so transport test projects can declare bus-only, queue-only, or dual capability.
6. Update `Headless.Messaging.Testing` to wrap both `IBusTransport` and `IQueueTransport` with recording decorators.
7. Add `IntentType` to recorded observations and wait helpers.
8. Preserve clear harness errors for missing in-memory transport/storage.

## Test Suite Design

- Renamed InMemory unit project owns bus/queue in-process transport behavior.
- Core harness tests cover the end-to-end vertical slice.
- `tests/Headless.Messaging.Testing.Tests.Unit/` owns recording and observation behavior.

## Test Scenarios

- `InMemoryBusTransport.SendAsync` to a topic with three subscribers delivers one copy to each.
- `InMemoryQueueTransport.SendAsync` to a queue with two competing workers delivers to exactly one.
- Dual registration of one handler for bus and queue invokes the handler once per path with matching `ConsumeContext.IntentType`.
- `OutboxBus.PublishAsync` -> storage row -> drainer -> `InMemoryBusTransport` -> subscriber -> received row with `IntentType.Bus`.
- `OutboxQueue.EnqueueAsync` follows the symmetric queue path and inserts received row with `IntentType.Queue`.
- Cancellation mid-dispatch leaves the published row in retryable state.
- `MessagingTestHarness` wraps bus and queue transports and records intent.
- `WaitForPublished<T>(IntentType.Bus)` and `WaitForPublished<T>(IntentType.Queue)` distinguish identical payloads.
- Existing testing-package tenant propagation and observation tests still pass after migration.
- `rg "Headless.Messaging.InMemoryQueue|InMemoryQueue" --type csproj --type cs src tests demo` returns only intentional `InMemoryQueueTransport` class references.

## Verification

- `tests/Headless.Messaging.InMemory.Tests.Unit/` passes.
- `tests/Headless.Messaging.Testing.Tests.Unit/` passes.
- Core harness tests that use InMemory pass.
- `dotnet build headless-framework.slnx --no-incremental` is green.
- `headless-framework.slnx` references the renamed package/test project and no old package path.

## Handoff Criteria

This plan is complete when InMemory proves the bus/queue model end-to-end and the testing package can be reused by external provider migrations without relying on the deleted unified `ITransport`.
