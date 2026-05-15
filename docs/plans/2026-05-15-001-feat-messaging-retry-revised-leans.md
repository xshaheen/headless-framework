---
status: proposal
date: 2026-05-15
supersedes: docs/plans/2026-05-14-001-feat-messaging-retry-policy-contract-plan.md
closes-issues: [255, 256, 257, 258, 260]
pr: 254
---

# Messaging Retry — Revised Leans (#255-260)

## Why a second proposal

The first plan got the **state lifecycle** right and the **state transitions** wrong. Each of the five follow-up issues traces to a transition the original plan never modelled:

| Issue | Transition the plan missed |
|---|---|
| #255 | `Failed -> Failed` (OnExhausted may fire more than once on at-least-once delivery) |
| #256 | `Scheduled/NULL -> picked up too early` (pickup query has two branches; persisted retry can run before inline retry budget would have) |
| #257 | `Continue -> Continue (n times)` (single linear `MaxAttempts` ignores that inline-vs-persisted budgets compose multiplicatively, like NServiceBus) |
| #258 | `Scheduled -> Failed (because host shut down)` (a host-stopping OCE writes a final state to a row that should remain owned by the storage) |
| #260 | `Continue -> Exhausted (with async work in OnExhausted)` (sync `Action<FailedInfo>` traps every async user into footgun territory) |

Peer-framework research (MassTransit, NServiceBus, Wolverine, Hangfire, Sidekiq — CAP explicitly excluded) confirmed the patterns below. None of them rely on a flat `MaxAttempts` like the original contract did.

## Summary of fixes

| # | Issue | Lean | Surface impact |
|---|---|---|---|
| X1 | #255 | Document at-least-once + idempotent contract; defensive prior-status guard | XML doc + small guard in `_SetFailedState` |
| X2 | #256 | Write `NextRetryAt = now + InitialDispatchGrace` on initial store; collapse pickup to one branch | `RetryPolicyOptions`, 3 providers, 1 SQL clause, drop unused index |
| X3 | #257 | Drop `MaxAttempts`; split into `MaxInlineRetries` × `MaxPersistedRetries` | `RetryPolicyOptions` + `RetryHelper` + 6 sites that log `MaxAttempts` + 3 storage `@Retries` parameters |
| X4 | #258 | Host-shutdown OCE returns without writing state | `IMessageSender._UpdateMessageForRetry`, `ISubscribeExecutor._UpdateMessageForRetry` |
| X5 | #260 | `OnExhausted` becomes `Func<FailedInfo, CT, Task>?` | `RetryPolicyOptions`, 3 invocation sites, docs, tests |

All five are breaking. The framework is pre-1.0 — one combined break is cheaper than five staggered breaks.

## X1 — OnExhausted at-least-once contract (#255)

### Root cause

`OnExhausted` is invoked synchronously from `_SetFailedState`. If a broker redelivers the same message (network blip, ack lost), `_SetFailedState` runs again on a row that is already `Failed`. The callback fires twice. The plan called this single-fire without saying who guarantees it.

### Lean

Make at-least-once the documented contract and add a cheap defensive guard so accidental redeliveries don't double-fire when storage can already prove the row is terminal.

### Surgery

`src/Headless.Messaging.Core/Configuration/RetryPolicyOptions.cs`

- Rewrite the `OnExhausted` XML doc to state:
  - Delivery is **at-least-once**; the callback may fire more than once for the same message under redelivery, partial failures, or crash-recover.
  - Handler MUST be idempotent (use `Message.Id` for dedupe).
  - The framework makes best-effort to avoid double-fire when storage proves the row is already terminal, but does not guarantee single-fire under all broker semantics.

`src/Headless.Messaging.Core/Persistence/IDataStorage.cs`

- Change `ChangeReceiveStateAsync` and `ChangePublishStateAsync` return type from `ValueTask` / `Task` to `ValueTask<bool>` — `true` when a row was updated, `false` when the conditional WHERE matched zero rows (the row was already in a terminal state).

`src/Headless.Messaging.PostgreSql/PostgreSqlDataStorage.cs`
`src/Headless.Messaging.SqlServer/SqlServerDataStorage.cs`

