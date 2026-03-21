---
status: ready
priority: p3
issue_id: "104"
tags: ["code-review","dotnet","messaging"]
dependencies: []
---

# Make _lockSafetyMargin a static readonly field in retry processor

## Problem Statement

In IProcessor.NeedRetry, `private readonly TimeSpan _lockSafetyMargin = TimeSpan.FromSeconds(10)` is an instance field that never changes across instances. It should be static readonly to avoid allocating the field per processor instance.

## Findings

- **Location:** src/Headless.Messaging.Core/Processor/IProcessor.NeedRetry.cs (~line 3967)
- **Risk:** Minor: unnecessary per-instance field allocation
- **Discovered by:** code-simplicity-reviewer

## Proposed Solutions

### Change to private static readonly TimeSpan _lockSafetyMargin
- **Pros**: Single allocation, correct for a constant value
- **Cons**: None
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Change `private readonly TimeSpan _lockSafetyMargin = TimeSpan.FromSeconds(10)` to `private static readonly TimeSpan _lockSafetyMargin = TimeSpan.FromSeconds(10)`.

## Acceptance Criteria

- [ ] _lockSafetyMargin is static readonly

## Notes

PR #194.

## Work Log

### 2026-03-21 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-03-21 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready
