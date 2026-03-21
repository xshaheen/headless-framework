---
status: done
priority: p3
issue_id: "114"
tags: ["code-review","dotnet","messaging","security"]
dependencies: []
---

# Sanitize GetName() and GetGroup() in SubscribeExecutor log and exception messages

## Problem Statement

In ISubscribeExecutor._SetSuccessfulState and related paths, message.Origin.GetName() and message.Origin.GetGroup() are logged and included in exception messages without sanitization. These values were originally sanitized at the _RegisterMessageProcessor entry point before storage, but the values stored to the database may differ from what was sanitized at log time. The exception message string is also stored to MediumMessage.ExceptionInfo in the database, creating a secondary injection vector.

## Findings

- **Location:** src/Headless.Messaging.Core/Internal/ISubscribeExecutor.cs:82-88,191
- **Risk:** Log injection via persisted message fields; unsanitized values in stored exception info
- **Discovered by:** compound-engineering:review:security-sentinel

## Proposed Solutions

### Apply _SanitizeHeader equivalent to GetName() and GetGroup() before log/exception use in SubscribeExecutor
- **Pros**: Defense-in-depth at read path
- **Cons**: None
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Apply _SanitizeHeader (or equivalent) to GetName() and GetGroup() in SubscribeExecutor before use in log structured properties and exception messages.

## Acceptance Criteria

- [ ] GetName() and GetGroup() sanitized in SubscribeExecutor log calls
- [ ] Exception message string sanitized before storage in ExceptionInfo

## Notes

PR #194 second-pass review.

## Work Log

### 2026-03-21 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-03-21 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready

### 2026-03-21 - Completed

**By:** Agent
**Actions:**
- Status changed: in-progress → done
