---
title: refactor: separate messaging storage and logical identity
type: refactor
status: active
date: 2026-03-23
---

# refactor: separate messaging storage and logical identity

## Overview

Decouple persisted outbox row identity from logical message identity across the messaging stack. Today the storage-backed published-message path overloads the message header `MessageId` as the persisted row key, which forces storage providers to treat a logical string identifier as a numeric database primary key. Since this is a greenfield project, the plan should take the breaking cleanup now: generate numeric storage IDs for persisted rows and keep logical `MessageId` as an arbitrary string contract.

## Problem Statement / Motivation

The current published-message contract is internally inconsistent:

- `PublishOptions.MessageId` is a `string`, but its XML docs tell users to supply a numeric string when using storage-backed outbox providers.
- `Message.GetId()` returns `string`, but durable providers parse that string into `BIGINT` when storing published rows.
- `MediumMessage.DbId` is a stringly typed storage identifier even though monitoring and delete APIs already use `long`.
- Scheduler, state-transition, monitoring, and dashboard flows all operate on that same numeric row key, so the constraint is broader than the initial insert path.
- The received-message path already models the cleaner split: numeric row `Id` plus separate string `MessageId`.

This leaves the public publish API advertising one contract while durable storage enforces another. Greenfield is the right time to remove the mismatch instead of documenting around it.

## Proposed Solution

Adopt two explicit identities everywhere the outbox participates:

- **Storage ID**: generated numeric row identity used for persistence, retries, delayed scheduling, monitoring, requeue, and delete operations.
- **Message ID**: arbitrary string from messaging headers used for tracing, transport integration, correlation, and business-level identity.

Concretely:

- Generate published-row storage IDs via the existing `ILongIdGenerator` instead of reusing `Message.GetId()`.
- Add a dedicated `MessageId` column to published tables in SQL Server and PostgreSQL.
- Stop parsing `Message.GetId()` as a `long` in storage-backed providers.
- Replace ambiguous `DbId` naming in medium-message and monitoring/dashboard surfaces with explicit storage-identity terminology.
- Keep dashboard/operator actions keyed by numeric storage ID, but expose logical `MessageId` alongside it in details and list views.
- Update in-memory storage to mirror the durable-provider contract instead of perpetuating the legacy coupling.
- Remove docs that tell users to provide numeric-string message IDs.

## Technical Considerations

### Core Identity Model

- Replace `MediumMessage.DbId` with an explicit numeric storage identifier such as `StorageId`.
- Preserve logical message identity in `Message.Headers[Headers.MessageId]` and `Message.GetId()`.
- Avoid adding a second arbitrary string identity property to core runtime types unless a concrete call site cannot reliably read the logical ID from `Origin` or a dedicated query projection.

### Storage Schema

- Published tables should match the received-table pattern: numeric primary key plus `MessageId` string column.
- The published `MessageId` column should be populated from the message headers at insert time.
- Do not make published `MessageId` unique in this refactor unless a separate product decision wants publish-side deduplication. The current system does not treat published rows as unique by logical message ID, and changing that behavior would be a semantics change, not just an identity cleanup.

### API Cleanup

- Keep row-oriented operations numeric: `GetPublishedMessageAsync(long storageId)`, `DeletePublishedMessageAsync(long storageId)`, scheduler transitions, and dashboard actions.
- Rename ambiguous DTO/property names that currently imply "message ID" but actually carry storage row identity.
- Surface both storage and logical identity in operator-facing payloads where ambiguity exists today.

### Migration / Compatibility

- This plan assumes breaking changes are acceptable.
- Existing persisted published rows can be backfilled using their current numeric `Id` value as `MessageId`, because the current implementation guarantees those values are equal.
- No compatibility layer should preserve the "numeric string required" contract in the main API after this refactor lands.

## System-Wide Impact

### Interaction Graph