- In `ChangePublishStateAsync` and `ChangeReceiveStateAsync`, append `AND "StatusName" NOT IN ('Failed','Succeeded')` to the UPDATE's WHERE clause. Return the `ExecuteNonQueryAsync` row-affected count compared to zero.
- The conditional UPDATE is one round-trip; no SELECT-then-UPDATE. (Gemini review note: avoids doubling DB calls per failure.)

`src/Headless.Messaging.InMemoryStorage/InMemoryDataStorage.cs`

- Same shape: only mutate the in-memory row when its current status is not terminal; return `true`/`false`.

`src/Headless.Messaging.Core/Internal/ISubscribeExecutor.cs`
`src/Headless.Messaging.Core/Internal/IMessageSender.cs`

- In `_SetFailedState`, await the state-change call and capture the bool. When the update returned `false` (row already terminal), skip the `OnExhausted` invocation entirely and log at debug: "Skipping OnExhausted: message {StorageId} already terminal". The cancellation-token wiring for X4 happens in the same block; ordering: X4 shutdown check first, then state-update, then conditional `OnExhausted`.

Note: `MediumMessage` does **not** carry a current `StatusName` field today (verified). The conditional-UPDATE-and-check-rows-affected pattern means we do not need to add one. The guard is enforced by storage, not by an in-memory snapshot.

### Tests

- `should_skip_on_exhausted_when_status_already_failed_when_redelivered` (subscribe + publish).
- Pin existing `should_invoke_on_exhausted_when_attempts_exhausted` — the at-least-once XML change is a contract change, not a behavior change.

## X2 — InitialDispatchGrace + single-branch pickup (#256)

### Root cause

When a message is first stored, `NextRetryAt` is `NULL`. The pickup query covers two branches:

```sql
WHERE (NextRetryAt IS NOT NULL AND NextRetryAt <= now())
   OR (StatusName = 'Scheduled' AND NextRetryAt IS NULL)
```

The second branch was meant to catch crash-recovery. It also catches *every* freshly-stored message. The persisted-retry processor can pick a message up before the inline retry loop has even tried it once, and certainly before the inline retry budget would have run.

This is the design flaw that turned #257 into a multi-issue cascade.

### Lean

Make `NextRetryAt` always non-null. Set it on initial store to `now + InitialDispatchGrace` (default 30s — long enough that the normal dispatch + inline retry path finishes first; short enough that genuine crash-before-dispatch recovers in under a minute). The pickup query collapses to one branch and the special-case index goes away.

This matches Wolverine and Hangfire, which model time-to-pickup as a scheduled column rather than a status-based query.

### Surgery

`src/Headless.Messaging.Core/Configuration/RetryPolicyOptions.cs`

- Add `InitialDispatchGrace { get; set; } = TimeSpan.FromSeconds(30);` with validator `GreaterThan(TimeSpan.Zero)`.
- Add `LessThanOrEqualTo(TimeSpan.FromHours(1))` to bound it; storage scans cannot be deferred indefinitely without a reason.

`src/Headless.Messaging.PostgreSql/PostgreSqlDataStorage.cs`
`src/Headless.Messaging.SqlServer/SqlServerDataStorage.cs`
`src/Headless.Messaging.InMemoryStorage/InMemoryDataStorage.cs`

- In `StoreMessageAsync` and `StoreReceivedMessageAsync`: compute `var nextRetryAt = timeProvider.GetUtcNow().UtcDateTime + messagingOptions.Value.RetryPolicy.InitialDispatchGrace;`.
- Set `MediumMessage.NextRetryAt = nextRetryAt`.
- Bind `@NextRetryAt` parameter to that value (replacing `DBNull.Value`).

`StoreReceivedExceptionMessageAsync` (the bypass path for poisoned-on-arrival messages) keeps `NextRetryAt = NULL`. Those rows are inserted as `Failed` and must never be picked up. The pickup query's `WHERE NextRetryAt IS NOT NULL` clause is exactly what filters them out.

- In the retry-pickup query, collapse the WHERE to:
  - PostgreSQL: `WHERE "Retries"<@Retries AND "Version"=@Version AND "NextRetryAt" IS NOT NULL AND "NextRetryAt" <= now() LIMIT {n} FOR UPDATE SKIP LOCKED`
  - SqlServer: `WHERE Retries < @Retries AND Version = @Version AND NextRetryAt IS NOT NULL AND NextRetryAt <= GETUTCDATE()`

