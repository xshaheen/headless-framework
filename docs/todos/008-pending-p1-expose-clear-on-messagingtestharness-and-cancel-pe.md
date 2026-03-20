---
status: pending
priority: p1
issue_id: "008"
tags: ["code-review","quality"]
dependencies: []
---

# Expose Clear() on MessagingTestHarness and cancel pending waiters

## Problem Statement

MessageObservationStore.Clear() is internal and not exposed on MessagingTestHarness. Shared fixture tests cannot reset observation state between tests. Additionally, Clear() drops waiters from the list without cancelling their TaskCompletionSource — any pending WaitForAsync caller's Task will hang until timeout.

## Findings

- **Location:** src/Headless.Messaging.Testing/MessageObservationStore.cs:131-143
- **Location:** src/Headless.Messaging.Testing/MessagingTestHarness.cs (Clear missing)
- **Discovered by:** strict-dotnet-reviewer (P1), agent-native-reviewer (P2), pragmatic-dotnet-reviewer (P3)

## Proposed Solutions

### Cancel waiters in Clear() and expose on harness
- **Pros**: Fixes the hang, enables shared fixture reset
- **Cons**: Minor change
- **Effort**: Small
- **Risk**: Low


## Recommended Action

In MessageObservationStore.Clear(), call TrySetCanceled on all waiters before clearing the list. Add public void Clear() => _store.Clear() to MessagingTestHarness. Update README shared fixture example.

## Acceptance Criteria

- [ ] MessagingTestHarness exposes public Clear() method
- [ ] MessageObservationStore.Clear() cancels pending waiters via TrySetCanceled
- [ ] README shared fixture example calls harness.Clear()
- [ ] Test added for Clear() cancelling pending waiters

## Notes

Flagged by 3 independent reviewers

## Work Log

### 2026-03-20 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