`IOutboxPublisher.PublishAsync(...)` calls `MessagePublishRequestFactory.Create(...)`, which creates the logical header `MessageId`. `IDataStorage.StoreMessageAsync(...)` should then allocate a numeric storage ID and persist both identities separately. `Dispatcher.EnqueueToPublish(...)` and `Dispatcher.EnqueueToScheduler(...)` should operate on storage ID for state changes and scheduling. `IMonitoringApi` and dashboard endpoints should continue using storage ID for row lookups, requeue, and delete, while operator responses show the logical `MessageId` explicitly.

### Error & Failure Propagation

- Storage insert failures should still log and trace the logical message ID.
- Retry, schedule, and delete failures should key off storage ID for row operations.
- Logs that currently print `DbId` need review so operators can distinguish storage identity from logical message identity.

### State Lifecycle Risks

- Delayed and scheduled published rows must retain stable storage IDs across state transitions.
- Migration/backfill must not strand existing rows without a `MessageId`.
- In-memory storage must not silently diverge from SQL Server/PostgreSQL behavior after the redesign.

### API Surface Parity

The following surfaces need the same identity split or a naming cleanup:

- `src/Headless.Messaging.Abstractions/PublishOptions.cs`
- `src/Headless.Messaging.Core/Messages/Message.cs`
- `src/Headless.Messaging.Core/Messages/MediumMessage.cs`
- `src/Headless.Messaging.Core/Persistence/IDataStorage.cs`
- `src/Headless.Messaging.Core/Monitoring/IMonitoringApi.cs`
- `src/Headless.Messaging.Core/Monitoring/MessageView.cs`
- `src/Headless.Messaging.Dashboard/Endpoints/MessagingDashboardEndpoints.cs`
- `src/Headless.Messaging.SqlServer/*`
- `src/Headless.Messaging.PostgreSql/*`
- `src/Headless.Messaging.InMemoryStorage/*`

### Integration Test Scenarios

- Publish through SQL Server outbox with caller-supplied alphanumeric `MessageId`; row stores successfully and later transitions by storage ID still work.
- Publish through PostgreSQL outbox with caller-supplied alphanumeric `MessageId`; monitoring returns both storage ID and logical message ID correctly.
- Schedule, retry, requeue, and delete published rows by storage ID without requiring numeric header IDs.
- In-memory storage accepts arbitrary logical message IDs while keeping numeric row identity for monitoring/delete parity.
- Received-message deduplication behavior remains unchanged.

## SpecFlow Analysis

### User Flow Overview

1. **Outbox publish with generated logical ID**
   Factory generates `MessageId`; storage allocates its own numeric row ID; dispatcher uses row ID; transport still emits logical `MessageId`.
2. **Outbox publish with caller-supplied logical ID**
   Caller can pass any non-empty string; storage no longer rejects non-numeric values.
3. **Delayed publish**
   Scheduler transitions and delayed-queue processing operate only on storage ID, not logical `MessageId`.
4. **Monitoring and dashboard operations**
   Operators inspect rows by storage ID but can also see the logical `MessageId` that went over the transport.
5. **Migration path for existing rows**
   Existing published rows gain a populated `MessageId` value without rewriting transport payloads.

### Flow Permutations Matrix

| Flow | Logical Message ID Source | Storage ID Source | Must Support |
| --- | --- | --- | --- |
| Immediate outbox publish | Generated by publish factory | Generated by storage provider | Arbitrary string message IDs |
| Immediate outbox publish with override | Caller-supplied `PublishOptions.MessageId` | Generated by storage provider | Non-numeric IDs |
| Delayed outbox publish | Generated or overridden logical ID | Generated by storage provider | State transitions by storage ID |
| Monitoring detail/list | Stored `MessageId` column or deserialized headers | Persisted numeric row ID | Both identities visible |
| Delete/requeue | N/A | Persisted numeric row ID | Stable row-oriented operator actions |

### Missing Elements & Gaps

- **Category:** Semantics
  **Gap Description:** Published-row logical `MessageId` uniqueness is undefined today.
  **Impact:** Adding a unique index could accidentally introduce publish deduplication behavior.
  **Current Ambiguity:** Whether repeated publish attempts with the same logical `MessageId` should succeed or conflict.

