---
title: "fix: align messaging bootstrap readiness with subscription registration"
type: fix
date: 2026-03-29
status: active
---

# fix: align messaging bootstrap readiness with subscription registration

## Overview

`IBootstrapper.IsStarted` currently flips to `true` as soon as `Bootstrapper` creates `_cts`, even though `ConsumerRegister.StartAsync(...)` has not finished discovering topics, creating clients, and binding subscriptions yet. That violates the `IBootstrapper` contract and creates a real readiness race for in-memory messaging, runtime subscriptions, and any caller that coordinates startup through `BootstrapAsync(...)` or `IsStarted`.

The proposed issue solution fixes the boolean but also makes `MemoryQueue.Send(...)` lenient. After reviewing the code, that is too shallow. The stricter and more correct fix is to make bootstrap readiness awaitable for concurrent callers, require full successful startup before reporting `IsStarted == true`, introduce a consumer-registration readiness boundary for runtime subscriptions, and preserve `InMemoryQueue`'s fail-fast behavior for genuinely unbound topics.

## Problem Statement / Motivation

The bug is not just a misleading property:

- [`IBootstrapper.IsStarted`](/Users/xshaheen/Dev/framework/headless-framework/src/Headless.Messaging.Core/IBootstrapper.cs#L25) promises readiness only after full initialization.
- [`Bootstrapper.IsStarted`](/Users/xshaheen/Dev/framework/headless-framework/src/Headless.Messaging.Core/Internal/IBootstrapper.Default.cs#L21) currently derives readiness from `_cts`, which is set before `_BootstrapCoreAsync()` runs.
- [`BootstrapAsync(...)`](/Users/xshaheen/Dev/framework/headless-framework/src/Headless.Messaging.Core/Internal/IBootstrapper.Default.cs#L23) also returns early for any second caller once `_cts` exists, even if bootstrap is still in progress.
- [`RuntimeSubscriber`](/Users/xshaheen/Dev/framework/headless-framework/src/Headless.Messaging.Core/Internal/RuntimeSubscriber.cs#L32) relies on `bootstrapper.IsStarted` to decide whether runtime registration should restart consumers immediately or be picked up by the initial startup path.
- [`MemoryQueue.Send(...)`](/Users/xshaheen/Dev/framework/headless-framework/src/Headless.Messaging.InMemoryQueue/MemoryQueue.cs#L62) intentionally throws when no topic binding exists, which is useful for catching incorrect test setup and invalid publish targets.

So the core problem is lifecycle correctness. Making `MemoryQueue` lenient would hide one manifestation of the race while keeping bootstrap state incorrect and leaving concurrent `BootstrapAsync(...)` callers with a false sense of readiness.

## Proposed Solution

Implement bootstrap as a single in-flight operation with explicit readiness signals, fail startup when required processors cannot start, and add regression coverage across the messaging surfaces that depend on it.

### 1. Replace implicit `_cts`-based readiness with explicit startup signals

Update [`Bootstrapper`](/Users/xshaheen/Dev/framework/headless-framework/src/Headless.Messaging.Core/Internal/IBootstrapper.Default.cs) so it distinguishes:

- bootstrap in flight
- consumer registration ready
- bootstrap completed

`IsStarted` should return `true` only after `_BootstrapCoreAsync()` completes successfully and the full bootstrap sequence is finished.

Runtime subscription restart behavior should not key off that final `IsStarted` flag alone. It needs a readiness signal that flips as soon as `IConsumerRegister` has completed its initial startup and topic snapshot, because that is the point after which a new runtime registration can be missed unless consumers are restarted.

Required processor startup failures should no longer be treated as "log and continue" for readiness purposes. In this greenfield codebase, correct behavior is preferred over compatibility with misleading partial-start semantics.

### 2. Make concurrent `BootstrapAsync(...)` callers await the same startup work

The first caller should create and own the bootstrap task.
Subsequent callers during startup should await that same task instead of returning early.

Cancellation needs to be defined explicitly:

- the bootstrap owner may cancel the shared startup operation
- later callers may cancel only their own wait
- the shared bootstrap task and any owner-only bootstrap CTS must be cleared on success, failure, stop, or dispose so future callers do not inherit stale state

If startup fails because a required processor cannot start, the shared bootstrap task should complete as failed and `IsStarted` must remain `false`.

This closes the broader readiness gap for:

- hosted startup paths
- manual `bootstrapper.BootstrapAsync(...)` callers
- test harness setup
- any code that races a second bootstrap invocation against the first

### 3. Keep `MemoryQueue` strict for truly missing subscriptions

Do not change [`MemoryQueue.Send(...)`](/Users/xshaheen/Dev/framework/headless-framework/src/Headless.Messaging.InMemoryQueue/MemoryQueue.cs).

Reasoning:

- the current throw is the only fail-fast signal in the in-memory transport when a topic is genuinely unbound
- making it lenient would silently swallow invalid test setup and invalid publish targets
- the transport provider guidance says transports should not hide failures; the race should be fixed at the bootstrap boundary instead

### 4. Verify runtime subscription parity with initial consumer registration

Keep [`RuntimeSubscriber`](/Users/xshaheen/Dev/framework/headless-framework/src/Headless.Messaging.Core/Internal/RuntimeSubscriber.cs) behavior aligned with the corrected readiness state:

- before `IConsumerRegister` reaches its initial ready point: register in the runtime registry and let the initial startup pass pick it up
- after `IConsumerRegister` is ready, even if overall bootstrap has not fully completed: restart consumers immediately so the new runtime registration is not missed
- after bootstrap completion: continue using the same restart path

That preserves parity between static `IConsume<TMessage>` subscriptions and runtime-attached delegates.

### 5. Update docs to match the actual lifecycle contract

Refresh XML docs and package docs in the same change, but limit the scope to lifecycle-semantic updates so the public guidance stays aligned with the corrected behavior without turning this change into a general messaging docs sweep:

- `IBootstrapper.IsStarted` means required messaging startup completed successfully
- manual callers should await `BootstrapAsync(...)` before publishing
- shared bootstrap callers may cancel their own wait without aborting bootstrap unless they own the startup operation
- `InMemoryQueue` remains strict for unbound topics
- any runtime-subscription docs that describe when registrations are picked up should be updated only if they are affected by the new readiness boundary

## Technical Considerations

- Use a shared bootstrap task plus a small set of explicit readiness signals, not only a `_started` bool. A bool fixes the property but not concurrent `BootstrapAsync(...)` reentry or the runtime-subscription window after consumer registration has started.
- If bootstrap faults before reaching the completed state, the implementation should not remain permanently stuck behind a non-null `_cts`, stale bootstrap task, or stale consumer-ready signal.
- Required processors should be explicit in behavior: if one fails to start, bootstrap fails. Optional processors, if they exist later, should be modeled intentionally rather than inheriting a default "catch/log/continue" policy.
- Stop and dispose paths are in scope for this change. The goal is the wider lifecycle correction, not only the narrow startup symptom, so bootstrap bookkeeping should be made consistent across success, failure, stop, and dispose paths without introducing unnecessary public API surface.
- Preserve existing logging semantics unless a new lifecycle transition genuinely needs additional diagnostics.
- Avoid broad public API expansion unless the implementation cannot safely express the behavior behind the existing `IBootstrapper` contract.

## System-Wide Impact

- **Interaction graph**: `AddHeadlessMessaging(...)` registers `Bootstrapper` as hosted service, which starts `IConsumerRegister`, which creates consumer clients, fetches topics, subscribes groups, and starts listening loops. `RuntimeSubscriber.SubscribeAsync(...)` mutates the same selector and conditionally restarts `IConsumerRegister`. Readiness must be correct across that whole chain.
- **Error propagation**: `MemoryQueue.Send(...)` throws for missing subscriptions, `InMemoryQueueTransport.SendAsync(...)` wraps transport failures in `PublisherSentFailedException`, and `DirectPublisher` turns failed `OperateResult` values back into publish exceptions. Required processor startup failures should likewise propagate as bootstrap failure instead of being logged and treated as successful startup.
- **State lifecycle risks**: today `_cts` doubles as both cancellation state and readiness signal, which conflates "startup began" with "startup completed". There is also a narrower readiness boundary inside startup: `IConsumerRegister` can become live before overall bootstrap completion, and runtime subscriptions added after that point can be missed unless the plan handles that intermediate state.
- **API surface parity**: the same readiness contract affects class-based consumers, runtime subscriptions, `MessagingTestHarness.CreateAsync(...)`, and direct manual bootstrap in tests and applications.
- **Integration test scenarios**: unit tests need one blocked-start regression and one post-start runtime-restart regression; integration tests should prove publish succeeds after awaited bootstrap and still fails for a truly unknown topic.

## Acceptance Criteria

- [x] `IBootstrapper.IsStarted` stays `false` until the full required messaging startup succeeds.
- [x] Concurrent `BootstrapAsync(...)` callers await the same in-flight bootstrap and do not observe early completion.
- [x] Runtime subscriptions added before `IConsumerRegister` is initially ready are picked up by the initial startup path.
- [x] Runtime subscriptions added after `IConsumerRegister` is initially ready are not missed, even if overall bootstrap has not fully completed yet.
- [x] Shared bootstrap callers can cancel their own wait without aborting another caller's startup unless they own the bootstrap operation.
- [x] If a required processor fails to start, bootstrap fails and `IsStarted` remains `false`.
- [x] `MemoryQueue.Send(...)` still throws for genuinely unbound topics after startup.
- [x] Manual bootstrap paths and hosted/test harness paths behave consistently.
- [x] XML/package docs reflect the corrected lifecycle semantics.
- [x] XML and package README updates ship in the same change as the lifecycle fix.
- [x] Doc edits remain limited to lifecycle semantics changed by this work, not a broader messaging documentation refresh.

## Implementation Units

- [x] **Bootstrap lifecycle**
  - Update [`src/Headless.Messaging.Core/Internal/IBootstrapper.Default.cs`](/Users/xshaheen/Dev/framework/headless-framework/src/Headless.Messaging.Core/Internal/IBootstrapper.Default.cs)
  - Introduce a shared bootstrap task plus explicit owner/caller cancellation rules
  - Ensure repeated callers await readiness instead of returning early
  - Replace blanket "log and continue" startup success semantics for required processors with fail-fast bootstrap behavior
  - Clear bootstrap bookkeeping on success, failure, stop, or dispose

- [x] **Consumer-register readiness**
  - Define the exact ready point after `IConsumerRegister` finishes its initial topic snapshot and live clients are in place
  - Expose that boundary to `RuntimeSubscriber` through an internal signal or equivalent collaboration point
  - Ensure runtime registrations crossing that boundary are either captured by initial startup or force a restart immediately

- [x] **Regression tests**
  - Add or extend tests under [`tests/Headless.Messaging.Core.Tests.Unit`](/Users/xshaheen/Dev/framework/headless-framework/tests/Headless.Messaging.Core.Tests.Unit)
  - Add a regression proving `IsStarted == false` while startup is blocked before subscription registration completes
  - Add a regression proving a second `BootstrapAsync(...)` call waits for the first
  - Add a regression proving a second caller can cancel its wait without aborting shared bootstrap
  - Add a regression proving bootstrap fails when a required processor fails to start
  - Add coverage for runtime subscription behavior around startup completion

- [x] **Transport guardrails**
  - Keep [`tests/Headless.Messaging.InMemoryQueue.Tests.Unit/MemoryQueueTests.cs`](/Users/xshaheen/Dev/framework/headless-framework/tests/Headless.Messaging.InMemoryQueue.Tests.Unit/MemoryQueueTests.cs) strict-topic behavior intact
  - Add or keep a test demonstrating unknown-topic publish still fails after startup

- [x] **Docs**
  - Update [`src/Headless.Messaging.Core/IBootstrapper.cs`](/Users/xshaheen/Dev/framework/headless-framework/src/Headless.Messaging.Core/IBootstrapper.cs)
  - Update relevant README sections in [`src/Headless.Messaging.Core/README.md`](/Users/xshaheen/Dev/framework/headless-framework/src/Headless.Messaging.Core/README.md) and [`src/Headless.Messaging.InMemoryQueue/README.md`](/Users/xshaheen/Dev/framework/headless-framework/src/Headless.Messaging.InMemoryQueue/README.md)
  - Ship those doc changes in the same PR so callers do not read stale lifecycle guidance after the behavior changes
  - Keep the edits narrowly scoped to bootstrap/readiness/runtime-subscription lifecycle semantics affected by this implementation

## Alternative Approaches Considered

### Keep the current bootstrap flow and only add `_started`

Rejected. It fixes the property but not the fact that a second `BootstrapAsync(...)` caller can return before the first one finishes.

### Use only overall bootstrap completion as the runtime-subscription boundary

Rejected. `IConsumerRegister` can reach a live topic snapshot before the rest of bootstrap finishes, so using only final bootstrap completion still leaves a window where a runtime registration can be missed.

### Keep swallow-and-continue startup semantics for required processors

Rejected. In a greenfield project, `IsStarted` should be trustworthy. Treating required processor startup failures as logged degradation would preserve ambiguous semantics instead of correcting them.

### Make `MemoryQueue.Send(...)` lenient

Rejected. That masks invalid configuration and test setup, diverges from the current in-memory transport contract, and leaves bootstrap readiness broken for runtime subscriptions and concurrent bootstrap callers.

### Add a brand-new public readiness API

Deferred unless implementation work proves the current contract cannot safely model shared startup. The existing `BootstrapAsync(...)` contract should be enough if it becomes awaitable and idempotent in the correct way.

## Success Metrics

- The startup race described in #204 is covered by regression tests and no longer reproducible.
- `IsStarted` is trustworthy: it becomes `true` only after required messaging startup succeeds.
- In-memory publish failures only occur for real unknown-topic cases, not because readiness was reported early.
- Runtime subscription behavior is consistent before consumer-register readiness, during the intermediate startup window, and after overall bootstrap completion.
- Docs no longer describe stronger guarantees than the implementation actually provides.

## Dependencies & Risks

- Bootstrapper synchronization changes are concurrency-sensitive; tests must control startup timing explicitly instead of relying on sleeps.
- The plan now depends on identifying the correct consumer-register ready point. If that boundary is chosen too early, runtime registrations can still be lost; if chosen too late, the change regresses to the original race.
- Changing required processor startup failures from logged degradation to bootstrap failure is an intentional behavior change. That is acceptable for this greenfield project, but the implementation should make the required/optional distinction explicit if optional processors are introduced later.
- README changes must stay aligned with the actual transport behavior to avoid another docs/runtime mismatch.
- The wider solution includes stop and dispose consistency. That broadens the change surface, so implementation should still prefer the smallest internal mechanism that makes lifecycle behavior coherent end to end.
- If doc edits expand beyond lifecycle semantics, the PR will accumulate review noise and increase the risk of unrelated doc drift.

## Sources & References

- Related issue: [#204](https://github.com/xshaheen/headless-framework/issues/204)
- Internal reference: [`src/Headless.Messaging.Core/Internal/IBootstrapper.Default.cs`](/Users/xshaheen/Dev/framework/headless-framework/src/Headless.Messaging.Core/Internal/IBootstrapper.Default.cs)
- Internal reference: [`src/Headless.Messaging.Core/Internal/RuntimeSubscriber.cs`](/Users/xshaheen/Dev/framework/headless-framework/src/Headless.Messaging.Core/Internal/RuntimeSubscriber.cs)
- Internal reference: [`src/Headless.Messaging.InMemoryQueue/MemoryQueue.cs`](/Users/xshaheen/Dev/framework/headless-framework/src/Headless.Messaging.InMemoryQueue/MemoryQueue.cs)
- Internal reference: [`src/Headless.Messaging.Testing/MessagingTestHarness.cs`](/Users/xshaheen/Dev/framework/headless-framework/src/Headless.Messaging.Testing/MessagingTestHarness.cs)
- Institutional learning: [`docs/solutions/concurrency/startup-pause-gating-and-half-open-recovery.md`](/Users/xshaheen/Dev/framework/headless-framework/docs/solutions/concurrency/startup-pause-gating-and-half-open-recovery.md)
- Institutional learning: [`docs/solutions/guides/messaging-transport-provider-guide.md`](/Users/xshaheen/Dev/framework/headless-framework/docs/solutions/guides/messaging-transport-provider-guide.md)
