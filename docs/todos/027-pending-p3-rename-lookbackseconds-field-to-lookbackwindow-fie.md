---
status: pending
priority: p3
issue_id: "027"
tags: ["code-review","dotnet","quality"]
dependencies: []
---

# Rename _lookbackSeconds field to _lookbackWindow (field is a TimeSpan not seconds)

## Problem Statement

In MessageNeedToRetryProcessor, the field is declared as `private readonly TimeSpan _lookbackSeconds;` but named as if it contains a number of seconds. This misleads readers into thinking it stores a raw integer/double of seconds.

## Findings

- **Location:** src/Headless.Messaging.Core/Processor/IProcessor.NeedRetry.cs:27
- **Discovered by:** compound-engineering:review:strict-dotnet-reviewer

## Proposed Solutions

### Rename
- **Pros**: Clear
- **Cons**: None
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Rename _lookbackSeconds to _lookbackWindow throughout the file.

## Acceptance Criteria

- [ ] Field renamed to _lookbackWindow
- [ ] All usages updated

## Notes

Discovered during PR #194 code review (round 2)

## Work Log

### 2026-03-21 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