- **Category:** API design
  **Gap Description:** `DbId` and `MessageView.Id` are ambiguous names for storage row identity.
  **Impact:** The redesign is easy to undermine if naming stays vague.
  **Current Ambiguity:** Which public types should be renamed now versus tolerated temporarily.

- **Category:** Migration
  **Gap Description:** Existing published rows need a deterministic `MessageId` backfill path.
  **Impact:** Dashboard/detail flows can break for pre-refactor rows if the new column is null.
  **Current Ambiguity:** Whether backfill is handled in initializer scripts, a separate migration, or both.

### Critical Questions Requiring Clarification

1. **Critical:** Should published `MessageId` become unique?
   Why it matters: uniqueness changes runtime semantics from identity cleanup to deduplication.
   Planning assumption: no; keep published rows non-unique by logical `MessageId` in this refactor.
   Example: two publishes with the same caller-supplied `MessageId` should still create two published rows unless a future dedupe feature is intentionally added.

2. **Important:** Should ambiguous public names be broken now?
   Why it matters: leaving `DbId` in place preserves the core confusion this refactor is trying to remove.
   Planning assumption: yes; rename storage-identity surfaces now because the repo is greenfield.
   Example: prefer `StorageId` over `DbId`, and consider exposing `MessageId` separately in dashboard DTOs.

3. **Important:** How should existing rows be backfilled?
   Why it matters: operator views and details need consistent data after schema change.
   Planning assumption: backfill published `MessageId` from the current numeric `Id` value because the existing contract makes them identical.

## Success Metrics

- Storage-backed outbox publish accepts arbitrary non-numeric `PublishOptions.MessageId` values.
- No durable provider parses `Message.GetId()` into a numeric row key for published storage.
- Published rows persist numeric storage ID and logical string `MessageId` separately.
- Scheduler, retry, requeue, monitoring, and delete paths keep working by storage ID.
- Operator-facing APIs and views expose unambiguous storage-vs-message identity.
- Documentation no longer instructs users to provide numeric-string message IDs.

## Dependencies & Risks

### Dependencies

- SQL Server and PostgreSQL storage initializer changes.
- In-memory provider parity updates.
- Dashboard and monitoring payload updates.
- Broad unit and integration test refresh across messaging storage providers.

### Risks

- Hidden assumptions in tests and helper code that treat `DbId` as both storage ID and logical message ID.
- Schema drift between SQL Server and PostgreSQL if the published-table changes are not kept symmetric.
- Over-scoping into publish-side deduplication if uniqueness is decided implicitly during implementation.

### Mitigations

- Keep the plan explicitly limited to identity separation, not deduplication semantics.
- Apply the same storage shape to SQL Server, PostgreSQL, and in-memory providers in the same change set.
- Add focused integration tests for publish, monitoring, delayed scheduling, and delete/requeue paths before deleting legacy assumptions.

## Implementation Units

- [ ] **Unit 1: Replace ambiguous identity types in core contracts**

  **Goal:** Make storage identity explicit in runtime and monitoring contracts.

  **Files:**
  - `src/Headless.Messaging.Core/Messages/MediumMessage.cs`
  - `src/Headless.Messaging.Core/Persistence/IDataStorage.cs`
  - `src/Headless.Messaging.Core/Monitoring/IMonitoringApi.cs`
  - `src/Headless.Messaging.Core/Monitoring/MessageView.cs`
  - `src/Headless.Messaging.Dashboard/Endpoints/MessagingDashboardEndpoints.cs`
  - `src/Headless.Messaging.Abstractions/PublishOptions.cs`

  **Approach:**
  - Rename storage-identity members away from `DbId`.
  - Keep logical message identity string-based in message headers and publish options.
  - Update dashboard/detail payloads to surface both storage and logical IDs where needed.