`src/Headless.Messaging.PostgreSql/PostgreSqlStorageInitializer.cs`
`src/Headless.Messaging.SqlServer/SqlServerStorageInitializer.cs`

- Drop the `idx_*_scheduled_null` / `IX_*_ScheduledNull` indexes — the new query never reads that branch. Keep the `idx_*_next_retry` / `IX_*_NextRetry` filtered index (it is now the sole pickup path).

`src/Headless.Messaging.InMemoryStorage/InMemoryDataStorage.cs`

- `GetMessagesOfNeedRetry` (and equivalent) — filter to `m.NextRetryAt is not null && m.NextRetryAt <= now`.

### Tests

- Pin: storing a message gives `NextRetryAt = now + grace` in all three providers.
- Pin: a fresh message is NOT picked up before `grace` elapses.
- Pin: a message stored just over `grace` ago is picked up by the retry processor.
- Pin: `StoreReceivedExceptionMessageAsync` rows stay invisible to pickup.

## X3 — Multiplicative inline × persisted budget (#257)

### Root cause

The original contract has a single linear ceiling `MaxAttempts` (default 50). Two consequences:

1. With `MaxInlineRetries = 2` and `MaxAttempts = 50`, *every persisted pickup* runs the inline loop again. The retry processor picks up a row, the executor inline-retries up to 2 more times, fails again, persists. The total observed "attempts" is `pickups × (1 + MaxInlineRetries)` — which `MaxAttempts` underestimates.

2. The budget cannot be reasoned about per axis. "I want bursts of 3 immediate retries with 16 persisted re-tries" is not expressible.

NServiceBus's formula — `Total = (ImmediateRetries+1) × (DelayedRetries+1)` — fixes both. Inline bursts on each persisted pickup are the design, not a bug.

### Lean

Split the budget:

- `MaxInlineRetries` (existing, default 2) — extra inline attempts on each pickup, *not counting* the first execution.
- `MaxPersistedRetries` (new, default 15) — persisted-retry pickups, *not counting* the first execution. Default 15 gives `(2+1) × (15+1) = 48` total attempts — close to the prior 50 default, more transparent.

Drop `MaxAttempts` entirely. The `MediumMessage.Retries` counter tracks **persisted-retry pickups only**, not inline iterations. The storage `Retries < @Retries` filter becomes `Retries < @MaxPersistedRetries`.

### Surgery

`src/Headless.Messaging.Core/Configuration/RetryPolicyOptions.cs`

- Remove `MaxAttempts`. Remove the corresponding validator rule and the "MaxInlineRetries must be less than MaxAttempts" cross-rule.
- Add `MaxPersistedRetries { get; set; } = 15;` with validator `GreaterThanOrEqualTo(0)`.
- Update `CopyTo` to mirror.
- Update XML for `MaxInlineRetries` and `MaxPersistedRetries` to document the multiplicative model explicitly:
  > Total observable attempts = (MaxInlineRetries + 1) × (MaxPersistedRetries + 1).
  > Inline retries burst on each persisted pickup. To disable retry entirely set both to 0.

`src/Headless.Messaging.Core/Retry/RetryHelper.cs`

Three changes, each isolated:

1. `RecordAttemptAndComputeDecision`: stop mutating `message.Retries`. The method becomes the authority for terminal-vs-continue, using `MaxPersistedRetries` against `message.Retries`:
   ```csharp
   // Returns Stop / Exhausted / Continue(clamped delay). No mutation.
   // Exhausted iff message.Retries >= policy.MaxPersistedRetries (no pickups left).
   // Otherwise Continue.
   ```
2. `ResolveNextState` stays a **pure** helper (returns the transition descriptor). No mutation inside it. Naming and contract preserved. (Gemini review note: keep this function side-effect-free.)
3. The call site (`_SetFailedState` in both `IMessageSender` and `ISubscribeExecutor`) increments `message.Retries` after `ResolveNextState` returns, when:
   - `state.IsInlineRetryInFlight == false`
   - AND `decision.Outcome == RetryDecision.Kind.Continue`

   This is exactly the "persisting for a later pickup" transition. The increment is one explicit line at the call site, not hidden inside the helper.

`IsInlineRetryInFlight` continues to use `MaxInlineRetries`; no change.

