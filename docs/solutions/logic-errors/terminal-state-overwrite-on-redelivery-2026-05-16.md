---
title: "Retry pipeline overwrites terminal message state on redelivery"
date: 2026-05-16
category: logic-errors
module: Headless.Messaging
problem_type: logic_error
component: background_job
severity: high
symptoms:
  - "OnExhausted callback fires for messages whose row had already reached Succeeded"
  - "Exhausted (Failed, NextRetryAt=null) rows get re-consumed after broker redelivers a duplicate"
  - "InMemory retry pickup leaks caller-mutated ExceptionInfo/headers back into the storage dictionary when the terminal-row guard rejects the update"
  - "Dispatcher schedule loop enqueues messages that storage's conditional ChangePublishStateAsync had already rejected"
  - "Scope-factory / sender / transport exceptions in _SendMessageDirectlyAsync escape into the channel-reader loop and tear it down"
root_cause: logic_error
resolution_type: code_fix
related_components:
  - database
  - service_class
tags:
  - messaging
  - retry-policy
  - idempotency
  - terminal-state
  - upsert-guard
  - redelivery
  - dispatcher
  - cancellation-token
---

# Retry pipeline overwrites terminal message state on redelivery

## Problem

Broker redelivery and host-shutdown timing could overwrite messaging rows that had already reached a terminal state (`Succeeded`, or `Failed` with `NextRetryAt IS NULL`), causing `OnExhausted` to fire for messages that actually succeeded and consumers to be re-invoked as if their retry budget weren't spent. The fix introduces a single invariant — *storage owns terminal-state truth; callers respect the "no rows affected" signal* — and applies it on both sides of the contract.

## Symptoms

- A previously-succeeded received message could be moved back to `Failed` if a later redelivery's payload failed to deserialize, and `OnExhausted` fired for the succeeded message.
- After host shutdown during the exhausted-state write, a row could remain non-terminal and the consumer would be re-invoked on next pickup as if the retry budget were intact.
- The publish-path scheduler (`Dispatcher.EnqueueToScheduler`) enqueued into its in-memory schedule even when storage rejected the underlying conditional `UPDATE`.
- The "skipping OnExhausted" log line fired on `Stop` paths that wouldn't have invoked the callback anyway, producing noise.
- Transport / scope-factory exceptions in the direct-send path (`Dispatcher._SendMessageDirectlyAsync`) leaked out to the channel-reader loop, while the parallel sibling path was already protected.
- InMemory pickup APIs returned live `MemoryMessage` references — so caller pre-write mutations (e.g., `AddOrUpdateException` on `Origin.Headers`) leaked back into the dictionary even when the subsequent conditional `UPDATE` was rejected by the terminal-row guard. SQL providers got this for free via deserialization; InMemory did not.

## What Didn't Work

The fix arrived through several intermediate states; the ones below were attempted on this branch and discarded. *(session history)*

- **Over-broad first formulation of the terminal-row guard.** The initial guard used `WHERE StatusName NOT IN ('Succeeded','Failed')` on all conditional UPDATEs across PostgreSQL, SqlServer, and InMemory. This silently blocked legitimate writes that move a persisted-retry row from `Failed/NextRetryAt IS NOT NULL` (retry-in-progress) to `Failed/NextRetryAt IS NULL` (terminal). The result: once a message entered persisted retry, every subsequent state write was dropped, `OnExhausted` never fired, and the row looped through pickup indefinitely — the *entire* persisted-retry budget became inoperable. The corrected predicate distinguishes terminal-Failed from retry-in-progress-Failed by also checking `NextRetryAt IS NULL`. *(session history)*
- **InMemory `ChangePublishStateAsync` / `ChangeReceiveStateAsync` as non-atomic check-then-write.** Two concurrent dispatchers could both pass the terminal check and both write, both firing `OnExhausted`. The fix locks on the per-row `MemoryMessage` object rather than the whole dictionary so contention is bounded to the message in question. *(session history)*
- **`isCancellation: false` hardcoded on the publish path.** `IMessageSender` passed `isCancellation: false` to `RetryHelper.ResolveNextState` regardless of the actual exception. Host-shutdown OCEs on the publish path were therefore misclassified as failures. The decision was to document the limitation rather than widen the internal API surface; the inline retry loop was later given `_shutdownToken` (partially closing the gap) without changing the public signature. *(session history)*
- **`_SetFailedState` writing the terminal-failed shape on every non-final inline attempt.** Before the persisted-retry budget was reached, the executor wrote `Failed/NextRetryAt=NULL` on every inline exhaustion, which the corrected guard then refused to overwrite on the next attempt. The fix is upstream: `ResolveNextState` always sets a non-null `NextRetryAt` for any `Continue` decision (inline or persisted), so crash-recovery via the pickup query `WHERE NextRetryAt IS NOT NULL` works regardless. *(session history)*
- **`Retries < @Retries` in the pickup predicate.** All three providers used `<` and therefore excluded the row sitting at exactly `MaxRetries`, so the final exhaustion dispatch never happened. Fixed to `<=`. *(session history)*

