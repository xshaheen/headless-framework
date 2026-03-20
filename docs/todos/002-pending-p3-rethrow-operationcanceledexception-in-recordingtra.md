---
status: pending
priority: p3
issue_id: "002"
tags: ["code-review","quality"]
dependencies: []
---

# Rethrow OperationCanceledException in RecordingTransport catch

## Problem Statement

RecordingTransport.SendAsync has a bare catch {} that swallows all exceptions from serializer.DeserializeAsync, including OperationCanceledException. If publish is cancelled, the cancellation is silently misreported as a successful fallback observation.

## Findings

- **Location:** src/Headless.Messaging.Testing/Internal/RecordingTransport.cs:50-52
- **Discovered by:** strict-dotnet-reviewer, pragmatic-dotnet-reviewer

## Proposed Solutions

_To be analyzed during triage._

## Recommended Action

Add catch (OperationCanceledException) { throw; } before the bare catch.

## Acceptance Criteria

- [ ] OperationCanceledException is rethrown, not swallowed
- [ ] Other deserialization exceptions still caught silently

## Notes

Source: Code review

## Work Log

### 2026-03-20 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
