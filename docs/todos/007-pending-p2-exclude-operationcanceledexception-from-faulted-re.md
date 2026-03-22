---
status: pending
priority: p2
issue_id: "007"
tags: ["code-review","correctness"]
dependencies: []
---

# Exclude OperationCanceledException from Faulted recording

## Problem Statement

RecordingConsumeExecutionPipeline catches all exceptions including OperationCanceledException and records them as Faulted. Cancellation is not a consumer logic failure — WaitForFaulted<T> would spuriously match a cancelled consumer. RecordingTransport correctly re-throws OCE without recording.

## Findings

- **Location:** src/Headless.Messaging.Testing/Internal/RecordingConsumeExecutionPipeline.cs:29-36
- **Discovered by:** strict-dotnet-reviewer

## Proposed Solutions

_To be analyzed during triage._

## Recommended Action

Add catch (OperationCanceledException) { throw; } before the general catch block.

## Acceptance Criteria

- [ ] OperationCanceledException not recorded as Faulted
- [ ] Other exceptions still recorded as Faulted
- [ ] Test added for cancellation-during-consume scenario

## Notes

Source: Code review

## Work Log

### 2026-03-22 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
