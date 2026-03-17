---
status: ready
priority: p2
issue_id: "009"
tags: ["code-review","concurrency","typescript"]
dependencies: []
---

# Frontend race conditions — concurrent loads, ping spinner deadlock, double-execute

## Problem Statement

Multiple frontend race conditions: (1) Nodes.vue: concurrent loadNodes/loadNodesByNamespace write to shared state, last-write-wins. (2) pingAll + individual Ping can run concurrently on same node — pingingNodes Set gets add/delete mismatch, spinner locks forever. (3) Published/Received: pendingAction double-execute on confirm dialog double-click. (4) Tab-switch fires multiple loadMessages without generation counter. (5) Debounce timer not cleared on unmount.

## Findings

- **Concurrent loads:** Nodes.vue:292-304
- **Ping deadlock:** Nodes.vue:263-273 + template:114
- **Double-execute:** Published.vue:244, Received.vue:251
- **Tab-switch race:** Published.vue:297, Received.vue:305
- **Debounce leak:** Published.vue:289, Received.vue:297
- **Discovered by:** dan-frontend-races-reviewer

## Proposed Solutions

### Generation counter pattern + guards
- **Pros**: Standard Vue composable discipline, no deps
- **Cons**: Touches multiple files
- **Effort**: Medium
- **Risk**: Low


## Recommended Action

Add loadGeneration counter to loadMessages/loadNodes. Guard pingNode with if(pingingNodes.has). Guard onConfirmAction with isExecuting flag. Clear debounce timers in onUnmounted.

## Acceptance Criteria

- [ ] Concurrent loads are superseded by latest request
- [ ] Ping spinner always resolves
- [ ] Confirm action executes exactly once
- [ ] Debounce timer cleared on unmount

## Notes

Source: Code review

## Work Log

### 2026-03-17 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-03-17 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready
