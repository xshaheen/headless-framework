---
title: Messaging stale-resume fence - Plan
type: fix
date: 2026-09-13
artifact_contract: x-unified-plan/v1
artifact_readiness: implementation-ready
product_contract_source: x-plan-bootstrap
execution: code
---

# Messaging stale-resume fence - Plan

## Goal Capsule

- **Objective:** An operator who forces a consumer group's circuit open, or a re-trip, restart, or group removal that pauses it, leaves the transport paused until the next valid transition. No earlier resume can undo it.
- **Means:** A monotonic circuit epoch issued by the state manager and applied by each consumer group handle under one serialized gate (KTD1).
- **Authority:** Issue [850](https://github.com/xshaheen/headless-framework/issues/850), this contract, and repository conventions.
- **Execution:** Implement U1 through U5 in dependency order. Stop for a demonstrated contradiction in the circuit state machine or the transport pause contract; naming and helper placement remain implementation decisions.
- **Tail ownership:** The calling delivery workflow owns review, commits, PR, and CI monitoring.

---

## Product Contract

### Summary

Give every pause and resume intent a circuit epoch, serialize their transport side effects per consumer group, and apply only the newest epoch. Move the open timer onto `TimeProvider` so the timer-driven race is testable without real delays.

### Problem Frame

`CircuitBreakerStateManager` launches the HalfOpen resume callback outside the group lock from two sites: the open timer and `GetRetryDecision`. `ConsumerRegister._ResumeGroupAsync` then clears `IsPaused` and resumes every transport client with no check against the circuit. If `ForceOpenAsync`, a HalfOpen re-trip, a restart's `AbortHalfOpenProbeAsync`, or `RemoveGroupAsync` runs between launch and completion, the newer pause finishes first and the stale resume reopens consumption. The circuit then reads Open while the transport delivers, and `TryAcquireHalfOpenProbe` admits every delivery because it only gates HalfOpen.

The only generation counter today is `TimerGeneration`, which fences timer callbacks and is never seen by the resume path. `ResetAsync` and the retry-decision advance do not bump it. Disposal tracks one `ResumeTask` per group and overwrites it on each HalfOpen entry, so a second in-flight resume escapes the disposal wait. The open timer is a raw `System.Threading.Timer`, so tests cannot drive Open to HalfOpen deterministically.

### Requirements

**Fencing**

- R1. A resume launched for one circuit transition must not resume transport clients after a newer transition has paused them, whether the resume is queued or already running.
- R2. Circuit-driven pause and resume side effects for one consumer group are applied one at a time, in a total order, and the last applied intent matches the newest circuit epoch. The startup pause a new client performs in `AddClientAsync` stays outside that order (Scope Boundaries).
- R3. `ResetAsync` resumes a Closed group; the fence keys on epoch order, not on a HalfOpen-only rule.
- R4. A resume launched during restart, after the new handle registered its callbacks and before `AbortHalfOpenProbeAsync` reopened the circuit, must not undo the new handle's pre-pause; and `RemoveGroupAsync` must not return while a resume for that group is in flight.
- R5. A late `ReleaseHalfOpenProbe`, whether from an abandoned probe or from a delivery admitted while Closed that finishes after a later HalfOpen entry, must not clear the probe slot or resolve the outcome of a newer epoch.

**Lifecycle**

- R6. Disposal cancels resumes that have not started and waits for every in-flight resume, not only the latest.
- R7. A resume callback failure still reopens the circuit and pauses the group when that resume is current; a stale failing resume reopens nothing.
- R8. A delivery admitted while the circuit is Open is still dispatched; no transport reject is issued. It is logged as an invariant breach only once the group's pause for the current epoch has completed, and at debug level during the pause-latency window.

**Testability**

- R9. The open timer is created through the injected `TimeProvider`, so `FakeTimeProvider` drives the Open to HalfOpen transition.
- R10. Deterministic tests cover stale resumes from the timer path and the retry-decision path, assert transport-client pause state as well as circuit state, and cover reset, the restart window, callback failure, and disposal.

### Key Decisions

- **Keep the stale-resume rule epoch-ordered rather than state-checked.** A state check before an asynchronous resume cannot see a pause that completes after the check. Governs R1, R2, R3.
- **Admit deliveries while Open, with a diagnostic, instead of rejecting them.** `IConsumerClient.RejectAsync` only "optionally returns the message to the queue", so a reject on a dead-lettering transport would lose the message. Governs R8.

### Scope Boundaries

- The pause callback keeps swallowing individual `PauseAsync` failures and the resume callback keeps rethrowing them; the asymmetry is documented in `docs/solutions/concurrency/startup-pause-gating-and-half-open-recovery.md`.
- The open timer stays armed before the pause callback completes. Under R2 the interleaving resolves correctly, and re-ordering would extend every open window by the pause latency.
- `AddClientAsync` keeps issuing the startup pause outside the handle gate. It reads `IsPaused` under the clients lock, and the race with a concurrent resume apply is pre-existing and narrow.
- Public messaging APIs, options, storage schemas, and dashboards do not change.

#### Deferred to Follow-Up Work

- Rejecting deliveries while Open on transports whose reject is a requeue. Needs a per-transport capability the client contract does not expose today.
- The `_PauseGroupAsync` failure path: a failed `PauseAsync` leaves the circuit Open with a live client and no signal beyond a log line. Rate-limiting the Open-admission warning per group belongs with that work.

### Acceptance Examples

- AE1. Covers R1, R2, R9. Given an Open circuit whose timer elapses under `FakeTimeProvider` and a resume parked before it reaches the handle gate, when `ForceOpenAsync` completes and the resume is released, then the circuit is Open, the group handle reports paused, and no transport client received a resume after its pause.
- AE2. Covers R1, R2. Given a resume already inside `client.ResumeAsync` when `ForceOpenAsync` starts, when both complete, then the pause was applied after the resume finished and every client ends paused.
- AE3. Covers R1, R10. Given `GetRetryDecision` advances an overdue Open circuit to HalfOpen, when `ForceOpenAsync` runs before the launched resume reaches the gate, then the resume is skipped as stale.
- AE4. Covers R3. Given an Open circuit with paused clients, when `ResetAsync` runs, then the group resumes and a later `ForceOpenAsync` pauses it again.
- AE5. Covers R4. Given a HalfOpen entry that launches a resume after the new handle's callbacks are registered and before `AbortHalfOpenProbeAsync` bumps the epoch, when the pre-pause is applied through the gate and the stale resume runs, then the new handle stays paused and its clients never receive a resume.
- AE6. Covers R6. Given two HalfOpen entries whose resumes are both blocked, when the manager is disposed, then disposal returns only after both resumes complete, and a third resume queued after cancellation never invokes the callback.
- AE7. Covers R5. Given a probe acquired at epoch N, the circuit reopened to epoch N+1, and a later HalfOpen entry at epoch N+2 with its own probe, when the epoch-N holder releases, then the epoch-N+2 slot and its pending outcome are unchanged.
- AE8. Covers R7. Given a HalfOpen resume that throws while its epoch is current, when the failure is handled, then the circuit reopens and pause is invoked once; given the same failure after `ForceOpenAsync` moved the epoch, then no second trip is recorded.
- AE9. Covers R8. Given a delivery that arrives while the group's pause is still inside `client.PauseAsync`, when it is admitted, then no warning is emitted; given a delivery admitted after that pause completed with the circuit still Open, then one warning names the group.

---

## Planning Contract

### Key Technical Decisions

- KTD1. **Manager-issued circuit epoch plus per-handle serialized apply.** (session-settled: user-directed — chosen over serializing inside the state manager and over a cancellation token per transition: manager-side serialization blocks Open transitions on transport latency and misses a resume closing over a replaced handle; a token needs transport pause/resume to honor cancellation, which they do not, and still needs serialization.) The manager holds one monotonic epoch counter; each intent change takes the next value under the group's `SyncLock`, so an epoch never repeats across group lifetimes. Callbacks receive the epoch. `GroupHandle` owns one async gate and a last-applied epoch; it applies a callback only when its epoch is not older than the last applied one. The property the handle-side gate adds over manager-side serialization is lifecycle: the gate dies and is reborn with the handle, which KTD4 relies on. Governs R1, R2, R3, R4.
- KTD2. **Open timer via `TimeProvider.CreateTimer`.** (session-settled: user-directed — chosen over a test-only timer seam and over real short durations: the manager already receives a `TimeProvider`, `ITimer` keeps the dispose semantics, and existing 20 to 50 ms tests become flake-prone races once epoch fencing is under test.) `TimeProvider` timers do not flow `ExecutionContext`, so logging scopes from the tripping delivery no longer reach the timer callback; accepted. Governs R9, R10.
- KTD3. **Epoch bump sites, and the epoch replaces `TimerGeneration`.** Every transition that changes pause or resume intent takes a new epoch: transition to Open (failure trip, `ForceOpenAsync`, `AbortHalfOpenProbeAsync`, reopen after resume failure), entry to HalfOpen (timer, `GetRetryDecision`), `ResetAsync`, and `RemoveGroupAsync`. Every site that bumps `TimerGeneration` today is one of these, so the timer callback and `_CreateAndAssignOpenTimer` fence on the captured epoch and `TimerGeneration` is removed. Governs R1, R3, R4, R7.
- KTD4. **Handle-side epoch is per handle, not per manager state.** A restart replaces the `GroupHandle` while `GroupCircuitState` survives with `removeCircuitState: false`. The new handle starts with no applied epoch, and the register applies the restart pre-pause through the gate with the epoch the manager reports for the current state. A resume launched before `AbortHalfOpenProbeAsync` bumped the epoch is then older than the pre-pause and is skipped. Governs R4.
- KTD5. **In-flight resumes are a per-group set, and removal waits like disposal.** Replace the single `ResumeTask` with a collection pruned on completion. `DisposeAsync`, `Dispose`, and `RemoveGroupAsync` snapshot and await it after the epoch bump. The wait inside `RemoveGroupAsync` is bounded on the register side by the shutdown budget. Governs R4, R6.
- KTD6. **Every admission returns its epoch, and release is fenced on it.** `TryAcquireHalfOpenProbe` returns the current epoch for every admission, including Closed and Open admissions that hold no slot; `ReleaseHalfOpenProbe` takes that epoch and is a no-op unless it matches and the state is HalfOpen. Three release sites carry it: the transport delivery callback in `ConsumerRegister` for pre-dispatch releases, `ISubscribeExecutor` for releases after dispatch, and `HalfOpenProbeHandle` in the retry processor. The register-to-executor handoff carries the epoch on a non-persisted `MediumMessage` member set before `EnqueueToExecute`; the retry path receives it on `CircuitRetryDecision` from `GetRetryDecision`, which acquires the probe itself. Governs R5.
- KTD7. **Stale callbacks are debug-logged; Open admissions are warning-logged only after the pause completed.** A skipped stale callback is expected under contention. A delivery admitted while Open during the pause-latency window is normal (prefetched and concurrent deliveries keep arriving while `PauseAsync` runs), so the register's delivery callback logs it at debug; once the handle's last completed apply for the current epoch is a pause and the circuit still reports Open, the transport is provably not paused, and the callback logs a warning with the sanitized group name. Governs R8.
- KTD8. **The handle gate is never disposed.** `SemaphoreSlim.Dispose` never completes queued waiters and makes the holder's release throw, so disposing the gate with the handle would strand a queued resume and hang the KTD5 waits. The gate allocates no wait handle, so leaving it undisposed costs nothing; disposal is signalled by the handle's existing disposing flag, read under the clients lock before waiting and again inside the gate. Governs R1, R6.

### High-Level Technical Design

```mermaid
sequenceDiagram
    participant T as Open timer (TimeProvider)
    participant M as CircuitBreakerStateManager
    participant O as Operator ForceOpenAsync
    participant H as GroupHandle gate
    participant C as Transport clients

    T->>M: elapsed (captured epoch still current)
    M->>M: State=HalfOpen, Epoch=N
    M-->>H: resume(N) queued via Task.Run
    O->>M: ForceOpenAsync
    M->>M: State=Open, Epoch=N+1
    M->>H: pause(N+1) awaited
    H->>H: acquire gate, lastApplied=N+1
    H->>C: PauseAsync each
    H-->>M: pause done
    H->>H: resume(N) acquires gate, N < N+1 -> skip
    Note over H,C: Clients stay paused; circuit Open
```

Directional apply rule inside the handle, for review only:

```text
apply(intent, epoch):
  if disposing (read under clients lock): return
  await gate
  try:
    if disposing or epoch < lastApplied: log stale at debug; return
    lastApplied = epoch; lastIntent = intent
    IsPaused = intent is pause
    for client in snapshot: pause or resume
    (resume rethrows aggregated failures; pause swallows per client)
  finally: release gate      // gate is never disposed (KTD8)
```

### Assumptions

- A1. Equal epochs re-apply idempotently. `PauseAsync` and `ResumeAsync` are idempotent by contract (`tests/Headless.Messaging.Core.Tests.Unit/Transport/ConsumerClientPauseResumeTests.cs`), so the restart pre-pause and a same-epoch pause can both run.
- A2. The gate is a per-handle `SemaphoreSlim(1,1)` that is never disposed (KTD8); `KeyedAsyncLock` from `Headless.Threading` is not needed because the key is the handle itself.
- A3. `FakeTimeProvider` runs timer callbacks synchronously inside `Advance` and outside its own lock, so the group lock taken inside the timer callback cannot invert with it. The options validator keeps the open duration above zero, so no timer fires inside `CreateTimer`.

### Sources

- `src/Headless.Messaging.Core/CircuitBreaker/CircuitBreakerStateManager.cs`: `_StartResumeCallback`, `_OnOpenTimerElapsed`, `GetRetryDecision`, `ForceOpenAsync`, `AbortHalfOpenProbeAsync`, `ResetAsync`, `RemoveGroupAsync`, `_CreateAndAssignOpenTimer`, `TryAcquireHalfOpenProbe`, `ReleaseHalfOpenProbe`, `GroupCircuitState`.
- `src/Headless.Messaging.Core/Internal/IConsumerRegister.cs`: `_PauseGroupAsync`, `_ResumeGroupAsync`, `GroupHandle`, the restart pre-pause in `ExecuteAsync`, the delivery callback probe admission and its `probeOutcomeTransferred` handoff.
- `src/Headless.Messaging.Core/Internal/ISubscribeExecutor.cs`: `_ReleaseHalfOpenProbe`, the post-dispatch release site.
- `src/Headless.Messaging.Core/Processor/IProcessor.NeedRetry.cs` and `IProcessor.NeedRetry.CircuitRetry.cs`: `HalfOpenProbeHandle` built from `work.Decision`.
- `docs/solutions/concurrency/startup-pause-gating-and-half-open-recovery.md` and `docs/solutions/concurrency/circuit-breaker-transport-thread-safety-patterns.md`: the existing pause contract and the timer-generation pattern this plan generalizes.
- PR #848 lease fencing (`MessageLeaseIdentity`, `CircuitDeferralOutcome.FenceRejected`): the in-repo idiom of carrying the exact generation with deferred work and rejecting a stale one.

---

## Implementation Units

### U1. Circuit epoch and in-flight resume tracking in the state manager

**Goal:** Issue an epoch per intent change, pass it to callbacks, fence the timer on it, and track every in-flight resume.

**Requirements:** R1, R3, R4, R6, R7 via KTD1, KTD3, KTD5.

**Dependencies:** none.

**Files:**
- `src/Headless.Messaging.Core/CircuitBreaker/CircuitBreakerStateManager.cs`
- `src/Headless.Messaging.Core/CircuitBreaker/ICircuitBreakerStateManager.cs`
- `src/Headless.Messaging.Core/Internal/IConsumerRegister.cs` (callback lambda signature only; gate logic is U3)
- `tests/Headless.Messaging.Core.Tests.Unit/CircuitBreaker/CircuitBreakerStateManagerTests.cs`
- `tests/Headless.Messaging.Core.Tests.Unit/CircuitBreaker/CircuitBreakerIntegrationTests.cs` (callback registration signature)

**Approach:**
1. Add the manager-wide epoch counter and a per-group current epoch on `GroupCircuitState`; take a new value under `SyncLock` at every site KTD3 lists, capturing it alongside the callback. Replace `TimerGeneration` and `TimerCallbackState.Generation` with the captured epoch.
2. Change the callback contract to receive the epoch, and change `_StartResumeCallback` to pass it and to register the task in the per-group in-flight set.
3. `_ReopenAfterResumeFailureAsync` takes the failing resume's epoch and is a no-op unless the state is HalfOpen at that epoch.
4. `DisposeAsync`, `Dispose`, and `RemoveGroupAsync` snapshot the set after the epoch bump and wait on all entries; keep the existing `_disposalCts` cancellation.
5. Add a member on `ICircuitBreakerStateManager` (not the public `ICircuitBreakerMonitor`) that returns whether the group is Open together with its current epoch, for the restart pre-pause in U3.

**Patterns to follow:** the capture-then-validate shape of the current timer callback state; `_CompleteRetryProbeOutcome` for resolving per-epoch signals.

**Test scenarios:**
- Timer path: after `FakeTimeProvider.Advance` past the open duration, the resume callback receives an epoch greater than the pause callback's epoch.
- `GetRetryDecision` on an overdue Open circuit takes a new epoch and launches the resume with it.
- `ResetAsync` on an Open group takes a new epoch and invokes resume with it.
- Two HalfOpen entries with blocked resumes: `DisposeAsync` completes only after both callbacks return (Covers AE6).
- Resume queued after disposal cancellation never invokes the callback (Covers AE6).
- `RemoveGroupAsync` with a blocked resume waits for it and bumps the epoch first (R4).
- Resume throws while its epoch is current: circuit reopens and pause is invoked; resume throws after `ForceOpenAsync` moved the epoch: no second trip is recorded and pause is invoked once (Covers AE8).
- A stale timer callback (epoch moved by `ForceOpenAsync`) does not transition, replacing the `TimerGeneration` assertions.

**Verification:** The state manager unit suite passes; every existing test compiles against the new callback contract.

### U2. Open timer through TimeProvider

**Goal:** Make Open to HalfOpen deterministic under `FakeTimeProvider`.

**Requirements:** R9, R10 via KTD2.

**Dependencies:** U1.

**Files:**
- `src/Headless.Messaging.Core/CircuitBreaker/CircuitBreakerStateManager.cs`
- `tests/Headless.Messaging.Core.Tests.Unit/CircuitBreaker/CircuitBreakerStateManagerTests.cs`
- `tests/Headless.Messaging.Core.Tests.Unit/CircuitBreaker/CircuitBreakerIntegrationTests.cs`

**Approach:**
1. Replace `new Timer(...)` in `_CreateAndAssignOpenTimer` with `timeProvider.CreateTimer` and store the `ITimer`; keep creation outside the lock and the stale-epoch dispose.
2. Keep every dispose site async-aware (`ITimer` is `IAsyncDisposable`).
3. Convert every test whose assertion depends on the Open to HalfOpen timer elapsing to `FakeTimeProvider.Advance`; `TimeProvider.System` may remain only in tests with no assertion on that transition.

**Test scenarios:**
- Advancing exactly the open duration transitions Open to HalfOpen; advancing one tick less does not.
- A stale timer (epoch moved by `ForceOpenAsync`) does not transition on advance.
- `dispose_concurrent_with_timer_callback_is_safe` still passes with the fake timer.
- The integration lifecycle test drives trip, open, half-open, close entirely by time advance.

**Verification:** No circuit-breaker test sleeps or polls real time for the Open to HalfOpen transition.

### U3. Serialized epoch-fenced apply in the consumer group handle

**Goal:** Apply pause and resume in epoch order with one gate per handle.

**Requirements:** R1, R2, R3, R4 via KTD1, KTD4, KTD8.

**Dependencies:** U1.

**Files:**
- `src/Headless.Messaging.Core/Internal/IConsumerRegister.cs`
- `tests/Headless.Messaging.Core.Tests.Unit/ConsumerRegisterTests.cs`

**Approach:**
1. Add the gate, last-applied epoch, and last-applied intent to `GroupHandle`; move the bodies of `_PauseGroupAsync` and `_ResumeGroupAsync` behind one apply method that takes the intent and epoch (design sketch above).
2. Keep the `Disposing`/`Disposed` lifecycle guard and the rethrow-on-resume-failure behavior.
3. In `ExecuteAsync`, apply the restart pre-pause through the gate with the epoch the U1 member reports, instead of setting `IsPaused` directly; NSubstitute mocks in the restart test stub that member instead of `IsOpen`.
4. Mark the handle disposing under the gate's contract per KTD8: the gate is never disposed, an apply that acquires it after disposal returns without touching clients, and a waiter already queued completes normally.

**Patterns to follow:** existing reflection-driven handle tests (`should_not_resume_group_when_register_is_quiesced`, `restart_normalizes_halfopen_circuit_and_pauses_new_transport`); the existing `#pragma warning disable CA2213` shape in the same file for an undisposed synchronization primitive.

**Test scenarios:**
- Resume with epoch older than the last applied pause is skipped: `IsPaused` stays true and no client receives `ResumeAsync`.
- Pause with a newer epoch waits for a resume that is inside `client.ResumeAsync`, then pauses every client (Covers AE2).
- Equal-epoch pause after pause is applied and is harmless.
- Restart pre-pause goes through the gate and records the epoch; a resume launched with an older epoch afterwards is skipped (Covers AE5).
- A resume failure in one client still rethrows an aggregate after the gate is released.
- Handle disposed while a pause holds the gate and a resume waits on it: the pause's release succeeds, the queued resume completes as a no-op, and nothing throws.

**Verification:** `ConsumerRegisterTests` pass; a client added during a paused epoch is paused before it subscribes (existing `AddClientAsync` gate unchanged).

### U4. Probe-release fence and Open-admission diagnostic

**Goal:** Fence `ReleaseHalfOpenProbe` to its epoch on all three release sites and surface deliveries admitted while Open without noise.

**Requirements:** R5, R8 via KTD6, KTD7.

**Dependencies:** U1, U3.

**Files:**
- `src/Headless.Messaging.Core/CircuitBreaker/CircuitBreakerStateManager.cs`
- `src/Headless.Messaging.Core/CircuitBreaker/ICircuitBreakerStateManager.cs` (`CircuitRetryDecision` gains the epoch)
- `src/Headless.Messaging.Core/Internal/IConsumerRegister.cs`
- `src/Headless.Messaging.Core/Internal/ISubscribeExecutor.cs`
- `src/Headless.Messaging.Core/Processor/IProcessor.NeedRetry.cs`
- `src/Headless.Messaging.Core/Processor/IProcessor.NeedRetry.CircuitRetry.cs`
- the `MediumMessage` type (non-persisted probe-epoch member)
- `tests/Headless.Messaging.Core.Tests.Unit/CircuitBreaker/CircuitBreakerStateManagerTests.cs`
- `tests/Headless.Messaging.Core.Tests.Unit/CircuitBreaker/SubscribeExecutorCircuitBreakerTests.cs`
- `tests/Headless.Messaging.Core.Tests.Unit/Processor/MessageNeedToRetryProcessorTests.cs`

**Approach:**
1. `TryAcquireHalfOpenProbe` returns the current epoch for every admission; `ReleaseHalfOpenProbe` takes it and releases only when it matches and the state is HalfOpen. `GetRetryDecision` places the acquisition epoch on `CircuitRetryDecision`.
2. The delivery callback stores the epoch on the message before `EnqueueToExecute` when it transfers probe ownership; `ISubscribeExecutor._ReleaseHalfOpenProbe` and `HalfOpenProbeHandle` pass their epoch through.
3. In the register's delivery callback, when the circuit reports Open: log debug while the handle's last completed apply is not a pause for the current epoch, and warning once it is.

**Test scenarios:**
- Release with a stale epoch leaves `ProbeAcquired` true and the pending sibling outcome unresolved (Covers AE7).
- Release with the current epoch clears the slot and resolves the outcome Uncertain, as today.
- A delivery admitted while Closed whose release arrives after a later HalfOpen entry leaves that entry's slot and pending outcome unchanged (R5).
- The executor's post-dispatch release carries the epoch set at admission; the retry handle's release carries the decision's epoch.
- Admission while Open during the pause-latency window emits no warning; admission after the pause completed emits one warning with the group name (Covers AE9).

**Verification:** Existing probe-sharing tests (`retry_and_transport_share_one_halfopen_probe_generation_and_outcome`, `releasing_halfopen_probe_resolves_pending_sibling_as_uncertain`) and the retry-processor release assertions pass with the epoch threaded through.

### U5. Cross-layer race tests and documentation

**Goal:** Prove the issue's acceptance criteria end to end and record the contract.

**Requirements:** R1, R2, R3, R4, R7, R10.

**Dependencies:** U1, U2, U3, U4.

**Files:**
- `tests/Headless.Messaging.Core.Tests.Unit/CircuitBreaker/CircuitBreakerIntegrationTests.cs`
- `docs/solutions/concurrency/startup-pause-gating-and-half-open-recovery.md`
- `docs/llms/messaging.md`
- `src/Headless.Messaging.Core/README.md`
- `CONCEPTS.md`

**Approach:**
1. Add integration tests with a real `CircuitBreakerStateManager`, a real `ConsumerRegister` group handle, `FakeTimeProvider`, and NSubstitute clients. Register the group callbacks from the test as wrappers that await a test-owned `TaskCompletionSource` before invoking the handle's apply through the existing reflection pattern, so a resume can be parked before it reaches the gate (AE1, AE3); park inside `client.ResumeAsync` for AE2.
2. Add the epoch rule to the pause-gating solution doc as a fourth review boundary.
3. State in the messaging doc and Core README that `ForceOpenAsync` and any Open transition leave the group paused regardless of an in-flight recovery.
4. Add a `Circuit epoch` entry under Messaging in `CONCEPTS.md`.

**Test scenarios:**
- Timer-driven stale resume parked before the gate, then `ForceOpenAsync`, leaves the circuit Open and clients paused (Covers AE1).
- Retry-decision-driven stale resume parked before the gate, then `ForceOpenAsync`, is skipped (Covers AE3).
- Reset resumes a Closed group and a later force-open pauses it (Covers AE4).
- Restart window: a resume launched between callback registration and `AbortHalfOpenProbeAsync` leaves the new handle paused (Covers AE5).
- Resume callback failure reopens and pauses; a stale failure does not (Covers AE8).

**Verification:** All acceptance examples map to a passing test that asserts both circuit state and transport-client pause state.

---

## Verification Contract

| Gate | Command | Applies to |
|---|---|---|
| Build | `make build-project PROJECT=src/Headless.Messaging.Core/Headless.Messaging.Core.csproj` | U1 to U5 |
| Unit tests | `make test-project TEST_PROJECT=tests/Headless.Messaging.Core.Tests.Unit/Headless.Messaging.Core.Tests.Unit.csproj` | U1 to U5 |
| Focused classes | `make test-class CLASS='*CircuitBreaker*'`, `make test-class CLASS='*ConsumerRegisterTests'`, `make test-class CLASS='*MessageNeedToRetryProcessorTests'` | iteration |
| Format | `make format-check` | before PR |
| Analyzers | `make quality-analyzers-project PROJECT=src/Headless.Messaging.Core/Headless.Messaging.Core.csproj` | before PR |
| Clean build | `dotnet build -c Release -v:minimal` on the changed projects | before PR, per repo learning that MTP test runs skip analyzers |

Coverage targets from `CLAUDE.md` apply: new branches in the apply gate and epoch bump sites are covered by the scenarios above.

---

## Definition of Done

- Every acceptance example has a deterministic passing test with no real-time sleep for Open to HalfOpen.
- `ForceOpenAsync`, HalfOpen re-trip, and the restart window each leave transport clients paused with a stale resume in flight, proven by client-level assertions; `RemoveGroupAsync` waits for in-flight resumes.
- Disposal waits on all in-flight resumes.
- No public API, option, or schema changed; docs and `CONCEPTS.md` updated per U5.
- Abandoned experiments removed from the diff; build, format, and analyzer gates green.