## Solution

The fix has two halves that must land together: **storage rejects terminal-row overwrites** and **callers respect that rejection**.

### 1. Storage-layer terminal-row guard

SQL Server ([SqlServerDataStorage.cs:528-538](../../../src/Headless.Messaging.SqlServer/SqlServerDataStorage.cs)) — the `_StoreReceivedMessage` MERGE adds a `WHEN MATCHED AND NOT (...)` predicate so the `UPDATE` branch is skipped for terminal rows; the `INSERT` branch is unaffected:

```csharp
MERGE {_receivedTable} WITH (HOLDLOCK) AS target
USING (SELECT @MessageId AS MessageId, @Group AS [Group]) AS source
ON target.MessageId = source.MessageId AND (target.[Group] = source.[Group] OR (target.[Group] IS NULL AND source.[Group] IS NULL))
WHEN MATCHED AND NOT (target.StatusName IN ('Succeeded','Failed') AND target.NextRetryAt IS NULL) THEN
    UPDATE SET ...
WHEN NOT MATCHED THEN
    INSERT (...);
```

PostgreSQL ([PostgreSqlDataStorage.cs:517-531](../../../src/Headless.Messaging.PostgreSql/PostgreSqlDataStorage.cs)) — the `ON CONFLICT DO UPDATE` is gated by a matching `WHERE NOT (...)` clause. The same narrow shape is applied to every `ChangePublishStateAsync` / `ChangeReceiveStateAsync` `UPDATE` statement ([SqlServerDataStorage.cs:168, 479](../../../src/Headless.Messaging.SqlServer/SqlServerDataStorage.cs), [PostgreSqlDataStorage.cs:177, 471](../../../src/Headless.Messaging.PostgreSql/PostgreSqlDataStorage.cs)).

InMemory ([InMemoryDataStorage.cs:135-213, 253-327](../../../src/Headless.Messaging.InMemoryStorage/InMemoryDataStorage.cs)) — `ChangePublishStateAsync` and `ChangeReceiveStateAsync` short-circuit and return `false` when the existing row is `Succeeded`/`Failed` with `NextRetryAt is null`. `StoreReceivedExceptionMessageAsync` was rewritten as a single locked upsert keyed on (Version, MessageId, Group); the lookup-then-insert/update path is wrapped in `_receivedExceptionUpsertLock` so concurrent redeliveries cannot both decide "not found" and race.

### 2. InMemory snapshot semantics on pickup

`GetPublishedMessagesOfNeedRetryAsync` and `GetReceivedMessagesOfNeedRetryAsync` now project through a private `_ToSnapshot` ([InMemoryDataStorage.cs:402-468](../../../src/Headless.Messaging.InMemoryStorage/InMemoryDataStorage.cs)) that clones `Origin.Headers` (Dictionary copy with `StringComparer.Ordinal`) so caller mutations cannot leak into the live dictionary entry when storage subsequently rejects the conditional `UPDATE`. The clone is **shallow** — `Origin.Value` (the payload `byte[]`) is shared by reference and treated as immutable, matching the source-side comment at `InMemoryDataStorage.cs:456-457`. SQL providers already produce snapshots through deserialization.

### 3. Caller-side respect for `affected == false`

The key behavioral change is treating storage's `false` return as authoritative. Previously the same branch ended with `return decision;` regardless of the storage signal. Now ([IMessageSender.cs:209-219](../../../src/Headless.Messaging.Core/Internal/IMessageSender.cs), [ISubscribeExecutor.cs:251-263](../../../src/Headless.Messaging.Core/Internal/ISubscribeExecutor.cs)):

```csharp
else if (!affected)
{
    if (decision.Outcome == RetryDecision.Kind.Exhausted)
    {
        _logger.SkippingOnExhaustedAlreadyTerminal(message.StorageId);
    }
    return RetryDecision.Stop;
}
```

Returning `RetryDecision.Stop` is load-bearing — when storage rejects the write, the row is already terminal, so the caller must not behave as though the new decision is in force. The log line moves inside the branch and only fires for `Exhausted` decisions (the only outcome that *would* have invoked the callback).

On the publish-success path ([IMessageSender.cs:131-146](../../../src/Headless.Messaging.Core/Internal/IMessageSender.cs)) the same signal is logged via `PublishSucceededButStorageTerminal`; the broker accepted the publish but storage refused the state write — at-least-once delivery is preserved.

