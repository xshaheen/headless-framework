---
date: 2026-05-21
type: feat
status: completed
depth: deep
origin: docs/plans/2026-05-21-001-feat-messaging-bus-queue-split-plan.md
part_of: messaging-bus-queue-split
sequence: 2
depends_on: docs/plans/2026-05-21-002-feat-messaging-foundation-contracts-plan.md
---

# feat: Messaging Intent Persistence and Drainer

## Summary

Make intent durable. This slice adds `IntentType` to stored published and received rows, expands received-message identity, updates retry/delayed/dashboard projections, and makes the outbox drainer dispatch through `IBusTransport` or `IQueueTransport` based on the stored row.

Parent design map: [2026-05-21-001-feat-messaging-bus-queue-split-plan.md](2026-05-21-001-feat-messaging-bus-queue-split-plan.md).

## Scope

In scope:

- Parent units: U4 and U5.
- Requirements: R7, R8, R9, R10.
- Acceptance examples: AE3, plus the persistence side of AE2.
- Providers: PostgreSQL, SqlServer, and InMemoryStorage.

Out of scope:

- InMemory transport rename/implementation.
- External broker provider migrations.
- OTel/dashboard endpoint work beyond storage projection types needed by monitoring.
- UI rendering for dashboard intent filters.

## Key Decisions

- `MediumMessage.IntentType` is the storage/runtime contract.
- Do not add `IDataStorage.StoreMessageAsync(..., IntentType intent)` as a side channel. The storage boundary should accept a row/envelope object that already carries intent.
- `IntentType` is `SMALLINT NOT NULL`; no DDL default should hide missing runtime values.
- Received identity expands from `(Version, MessageId, Group)` to `(Version, MessageId, Group, IntentType)`.
- Same-process missing transport registration fails during bootstrap. A persisted row that cannot be dispatched in a later/drainer process is marked terminal `Failed` with `NextRetryAt = null`, and the loop continues.
- Existing greenfield schema handling must be explicit: either drop/recreate messaging tables or add idempotent `ALTER TABLE` + backfill logic and test that path.

## Files

- Modify: `src/Headless.Messaging.Core/Internal/IMessageSender.cs`
- Modify: `src/Headless.Messaging.Core/Internal/MessageSender.cs`
- Modify: `src/Headless.Messaging.Core/Messages/MediumMessage.cs`
- Modify: `src/Headless.Messaging.Core/Persistence/IDataStorage.cs`
- Modify: `src/Headless.Messaging.Core/Monitoring/MessageQuery.cs`
- Modify: `src/Headless.Messaging.Core/Monitoring/MessageView.cs`
- Modify: `src/Headless.Messaging.Core/Processor/Dispatcher.cs`
- Modify: `src/Headless.Messaging.Core/Processor/IProcessor.Delayed.cs`
- Modify: `src/Headless.Messaging.Core/Transactions/OutboxTransaction.cs`
- Modify: `src/Headless.Messaging.PostgreSql/PostgreSqlStorageInitializer.cs`
- Modify: `src/Headless.Messaging.PostgreSql/PostgreSqlDataStorage.cs`
- Modify: `src/Headless.Messaging.PostgreSql/PostgreSqlMonitoringApi.cs`
- Modify: `src/Headless.Messaging.SqlServer/SqlServerStorageInitializer.cs`
- Modify: `src/Headless.Messaging.SqlServer/SqlServerDataStorage.cs`
- Modify: `src/Headless.Messaging.SqlServer/SqlServerMonitoringApi.cs`
- Modify: `src/Headless.Messaging.InMemoryStorage/InMemoryDataStorage.cs`
- Modify: `src/Headless.Messaging.InMemoryStorage/InMemoryMonitoringApi.cs`
- Modify: `src/Headless.Messaging.InMemoryStorage/InMemoryStorageInitializer.cs`
- Modify: `tests/Headless.Messaging.Core.Tests.Unit/MessageSenderTests.cs`
- Modify: `tests/Headless.Messaging.Core.Tests.Harness/DataStorageTestsBase.cs`
- Modify: `tests/Headless.Messaging.PostgreSql.Tests.Unit/`
- Modify: `tests/Headless.Messaging.PostgreSql.Tests.Integration/`
- Modify: `tests/Headless.Messaging.SqlServer.Tests.Unit/`
- Modify: `tests/Headless.Messaging.SqlServer.Tests.Integration/`
- Modify: `tests/Headless.Messaging.InMemoryStorage.Tests.Unit/`

## Approach

1. Add `required IntentType IntentType` to `MediumMessage` and thread it through all creation/projection paths.
2. Update `IDataStorage` so persisted message input carries intent on the row/envelope object, not through a separate method argument.
3. Update delayed pickup, retry pickup, state changes, outbox transactions, and monitoring projections to preserve `IntentType`.
4. Add `IntentType SMALLINT NOT NULL` to `Published` and `Received` tables for PostgreSQL and SqlServer; add in-memory model support.
5. Expand received unique identities and update exception/state update predicates to include `IntentType`.
6. Refactor `MessageSender` to dispatch by `MediumMessage.IntentType`.
7. Handle corrupted/unsupported row intent as a per-row terminal failure, not a drainer-wide stop.
8. Tune per-provider indexes against real query predicates; do not force synthetic indexes that do not match paging/order patterns.

## Test Suite Design

- Core drainer unit tests live in `tests/Headless.Messaging.Core.Tests.Unit/MessageSenderTests.cs`.
- Storage behavior goes through `tests/Headless.Messaging.Core.Tests.Harness/DataStorageTestsBase.cs`.
- Real DDL/index coverage belongs in PostgreSQL and SqlServer integration projects.
- InMemoryStorage stays unit-level.

## Test Scenarios

- A `MediumMessage` with `IntentType.Queue` dispatches through `IQueueTransport` only.
- A `MediumMessage` with `IntentType.Bus` dispatches through `IBusTransport` only.
- Invalid stored enum value logs an error and marks only that row terminal `Failed`.
- Valid stored intent with missing matching transport marks only that row terminal `Failed` with `NextRetryAt = null`.
- Delayed queue row with future `ExpiresAt` is not dispatched early.
- Published insert/fetch round-trips `IntentType.Bus` and `IntentType.Queue`.
- Delayed/retry pickup projections populate `MediumMessage.IntentType`; no delayed queue row re-enters as default bus.
- Received identity allows same `(Version, MessageId, Group)` for bus and queue rows.
- `Group = null` received identity works with PostgreSQL `COALESCE(Group, '')` uniqueness.
- Existing local/test schema path is tested for the chosen drop/recreate or alter/backfill strategy.
- `MessageQuery.IntentType` filters count/page queries and monitoring projections include `MessageView.IntentType`.
- PostgreSQL and SqlServer catalog tests assert new indexes exist by name.

## Verification

- Storage unit/integration suites pass for PostgreSQL, SqlServer, and InMemoryStorage.
- Core drainer tests pass.
- Query-plan checks confirm intent-filtered queries use the selected PostgreSQL/SqlServer index shape where supported.
- No `StoreMessageAsync` overload accepts intent separately from the stored row/envelope object.

## Handoff Criteria

This plan is complete when intent is durable in every storage backend, the drainer dispatches by stored intent, and the storage contract can safely support provider and observability slices.
