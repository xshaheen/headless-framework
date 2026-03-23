---
status: pending
priority: p2
issue_id: "106"
tags: ["code-review","documentation"]
dependencies: []
---

# README ResetAsync signature includes non-existent CancellationToken parameter

## Problem Statement

README at line 281 shows monitor.ResetAsync('payments', cancellationToken) but the actual ICircuitBreakerMonitor.ResetAsync signature is ValueTask<bool> ResetAsync(string groupName) — no CancellationToken. Code following the README won't compile.

## Findings

- **Location:** src/Headless.Messaging.Core/README.md:281
- **Correct signature:** ValueTask<bool> ResetAsync(string groupName)
- **Discovered by:** compound-engineering:review:agent-native-reviewer

## Proposed Solutions

_To be analyzed during triage._

## Recommended Action

Remove cancellationToken from the README call site.

## Acceptance Criteria

- [ ] README ResetAsync example matches actual interface signature

## Notes

IRetryProcessorMonitor.ResetBackpressureAsync does accept a CT, which likely caused the confusion.

## Work Log

### 2026-03-23 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
