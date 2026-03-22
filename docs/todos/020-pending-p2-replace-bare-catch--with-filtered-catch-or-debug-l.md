---
status: pending
priority: p2
issue_id: "020"
tags: ["code-review","observability"]
dependencies: []
---

# Replace bare catch {} with filtered catch or debug log

## Problem Statement

RecordingTransport has a bare catch {} that swallows all deserialization exceptions. If ISerializer throws due to a test infrastructure bug, the exception disappears and the test gets a cryptic timeout instead.

## Findings

- **Location:** src/Headless.Messaging.Testing/Internal/RecordingTransport.cs:48-50
- **Discovered by:** security-sentinel

## Proposed Solutions

_To be analyzed during triage._

## Recommended Action

Narrow to expected exceptions (JsonException, InvalidOperationException) or add debug-level logging.

## Acceptance Criteria

- [ ] Deserialization failures are either logged or narrowly caught
- [ ] Unexpected exceptions propagate for diagnosis

## Notes

Source: Code review

## Work Log

### 2026-03-22 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