Pickup-query predicate: `Retries < @MaxPersistedRetries`. Walk-through: with `MaxPersistedRetries = 15`, the very first failure persists with `Retries = 1` (incremented at the call site). The retry processor will pick the row up while `Retries < 15`, i.e. for `Retries` values 1..14 — that is 14 more pickups. Combined with the 1 already-incremented persist, the total persisted pickups is 15. (Gemini review flagged a possible off-by-one; the walk-through confirms `<` is correct, NOT `<=`.)

`src/Headless.Messaging.Core/Configuration/MessagingOptions.cs`

- If any defaults composition referenced `MaxAttempts`, replace.

`src/Headless.Messaging.Core/Internal/LoggerExtensions.cs`

- Update three log message templates that include `MaxAttempts` in their text to use `MaxPersistedRetries` (or remove the parameter — the count is more informative without it). At the call sites in `IMessageSender.cs:164`, `ISubscribeExecutor.cs:241`, `IConsumerRegister.cs:675`, replace `_retryPolicy.MaxAttempts` with `_retryPolicy.MaxPersistedRetries`.

`src/Headless.Messaging.PostgreSql/PostgreSqlDataStorage.cs:268`, `:527`
`src/Headless.Messaging.SqlServer/SqlServerDataStorage.cs:263`, `:527`
`src/Headless.Messaging.InMemoryStorage/InMemoryDataStorage.cs:222`, `:312`, `:334`

- Replace `messagingOptions.Value.RetryPolicy.MaxAttempts` with `messagingOptions.Value.RetryPolicy.MaxPersistedRetries`. The semantics shift cleanly because `MediumMessage.Retries` now counts persisted pickups.

`src/Headless.Messaging.Core/Internal/IConsumerRegister.cs:675` (bypass-path log)

- Same `MaxAttempts -> MaxPersistedRetries` swap.

### Tests

- `tests/Headless.Messaging.Core.Tests.Unit/Retry/RetryHelperTests.cs`:
  - All references to `MaxAttempts` -> `MaxPersistedRetries` in setup.
  - New: `should_not_increment_retries_when_inline_budget_available`.
  - New: `should_increment_retries_only_when_transitioning_to_persisted_retry`.
  - New: `should_emit_exhausted_when_retries_equals_max_persisted_retries_and_inline_exhausted`.
  - Update `should_return_continue_when_attempts_remain` and similar to use the multiplicative model.
- `tests/Headless.Messaging.Core.Tests.Unit/Configuration/MessagingOptionsValidationTests.cs`:
  - Drop the "MaxInlineRetries must be less than MaxAttempts" assertion.
  - Add `should_reject_negative_max_persisted_retries`.

## X4 — Host-shutdown OCE retains prior state (#258)

### Root cause

When the host is stopping, an in-flight message can throw an `OperationCanceledException` whose token equals the shutdown token. The current `_SetFailedState` path treats this as a normal failure, calls `RetryHelper.RecordAttemptAndComputeDecision` (which returns `Stop` for cancellation), and then writes `StatusName = Failed`. The row is now terminal — but the message was simply not given a chance to complete. On restart, no retry happens, and the message is lost.

### Lean

On host-shutdown OCE, return from `_SetFailedState` early *without* calling `ChangePublishStateAsync` / `ChangeReceiveStateAsync`. The row keeps its prior `Scheduled` state and its prior `NextRetryAt`. On restart, the persisted retry processor picks it up normally.

### Surgery

`src/Headless.Messaging.Core/Internal/IMessageSender.cs`
`src/Headless.Messaging.Core/Internal/ISubscribeExecutor.cs`

- In `_SetFailedState`, before computing decision, check `RetryHelper.IsCancellation(ex, _shutdownToken)` (sender) / the passed-in `cancellationToken` (subscribe). If true, log "Skipping state update on host shutdown for {StorageId}" at debug, return `RetryDecision.Stop`. Do NOT call `ChangePublishStateAsync` / `ChangeReceiveStateAsync`. Do NOT report to circuit breaker (shutdown is not a transient failure signal).

This is a one-add per file. The existing cancellation handling inside `_UpdateMessageForRetry` (which produces `RetryDecision.Stop` for cancellation) still applies for non-shutdown cancellations (handler timeouts, etc.) — those continue to write `Failed` so the message is not abandoned.

