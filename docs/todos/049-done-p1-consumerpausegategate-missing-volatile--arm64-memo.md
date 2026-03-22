---
status: done
priority: p1
issue_id: "049"
tags: ["code-review","concurrency"]
dependencies: []
---

# ConsumerPauseGate._gate missing volatile — ARM64 memory ordering bug

## Problem Statement

ConsumerPauseGate.cs declares `_gate` as a plain field but WaitIfPausedAsync reads it lock-free with the comment 'snapshot under volatile read'. On ARM64 (weakly-ordered memory model), a plain field read is not guaranteed to see the latest write from PauseAsync on another thread. This is a real correctness bug on Apple Silicon and AWS Graviton, not a theoretical concern.

## Findings

- **Location:** src/Headless.Messaging.Core/Transport/ConsumerPauseGate.cs:13
- **Discovered by:** strict-dotnet-reviewer, performance-oracle
- **Impact:** Consumer can proceed past the pause gate on ARM64, processing messages while circuit thinks it is paused

## Proposed Solutions

### Mark _gate as volatile
- **Pros**: Minimal change, correct for this use case
- **Cons**: None
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Declare the field as `private volatile TaskCompletionSource<bool> _gate = _CreateCompletedGate();`

## Acceptance Criteria

- [ ] _gate field declared volatile
- [ ] No behavior change to existing tests
- [ ] Comment updated to remove misleading 'volatile read' claim if field is now actually volatile

## Notes

Source: Code review

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
