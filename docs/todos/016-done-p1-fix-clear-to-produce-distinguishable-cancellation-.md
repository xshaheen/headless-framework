---
status: done
priority: p1
issue_id: "016"
tags: ["code-review","quality","testing"]
dependencies: []
---

# Fix Clear() to produce distinguishable cancellation for pending waiters

## Problem Statement

MessageObservationStore.Clear() calls TrySetCanceled() on all pending waiters. This produces an OperationCanceledException indistinguishable from external cancellation (test runner abort) or timeout. The WaitForAsync catch guard 'when (!cancellationToken.IsCancellationRequested)' may or may not catch it depending on which token was used. Test failures caused by Clear() look like timeouts with no diagnostic context.

## Findings

- **Location:** src/Headless.Messaging.Testing/MessageObservationStore.cs:147-149
- **Impact:** Non-deterministic behavior; test failures from Clear() are misdiagnosed as timeouts
- **Discovered by:** pragmatic-dotnet-reviewer, agent-native-reviewer

## Proposed Solutions

### Use TrySetException with descriptive exception
- **Pros**: Machine-readable, distinguishable from cancellation/timeout
- **Cons**: New exception type needed
- **Effort**: Small
- **Risk**: Low

### Use TrySetCanceled(CancellationToken.None)
- **Pros**: Minimal change, ensures timeout guard catches it consistently
- **Cons**: Still an OperationCanceledException — less descriptive
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Use TrySetException with InvalidOperationException('MessagingTestHarness.Clear() was called while a WaitFor* was pending.') — clearest diagnostic.

## Acceptance Criteria

- [ ] Clear() during pending WaitFor* produces a distinguishable exception
- [ ] Exception message mentions Clear() as the cause
- [ ] Test added for Clear() during pending waiter scenario

## Notes

Shared fixture pattern (Clear() between tests) is the primary trigger.

## Work Log

### 2026-03-22 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-03-22 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready

### 2026-03-22 - Completed

**By:** Agent
**Actions:**
- Status changed: in-progress → done