### Tests

- `should_not_write_state_when_host_shutdown_token_cancelled` (sender + subscribe).
- `should_write_state_when_handler_timeout_cancellation_independent_of_shutdown_token` (sender + subscribe) — pin that the carve-out only applies to the shutdown token.
- Test-harness wiring: many existing tests rely on `IHostApplicationLifetime` stubs that never trigger their `ApplicationStopping` token. New X4 tests MUST actually cancel that token (or use a manual `CancellationTokenSource` injected as the shutdown source). Audit `IMessageSender` test construction during implementation to confirm the token wired in is the one we cancel. (Gemini review note.)

## X5 — Async OnExhausted (#260)

### Root cause

`OnExhausted` is `Action<FailedInfo>`. The dispatch scope is disposed as soon as the callback returns. Users who need async work (writing a dead-letter row, calling an alerter HTTP endpoint, publishing a notification) have three bad choices:

1. `.GetAwaiter().GetResult()` — deadlocks on sync contexts.
2. `Task.Run(...)` — abandons the scope; `FailedInfo.ServiceProvider` is disposed before the work runs.
3. `async void` lambda — exceptions become process-killers and the framework cannot log them.

All three are footguns. Peer frameworks (MassTransit `IFaultConsumer`, Wolverine `IDeadLetterQueue`, NServiceBus `IHandleMessages`) ship async signatures.

### Lean

Change `OnExhausted` to `Func<FailedInfo, CancellationToken, Task>?`. Await it at every call site, inside the existing try/catch. Pass through the cancellation token already in scope at the call site (or `CancellationToken.None` for the bypass path since the bypass scope is fresh).

### Surgery

`src/Headless.Messaging.Core/Configuration/RetryPolicyOptions.cs`

- Change the property type.
- Update XML to remove the "synchronous" / "fire-and-forget" warning and replace with: "Awaited inside the live dispatch scope. The supplied cancellation token reflects host shutdown — handle it to fail fast on stop."
- Update `CopyTo`.

`src/Headless.Messaging.Core/Internal/IMessageSender.cs`
`src/Headless.Messaging.Core/Internal/ISubscribeExecutor.cs`

- Change `_InvokeOnExhausted` signature from `void` to `async Task`, accept a `CancellationToken`, replace `?.Invoke(...)` with:
  ```csharp
  var callback = _retryPolicy.OnExhausted;
  if (callback is not null)
  {
      try
      {
          await callback(new FailedInfo {...}, cancellationToken).ConfigureAwait(false);
      }
      catch (Exception callbackEx)
      {
          _logger.ExecutedThresholdCallbackFailed(callbackEx, callbackEx.Message);
      }
  }
  ```
