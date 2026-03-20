---
status: done
priority: p1
issue_id: "016"
tags: ["code-review","messaging","circuit-breaker","dotnet"]
dependencies: []
---

# _ResumeGroupAsync does not restart consumer task loops — consumers permanently dead after first circuit open/close

## Problem Statement

_PauseGroupAsync cancels handle.Cts which causes all ListeningAsync loops to exit via OperationCanceledException. _ResumeGroupAsync creates a new CTS and calls client.ResumeAsync() (which unblocks the ManualResetEventSlim gate or re-subscribes RabbitMQ) but never restarts the Task.Factory.StartNew(LongRunning) tasks that drive ListeningAsync. After the first circuit trip and recovery, the transport-level consumer is un-paused but no task is calling ListeningAsync. Messages from the broker are silently dropped. The consumer group is permanently dead until application restart.

## Findings

- **Location:** src/Headless.Messaging.Core/Internal/IConsumerRegister.cs:_ResumeGroupAsync
- **Location:** src/Headless.Messaging.Core/Internal/IConsumerRegister.cs:_PauseGroupAsync
- **Risk:** Critical — silent message loss after first circuit recovery
- **Discovered by:** strict-dotnet-reviewer, pragmatic-dotnet-reviewer, security-sentinel, performance-oracle, architecture-strategist

## Proposed Solutions

### Option A: Restart LongRunning tasks in _ResumeGroupAsync
- **Pros**: Direct fix, mirrors startup path
- **Cons**: Requires storing topics on GroupHandle
- **Effort**: Medium
- **Risk**: Low

### Option B: Redesign pause to not cancel CTS — rely exclusively on PauseAsync (gate/broker-native)
- **Pros**: Cleaner architecture, no task restart needed
- **Cons**: Larger refactor, must verify all 8 transports
- **Effort**: Large
- **Risk**: Medium


## Recommended Action

Option A for V1: Store topics on GroupHandle, extract _StartGroupAsync helper, call it from both ExecuteAsync and _ResumeGroupAsync with the new CTS.

## Acceptance Criteria

- [ ] After circuit open + close cycle, ListeningAsync resumes for all 8 transports
- [ ] No message loss observed between pause and resume
- [ ] GroupHandle.Topics is populated at startup and available at resume time
- [ ] Integration test or regression test covering open/HalfOpen/close cycle

## Notes

PR #194 review. Most impactful functional bug — silent data loss after first recovery.

## Work Log

### 2026-03-20 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-03-20 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready

### 2026-03-20 - Completed

**By:** Agent
**Actions:**
- Status changed: in-progress → done