`Dispatcher.EnqueueToScheduler` ([Dispatcher.cs:91-115](../../../src/Headless.Messaging.Core/Processor/Dispatcher.cs)) captures the storage return and early-exits the scheduler enqueue:

```csharp
var changed = await _storage.ChangePublishStateAsync(message, statusName, transaction).ConfigureAwait(false);
if (!changed)
{
    return;
}
```

The circuit breaker report in `ISubscribeExecutor._SetFailedState` ([ISubscribeExecutor.cs:270-276](../../../src/Headless.Messaging.Core/Internal/ISubscribeExecutor.cs)) is also gated on `affected`, so a rejected redelivery doesn't count toward the breaker threshold.

### 4. Terminal writes use `CancellationToken.None`

`ISubscribeExecutor._SetFailedState` ([ISubscribeExecutor.cs:231-245](../../../src/Headless.Messaging.Core/Internal/ISubscribeExecutor.cs)) and `IMessageSender._SetFailedState` ([IMessageSender.cs:194-201](../../../src/Headless.Messaging.Core/Internal/IMessageSender.cs)) both pass `CancellationToken.None` to the terminal `ChangeXxxStateAsync` write so the dispatch/shutdown token can't tear it down. The `RetryHelper.IsCancellation` guard above each call short-circuits genuine host-shutdown OCEs before we get here — and it does so by *identity* comparison: `IsCancellation` compares `oce.CancellationToken` against the specific dispatch/shutdown token, so an `OperationCanceledException` from an unrelated timeout CTS does **not** short-circuit and is correctly classified as a failure. Once `_SetFailedState` has classified the failure as non-cancellation and resolved an `Exhausted` decision, the row must land in `Failed/NULL` and `OnExhausted` must fire, even if shutdown races the write.

### 5. Parallel sibling envelope parity

`Dispatcher._SendMessageDirectlyAsync` ([Dispatcher.cs:435-454](../../../src/Headless.Messaging.Core/Processor/Dispatcher.cs)) wraps its body in `try/catch` so transport / scope-factory exceptions log via `TransportSendError` instead of unwinding to `EnqueueToPublish` (whose outer catch only handles OCEs). This matches the long-existing envelope on `_SendMessageAsync` ([Dispatcher.cs:418-433](../../../src/Headless.Messaging.Core/Processor/Dispatcher.cs)).

## Why This Works

Storage is the sole arbiter of terminal state, and a row in `(Succeeded | Failed, NextRetryAt IS NULL)` is settled. Both halves of the fix exist because that invariant can be broken from either direction: the storage-side guard prevents a redelivery from overwriting a settled row's status, and the caller-side `affected == false` handling prevents callers from acting *as if* the write succeeded — which would produce duplicate `OnExhausted` invocations, double-counted circuit-breaker failures, and ghost scheduler entries. The `CancellationToken.None` choice is the third leg: classify cancellation once at the top of `_SetFailedState`, then commit unconditionally; the inline-retry loop can be torn down by shutdown, the terminal write must not be.

## Prevention