- The try/catch is the same one the synchronous code has today — preserved so that a user-thrown exception inside `OnExhausted` never crashes the dispatch loop. (Gemini review note: explicit so the safety isn't lost during the sync-to-async port.)
- Await `_InvokeOnExhausted(...)` at the call site.

`src/Headless.Messaging.Core/Internal/IConsumerRegister.cs:659` (bypass path)

- Same shape, using `CancellationToken.None` (the bypass path runs from the transport-receive loop and uses its own token; it does not have a logical "dispatch cancellation" — `CancellationToken.None` is correct).

### Tests

- `should_await_on_exhausted_callback_before_returning` (sender + subscribe + bypass).
- Pin: any prior `Action<FailedInfo>` test mocks become `Func<FailedInfo, CancellationToken, Task>`. NSubstitute mock setup migrates one-for-one.

### PR body update

Add a row to the breaking-changes table:

| Symbol | Before | After | Migration |
|---|---|---|---|
| `RetryPolicyOptions.OnExhausted` | `Action<FailedInfo>?` | `Func<FailedInfo, CancellationToken, Task>?` | Convert handler to `async (info, ct) => { await ...; }`. Synchronous handlers stay valid by returning `Task.CompletedTask`. |
| `RetryPolicyOptions.MaxAttempts` | `int`, default 50 | (removed) | Replace with `MaxPersistedRetries` (new, default 15). Total budget = `(MaxInlineRetries+1) × (MaxPersistedRetries+1)`. |
| `RetryPolicyOptions.MaxPersistedRetries` | (new) | `int`, default 15 | See above. Increment of `MediumMessage.Retries` now happens on persisted transitions only. |
| `RetryPolicyOptions.InitialDispatchGrace` | (new) | `TimeSpan`, default 30s | Bounds how long the persisted retry processor waits after initial store before picking up a never-dispatched row. Lower for faster crash-recovery; higher for less storage scan pressure during burst publishes. |

## Cross-cutting test coverage

- `tests/Headless.Messaging.Core.Tests.Unit/Retry/RetryHelperTests.cs`: ~5 added tests, ~10 updated.
- `tests/Headless.Messaging.Core.Tests.Unit/Configuration/MessagingOptionsValidationTests.cs`: ~3 added tests.
- `tests/Headless.Messaging.Core.Tests.Unit/Processor/MessageNeedToRetryProcessorTests.cs`: pin single-branch pickup; pin `InitialDispatchGrace` honored.
- `tests/Headless.Messaging.PostgreSqlStorage.Tests`, `tests/Headless.Messaging.SqlServerStorage.Tests`: pin that `StoreMessageAsync` / `StoreReceivedMessageAsync` set `NextRetryAt = added + grace`, and that pickup honors the single-branch filter.

## Build / verify gates

- `dotnet build src/Headless.Messaging.Core/Headless.Messaging.Core.csproj --no-incremental -v:q -nologo /clp:ErrorsOnly`
- `dotnet build src/Headless.Messaging.InMemoryStorage/Headless.Messaging.InMemoryStorage.csproj --no-incremental -v:q -nologo /clp:ErrorsOnly`
- `dotnet build src/Headless.Messaging.PostgreSql/Headless.Messaging.PostgreSql.csproj --no-incremental -v:q -nologo /clp:ErrorsOnly`
- `dotnet build src/Headless.Messaging.SqlServer/Headless.Messaging.SqlServer.csproj --no-incremental -v:q -nologo /clp:ErrorsOnly`
- `dotnet build tests/Headless.Messaging.Core.Tests.Unit/... --no-incremental -v:q -nologo /clp:ErrorsOnly`
- `dotnet-test` MTP run on `RetryHelperTests`, `MessagingOptionsValidationTests`, `MessageNeedToRetryProcessorTests`, `MessageSenderTests`, `SubscribeExecutorRetryTests`, `SubscribeExecutorCancellationTests`.

## Execution order

Build the changes in this sequence; each step keeps the tree green:

1. **X5 first** (`OnExhausted` async). One signature change, propagated. Independent of the other four.
2. **X3** (split budget). Largest surface. After X5 because the log-call updates in X3 should land while OnExhausted call sites are already touched.
3. **X2** (`InitialDispatchGrace` + single-branch pickup). Independent of X3 once `RetryPolicyOptions` has stabilized.
4. **X4** (shutdown OCE non-write). One-line guard per executor. Last because it depends on shutdown-token plumbing that X3 may have already touched.
5. **X1** (at-least-once contract + prior-status guard). Documentation-dominant. Lands last so the guard sits on top of the new transition model.

## Risks and what I'm watching

- **X3 + tests**: The Retries counter contract change ripples through every retry test. Need to read each test in full to confirm assertion semantics still match — silent regressions here are easy. Bias toward over-testing the `(2+1) × (15+1) = 48` boundary explicitly.
- **X2 + clock skew**: Storing `NextRetryAt = now + 30s` assumes monotonic UTC across writer and reader. The framework already enforces UTC at the parameter boundary (`ToUtcParameterValue`). Multi-node deployments with skewed clocks could see early-pickup; that is acceptable risk for a 30s default grace.
- **X4 + circuit breaker**: Need to confirm that *not* reporting host-shutdown failures to the circuit breaker is correct. The breaker should not open because the host stopped. Audit `_circuitBreakerStateManager.ReportFailureAsync` paths during implementation.
- **X1 prior-status guard**: The in-memory snapshot guard is best-effort. Documenting at-least-once is the real fix; the guard is a defense-in-depth bonus.

## Out of scope (intentional)

- A separate "dead-letter store" abstraction (peer pattern). Would be a v2 conversation.
- Per-message override of retry policy. The current global policy is sufficient for v1.
- Telemetry for retry budget consumption — tracked separately in #230.
