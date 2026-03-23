---
status: pending
priority: p3
issue_id: "096"
tags: ["code-review","dotnet","quality"]
dependencies: []
---

# TaskCompletionSource<bool> should be TaskCompletionSource (non-generic)

## Problem Statement

ConsumerPauseGate uses TaskCompletionSource<bool> but the bool result is never observed. WaitIfPausedAsync wraps it in ValueTask discarding the value. Semantically misleading.

## Findings

- **Location:** src/Headless.Messaging.Core/Transport/ConsumerPauseGate.cs:13,33,65
- **Discovered by:** compound-engineering:review:pragmatic-dotnet-reviewer, compound-engineering:review:strict-dotnet-reviewer

## Proposed Solutions

_To be analyzed during triage._

## Recommended Action

Replace with non-generic TaskCompletionSource throughout ConsumerPauseGate.

## Acceptance Criteria

- [ ] ConsumerPauseGate uses TaskCompletionSource (non-generic)

## Notes

Source: Code review

## Work Log

### 2026-03-23 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
