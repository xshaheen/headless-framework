---
status: pending
priority: p1
issue_id: "088"
tags: ["code-review","docs","agent-native","messaging"]
dependencies: []
---

# Fix wrong ResetAsync signature in docs/llms/messaging.txt — extra CancellationToken breaks agent code

## Problem Statement

The docs/llms/messaging.txt 'Programmatic Control' example shows `await monitor.ResetAsync("payments", cancellationToken)` but ICircuitBreakerMonitor.ResetAsync has no CancellationToken parameter: `ValueTask<bool> ResetAsync(string groupName)`. Any agent or developer reading the LLM context file will generate code that fails to compile.

## Findings

- **Location:** docs/llms/messaging.txt — Programmatic Control section
- **Actual signature:** ValueTask<bool> ResetAsync(string groupName)
- **Docs signature:** monitor.ResetAsync("payments", cancellationToken) — WRONG
- **Impact:** All agent-generated circuit breaker recovery code will fail to compile
- **Discovered by:** agent-native-reviewer

## Proposed Solutions

### Fix the code snippet in docs/llms/messaging.txt
- **Pros**: Trivial fix, immediate correctness
- **Cons**: None
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Change `await monitor.ResetAsync("payments", cancellationToken)` to `await monitor.ResetAsync("payments")` in docs/llms/messaging.txt. Also add ForceOpenAsync to the same section since it is completely absent.

## Acceptance Criteria

- [ ] ResetAsync snippet in docs/llms/messaging.txt matches actual interface signature
- [ ] ForceOpenAsync documented in docs/llms/messaging.txt Programmatic Control section
- [ ] GetSnapshot documented with CircuitBreakerSnapshot fields
- [ ] KnownGroups documented in docs/llms/messaging.txt

## Notes

PR #194 code review finding. Bundle all docs/llms/messaging.txt agent-facing gaps into this single fix.

## Work Log

### 2026-03-23 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