- [ ] **Unit 2: Redesign published-row persistence in durable providers**

  **Goal:** Persist published rows with generated numeric storage IDs plus separate logical `MessageId`.

  **Files:**
  - `src/Headless.Messaging.SqlServer/SqlServerStorageInitializer.cs`
  - `src/Headless.Messaging.SqlServer/SqlServerDataStorage.cs`
  - `src/Headless.Messaging.SqlServer/SqlServerMonitoringApi.cs`
  - `src/Headless.Messaging.PostgreSql/PostgreSqlStorageInitializer.cs`
  - `src/Headless.Messaging.PostgreSql/PostgreSqlDataStorage.cs`
  - `src/Headless.Messaging.PostgreSql/PostgreSqlMonitoringApi.cs`

  **Approach:**
  - Add published `MessageId` column.
  - Generate storage IDs with `ILongIdGenerator`.
  - Backfill existing published rows deterministically.
  - Remove `_ParseStorageId(message.GetId())` from published insert paths.

- [ ] **Unit 3: Align in-memory storage, docs, and tests**

  **Goal:** Keep all providers and operator docs consistent with the new identity model.

  **Files:**
  - `src/Headless.Messaging.InMemoryStorage/InMemoryDataStorage.cs`
  - `src/Headless.Messaging.InMemoryStorage/InMemoryMonitoringApi.cs`
  - `src/Headless.Messaging.Core/README.md`
  - `src/Headless.Messaging.Abstractions/README.md`
  - `tests/Headless.Messaging.Core.Tests.Harness/DataStorageTestsBase.cs`
  - `tests/Headless.Messaging.SqlServer.Tests.Integration/*`
  - `tests/Headless.Messaging.PostgreSql.Tests.Integration/*`
  - `tests/Headless.Messaging.InMemoryStorage.Tests.Unit/*`

  **Approach:**
  - Mirror the durable-provider identity split in memory.
  - Replace tests that assert or rely on numeric logical message IDs.
  - Update docs to state that `MessageId` is an arbitrary string and storage identity is internal/numeric.

## Acceptance Criteria

- [ ] `PublishOptions.MessageId` no longer documents a numeric-string restriction.
- [ ] Published SQL Server rows persist `Id` and `MessageId` separately.
- [ ] Published PostgreSQL rows persist `Id` and `MessageId` separately.
- [ ] In-memory storage matches the same identity model.
- [ ] Dashboard and monitoring APIs remain row-oriented and expose logical `MessageId` clearly.
- [ ] Integration coverage proves non-numeric logical message IDs work for published storage.

## Sources & References

- `src/Headless.Messaging.Abstractions/PublishOptions.cs`
- `src/Headless.Messaging.Core/Messages/Message.cs`
- `src/Headless.Messaging.Core/Messages/MediumMessage.cs`
- `src/Headless.Messaging.Core/Persistence/IDataStorage.cs`
- `src/Headless.Messaging.Core/Monitoring/IMonitoringApi.cs`
- `src/Headless.Messaging.Core/Monitoring/MessageView.cs`
- `src/Headless.Messaging.Core/Internal/IMessagePublishRequestFactory.cs`
- `src/Headless.Messaging.Core/Internal/OutboxPublisher.cs`
- `src/Headless.Messaging.Dashboard/Endpoints/MessagingDashboardEndpoints.cs`
- `src/Headless.Messaging.SqlServer/SqlServerStorageInitializer.cs`
- `src/Headless.Messaging.SqlServer/SqlServerDataStorage.cs`
- `src/Headless.Messaging.SqlServer/SqlServerMonitoringApi.cs`
- `src/Headless.Messaging.PostgreSql/PostgreSqlStorageInitializer.cs`
- `src/Headless.Messaging.PostgreSql/PostgreSqlDataStorage.cs`
- `src/Headless.Messaging.PostgreSql/PostgreSqlMonitoringApi.cs`
- `src/Headless.Messaging.InMemoryStorage/InMemoryDataStorage.cs`
- `src/Headless.Messaging.InMemoryStorage/InMemoryMonitoringApi.cs`
- `tests/Headless.Messaging.Core.Tests.Harness/DataStorageTestsBase.cs`
