---
status: done
priority: p3
issue_id: "009"
tags: ["code-review","documentation"]
dependencies: []
---

# Clarify RecordedMessage.Timestamp XML doc

## Problem Statement

Timestamp is set at observation time (after send ack or consume completion), not message creation time. Doc says 'UTC timestamp when the message was observed' which is technically correct but will confuse users comparing Published vs Consumed timestamps.

## Findings

- **Location:** src/Headless.Messaging.Testing/RecordedMessage.cs:42,70
- **Discovered by:** strict-dotnet-reviewer, pragmatic-dotnet-reviewer

## Proposed Solutions

_To be analyzed during triage._

## Recommended Action

Update XML doc to: 'UTC timestamp when the message observation was recorded (publish acknowledgment or consume completion).'

## Acceptance Criteria

- [ ] XML doc clearly states this is observation time, not message creation time

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
