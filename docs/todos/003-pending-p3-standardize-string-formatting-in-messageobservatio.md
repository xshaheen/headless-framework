---
status: pending
priority: p3
issue_id: "003"
tags: ["code-review","quality"]
dependencies: []
---

# Standardize string formatting in MessageObservationTimeoutException._BuildMessage

## Problem Statement

Mixed use of string.Format with CultureInfo.InvariantCulture and interpolated strings in the same method. Inconsistent style for diagnostic text.

## Findings

- **Location:** src/Headless.Messaging.Testing/MessageObservationTimeoutException.cs:47-71
- **Discovered by:** code-simplicity-reviewer, pragmatic-dotnet-reviewer, strict-dotnet-reviewer

## Proposed Solutions

_To be analyzed during triage._

## Recommended Action

Use interpolated strings throughout. For the float format, use elapsed.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture).

## Acceptance Criteria

- [ ] Consistent string formatting style in _BuildMessage

## Notes

Source: Code review

## Work Log

### 2026-03-20 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