- **Storage `bool` returns are contracts, not hints.** Any caller of `ChangePublishStateAsync` / `ChangeReceiveStateAsync` / similar conditional UPDATEs must respect the `false` return. The pattern recurs in parallel sibling paths — `IMessageSender._SetFailedState`, `ISubscribeExecutor._SetFailedState`, `Dispatcher.EnqueueToScheduler` all made the same mistake. When adding a new caller, search the existing call sites and copy the early-exit shape. These methods return `ValueTask<bool>` — capture the result in a local before re-using; awaiting the same `ValueTask` twice is undefined behavior.
- **Terminal-state writes use `CancellationToken.None` after the `IsCancellation` short-circuit** — never the dispatch token. The pattern is: classify cancellation once at the top of the method, then commit. `IMessageSender.cs:161-201` and `ISubscribeExecutor.cs:195-245` are the canonical templates.
- **Terminal-row predicates must include the "scheduled retry" axis.** A guard of the shape `WHERE StatusName NOT IN ('Succeeded','Failed')` is almost always wrong if the same provider also persists in-flight retries as `Failed/NextRetryAt IS NOT NULL`. The correct predicate is `WHERE NOT (StatusName IN ('Succeeded','Failed') AND NextRetryAt IS NULL)`. *(session history)*
- **Provider parity for the terminal-row guard.** InMemory, PostgreSQL, and SQL Server must all reject the same upsert against `(Succeeded | Failed, NextRetryAt IS NULL)`. Add cross-provider tests in `tests/Headless.Messaging.Core.Tests.Harness/DataStorageTestsBase.cs` so a new provider can't ship without honoring the contract.
- **InMemory pickup APIs must return snapshots, not live references.** Callers will mutate the returned `MediumMessage` before the storage write that storage may then reject. `_ToSnapshot` ([InMemoryDataStorage.cs:451-468](../../../src/Headless.Messaging.InMemoryStorage/InMemoryDataStorage.cs)) is the model; clone `Origin.Headers` explicitly. SQL providers get this for free; InMemory must opt in.
- **InMemory mutations are not atomic.** `ChangeXxxStateAsync` and `StoreReceivedExceptionMessageAsync` lock on the per-row `MemoryMessage` object around check+write. A dictionary-wide lock would serialize unrelated messages; a missing lock loses to concurrent redelivery. Use the message object itself as the lock target — this is safe *because* `MemoryMessage` is `internal sealed`; external code cannot acquire a reference, so the well-known "don't lock on a publicly-typed object" rule doesn't apply. Note that `ConcurrentDictionary` operations are individually atomic, but compound *check-then-act* on dictionary values is not — which is what fails here. *(session history)*
- **Parallel sibling code paths share their exception envelopes.** When `_SendMessageAsync` and `_SendMessageDirectlyAsync` both feed the same outer boundary (the channel-reader loop), both must use the same try/catch shape. When adding a new sibling, diff against the existing one before merging.
- **Test the rejection signal end-to-end.** The test cluster lives at:
  - `tests/Headless.Messaging.Core.Tests.Unit/DispatcherTests.cs` — exercises `EnqueueToScheduler` early-exit when storage returns `false`.
  - `tests/Headless.Messaging.Core.Tests.Unit/MessageSenderTests.cs` — exercises publish-path `_SetFailedState` with `affected == false`.
  - `tests/Headless.Messaging.Core.Tests.Unit/SubscribeExecutorRetryTests.cs` — exercises consume-path `_SetFailedState`, OnExhausted-skip log, and circuit-breaker non-report.
  - `tests/Headless.Messaging.Core.Tests.Unit/Retry/RetryHelperTests.cs` — covers `ResolveNextState`, `IsCancellation` token equality, OnExhausted timeout-CTS, and strategy-throw paths.
  - `tests/Headless.Messaging.Core.Tests.Unit/Configuration/MessagingOptionsCopyToTests.cs` — guards deep `CopyTo` of nested retry options so test harnesses don't share mutable state.
- **When mocking storage in tests, match the current arity exactly.** During the `ValueTask<bool>` migration, NSubstitute `Received()` assertions that still used the pre-migration 2-arg shape matched zero calls, the tests passed green, and the actual behavior was unverified. Audit `Received(...)` and `When(...)` call sites whenever the storage interface gains a parameter. *(session history)*

## Related Issues

- [#255 Messaging: OnExhausted may fire more than once when persisted state write fails](https://github.com/xshaheen/headless-framework/issues/255) — Directly resolved by the storage-layer conditional UPDATE + caller-respects-affected=false pattern documented here.
- [#258 Messaging: shutdown during publish parks messages as terminal-failed](https://github.com/xshaheen/headless-framework/issues/258) — Couples to the same `Failed/null` terminal sentinel contract this fix hardens. This doc governs *who is allowed to overwrite* the terminal state; #258 concerns *what writes it in the first place*.
- [#229 Messaging P1.1: retry policy and delayed retry contract](https://github.com/xshaheen/headless-framework/issues/229) — Umbrella contract issue these fixes operationalize.

## Related Docs

- [`docs/solutions/concurrency/startup-pause-gating-and-half-open-recovery.md`](../concurrency/startup-pause-gating-and-half-open-recovery.md) — Same shape of root cause: a caller swallowed a no-op signal from a lower layer. Different layer (transport pause/resume vs storage UPDATE affected-rows). Worth a consolidation pass once both stabilize.
- [`docs/solutions/api/aspnet-core-cancellation-vs-timeout-differentiation-2026-05-07.md`](../api/aspnet-core-cancellation-vs-timeout-differentiation-2026-05-07.md) — Same "use `CancellationToken.None` for the final must-complete write so client cancellation doesn't tear down side-effects" pattern at the ASP.NET Core layer.
- [`docs/solutions/concurrency/circuit-breaker-transport-thread-safety-patterns.md`](../concurrency/circuit-breaker-transport-thread-safety-patterns.md) — Tangential overlap on cancellation handling and exception-envelope parity in dispatch paths.
- [`docs/solutions/guides/messaging-transport-provider-guide.md`](../guides/messaging-transport-provider-guide.md) — Explicitly states core owns retries/outbox/state and transports must not invent their own. This doc reinforces that contract from inside core.
