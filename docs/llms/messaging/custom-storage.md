---
domain: Messaging
packages:
---

# Writing a storage provider

> Part of [Messaging](../messaging.md). The contract a third-party messaging storage implements: `IDataStorage`, its optional capabilities, and the rules each member owes the core.

## Writing a Storage Provider

A storage package holds the outbox, inbox, retry state, and monitoring reads for one database. It implements `IDataStorage` and `IMonitoringApi` (namespace `Headless.Messaging`), declares a storage capability, and registers through `MessagingSetupBuilder.RegisterExtension(...)` with an `IMessagesOptionsExtension`, as a transport does ([Registration shape](custom-transport.md#registration-shape)).

**A custom storage does not yet have the parity a custom transport has.** The interfaces on this page are public, but the transactional inbox and enlistment in a caller's unit of work run through internal members, so a third-party storage cannot declare the `Transactional` inbox guarantee or let `unit.Outbox` join a unit. Declare `InboxGuarantee.Durable`, and set `MessagingOptions.MinimumInboxGuarantee = InboxGuarantee.Durable` in hosts that use it. The built-in storages still compile against `Headless.Messaging` internals.

Declare the capability from the extension's `AddServices`:

```csharp
services.AddMessagingProviderCapabilities(
    MessagingProviderCapabilities.Storage(
        "MyStore",
        [MessageLane.Bus, MessageLane.Queue],
        supportsDelayedScheduling: true,
        InboxGuarantee.Durable
    )
);
```

**Verify:** `describe` lists the store under `messaging.outboxes` as `primary` with your storage type name. A host whose `MinimumInboxGuarantee` is above the declared guarantee and that has durable consumers fails startup with a `MessagingConfigurationException`, so a passing start with durable consumers proves the declaration.

## Clock and due times

- **Decide due time and leases on the store's own clock.** Core never passes an application-clock due time. A state write takes a `RetryDelay?`:
  - `RetryDelay.Exactly(delay)` makes the row due `delay` after the store's clock.
  - `RetryDelay.AtLeast(delay)` does the same but keeps a later due time already on the row.
  - `null` clears the due time.
- A store whose clock lives in process resolves the instant with `RetryDelay.ResolveDueAt(now, currentDue)`. A database store computes it in the update statement.
- A store with an in-process clock can apply the shared lease rules from `RowLeaseRules` instead of writing them again.
- **Read the clock after you lock the row.** A decision that waited for another transaction's lock must use the clock as it stood when the lock was granted.

## Leases and attempts

- **`ReservePublishAttemptAsync` and `ReserveReceiveAttemptAsync` renew the lease they reserve under.** Renew the caller's held generation to the store's clock plus `RetryPolicy.DispatchTimeout`, refuse when the generation is no longer the row's or has lapsed, then reserve the attempt under that generation and copy the renewed lease back onto the `MediumMessage`. A refused reservation changes neither the counter nor the lease. Do both in one atomic step.
- **`RenewPublishLeaseAsync` and `RenewReceiveLeaseAsync` renew a running attempt's lease.** Their default implementations throw `NotSupportedException`. Implement each as a renewal of the held generation by the duration the caller passes, and return `false` when the caller no longer holds the lease. A renewal judges the lease alone: the state write that ends the attempt carries the terminal and inbox guards. Without them, a row reached after its claim ran out is released for the next pickup instead of renewed.
- **Inbox fence.** Releasing an inbox lease and deferring an inbox retry require the complete stored attempt fence: generation, incarnation, and attempt ID. A missing or mismatched fence leaves the row unchanged. An inline inbox reservation verifies the full active fence before it advances the counter and keeps the attempt ID within the lease. A fresh lease or a claimed due retry gets a new attempt ID.
- **Admission field limits.** Validate inbox admission like the built-in storages:
  - Contract names, consumer identities, and message IDs must be nonblank and at most 200 characters.
  - Contract versions are at most 100 characters.
  - Generations are not negative.
  - Tenant IDs are nonblank and at most 200 characters. A blank tenant header normalizes to no tenant.

## State writes

- **`MessageContentWrite`.** `ChangePublishStateAsync`, `ChangePublishRetryStateAsync`, `ChangeReceiveStateAsync`, and `ChangeReceiveRetryStateAsync` take a `MessageContentWrite`.
  - `Preserve`, the default on the two non-retry methods, leaves the stored envelope byte-identical and omits the content from the update.
  - `Refresh` re-serializes `MediumMessage.Origin`, writes it, and refreshes `MediumMessage.Content`.
  - Keep `persisted content == Serialize(Origin)` in both directions. A store that keeps the envelope in more than one form updates every form on `Refresh`.
- **Persist `PreEntryFailures`.** `ChangeReceiveStateAsync` and `ChangeReceiveRetryStateAsync` write `MediumMessage.PreEntryFailures` with the rest of the state. Every received row handed to dispatch (inbox admission and the retry claim) carries it back. A store that drops it lets a fault that never clears reschedule forever.
- **Failure text and fingerprint.** Store `MediumMessage.ExceptionInfo` and `MediumMessage.FailureFingerprint` on every state write, in the same guarded statement as the state. `StoreReceivedExceptionMessageAsync` and a row a retry claim cannot deserialize store `MessagingFailureFingerprints.Poison` when the message carries no fingerprint. Never derive a fingerprint from stored text.
- **`ChangePublishStateToDelayedAsync`** takes the `MediumMessage` instances the caller holds, not bare IDs. The scheduler queue it flushes on shutdown holds rows this node claimed and unleased copies of rows another node may have claimed since.
  - Reset a leased copy's row to `Delayed` by releasing the generation the message holds.
  - Reset an unleased copy's row only while no attempt holds it and it is still at the generation the copy read.
  - Require the lane to match, and leave terminal and unknown rows unchanged.
  - The delayed processor uses the same method to hand back claimed rows the scheduler queue refused.

## Inbox and ordering checks

- **`IsInboxMessageAdmittedAsync(tenantId, messageId, messageName, consumerIdentity, lane)`** reports whether an inbox row exists for that exact identity, any contract version included. Core asks it before it stores a poison row for a delivery that failed before admission, and commits the delivery as a duplicate when it returns `true`. Compare every component byte-exactly: a database whose `=` ignores trailing spaces must confirm the matched columns ordinally. A store without an inbox returns `false`.
- **`DeferReceivedDispatchAsync(message, delay, cancellationToken)`** puts off retry pickup of a received row whose first dispatch a host still holds in memory. A [sequential consumer](ordering.md#sequential-consumers) and a [rate-limited consumer](ordering.md#consumer-rate-limit) use it for messages that wait. In one statement, set the due time to the store's clock plus `delay` and stamp the change marker, only while the row is not terminal, no live lease holds it, and its `Retries` and `InlineAttempts` equal the message's. Return whether a row changed. Take no lease and change nothing else. The default throws `NotSupportedException`, and Core then leaves waiting rows to retry pickup, which can run one out of its key's order.
- **`GetSequenceBlockAsync(message, cancellationToken)`** is the check a sequential consumer makes before it runs a keyed received row.
  - Persist `MediumMessage.SequenceKey` with the admitted row, and give each received row an admission number that increases in admission order.
  - Clear the key in every write that makes the row terminal: success, terminal failure, and a poison or exception row.
  - Return a `SequenceBlock` when an earlier row of the same consumer identity and key is not terminal. Set `DueAt` to that earliest blocking row's due time. Return `null` when the row has no key or nothing earlier blocks it.
  - Compare keys and identities ordinally, and answer from one indexed read.
  - The default returns `null`, so a store without it orders each key per host only.

## Operator and monitoring reads

- **`RequeuePublishedMessagesAsync(ids)`** backs the dashboard's published requeue and returns the IDs it requeued.
  - Requeue only a row that is `Failed` with no `NextRetryAt`.
  - Reset `Retries` and `InlineAttempts` to zero, clear `ExpiresAt`, revoke any lease, and set `NextRetryAt` from the store's clock.
  - Decide each row on its own under its row lock. Leave every other row, including a missing one, unchanged.
- **`IMonitoringApi.GetBacklogAsync`** has no default implementation. Return the pending outbox and inbox counts and the oldest pending age, measured on the store's clock ([Outbox and inbox backlog](observability.md#outbox-and-inbox-backlog) defines pending). `MediumMessage.StatusName` carries the stored status on monitoring reads.
- **`IMonitoringApi.GetCorrelatedMessagesAsync`** has a default that throws `NotSupportedException` naming the provider, so a store written before it still compiles. The dashboard trace lists such a store as unavailable. Implement it to return, for one correlation id, the oldest `Limit + 1` published rows and the oldest `Limit + 1` received rows (every generation, not only the current one), each ordered by time added, then storage id. Keep these rules:
  - Match the id exactly and by ordinal comparison. `C-42`, `c-42` and `c-42 ` are three ids, so compare in code even where the database's `=` ignores case or trailing spaces.
  - Store the correlation and causation ids in columns of their own at publish and receive, and write null for a value over 200 characters instead of cutting it short. A receive never fails for it.
  - Copy both values onto a row created from another row, such as a replay's child generation.
  - Return an empty received list when the store holds no received table.
  - Fill `MessageTraceRow`: a published row is identified by `MessageId`, a received generation by `StorageId`; `CausationId` is the stored causation id; `DueAt` is the publish time of a delayed or queued published row, else the next retry time.
  - `MessageListQuery.CorrelationId` follows the same match rule.
- **`GetScheduledDeliveryOperationsApi()` and `GetPublishedOperationsApi()`** throw `NotSupportedException` naming the provider by default. Implement them to support the audited operator actions in [Operator APIs](operations.md#operator-apis).

## Optional capabilities

Core detects each capability with a type check on the registered `IDataStorage`. A store without one keeps the fallback in the last column.

| Interface | Members | Contract | Without it |
| --- | --- | --- | --- |
| `IDelayedMessageClaimStorage` | `ClaimDelayedMessagesAsync` | Atomically claim and transition a bounded batch of due delayed rows, then return only after commit. Grant each winner a new lease generation expiring at `max(store now, ExpiresAt) + DispatchTimeout` from the same clock snapshot that tests eligibility, and skip terminal rows | `ScheduleMessagesOfDelayedAsync` |
| `IGracefulLeaseReleaseStorage` | `ReleasePublishedLeaseAsync`, `ReleaseReceivedLeaseAsync`, and their batch forms | Release only the exact `(StorageId, lane, Generation)` lease the store returned, refuse terminal rows, change nothing else | Rows recover when the lease expires after a graceful shutdown |
| `IMessageRevocationStorage` | `RevokeAsync(storageId)` | Atomically delete a scheduled row before its first attempt reservation, fenced by storage version, terminal status, and retry state. Return `Revoked`, `NotFound`, or `AttemptReserved` | `IMessageRevoker` throws `NotSupportedException` naming the provider |

The circuit-deferral capability is internal. A circuit-open received retry therefore keeps its claim until normal lease expiry on a third-party store, instead of being deferred to the circuit's next probe.
