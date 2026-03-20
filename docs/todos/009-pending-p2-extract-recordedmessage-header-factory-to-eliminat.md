---
status: pending
priority: p2
issue_id: "009"
tags: ["code-review","quality"]
dependencies: []
---

# Extract RecordedMessage header factory to eliminate DRY violation

## Problem Statement

RecordingTransport and RecordingConsumeExecutionPipeline both contain identical 9-line header extraction logic (messageId, correlationId, topic from headers dictionary). A header name change requires touching two files.

## Findings

- **Location:** src/Headless.Messaging.Testing/Internal/RecordingTransport.cs:20-26
- **Location:** src/Headless.Messaging.Testing/Internal/RecordingConsumeExecutionPipeline.cs:48-53
- **Discovered by:** code-simplicity-reviewer

## Proposed Solutions

_To be analyzed during triage._

## Recommended Action

Add internal static RecordedMessage.FromHeaders(headers, message, messageType, exception?) factory method on RecordedMessage. Use from both decorators.

## Acceptance Criteria

- [ ] Header extraction logic exists in one place only
- [ ] Both decorators use the shared factory
- [ ] All tests pass

## Notes

Source: Code review

## Work Log

### 2026-03-20 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
