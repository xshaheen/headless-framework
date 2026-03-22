---
status: pending
priority: p2
issue_id: "015"
tags: ["code-review","concurrency"]
dependencies: []
---

# Reorder CTS creation before waiter registration

## Problem Statement

In WaitForAsync, the CancellationTokenSource is created and CancelAfter called AFTER the waiter is added to _waiters. There is a window where the waiter is live but no timeout is armed.

## Findings

- **Location:** src/Headless.Messaging.Testing/MessageObservationStore.cs:83-98
- **Discovered by:** performance-oracle

## Proposed Solutions

_To be analyzed during triage._

## Recommended Action

Create CTS, call CancelAfter, and Register before adding entry to _waiters.

## Acceptance Criteria

- [ ] CTS created before _waiters.Add(entry)
- [ ] Timeout is armed before waiter is visible to Record()

## Notes

Source: Code review

## Work Log

### 2026-03-22 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
