---
status: done
priority: p2
issue_id: "120"
tags: ["code-review","dotnet","messaging","security"]
dependencies: []
---

# Sanitize logMessage.Reason in IConsumerRegister._WriteLog

## Problem Statement

In IConsumerRegister._WriteLog, logMessage.Reason comes from broker client libraries (RabbitMQ, Kafka, Azure Service Bus, NATS, SQS, Redis). These can contain control characters or ANSI sequences derived from message metadata. All _WriteLog cases pass logMessage.Reason to structured log properties without going through _SanitizeHeader.

## Findings

- **Location:** src/Headless.Messaging.Core/Internal/IConsumerRegister.cs:586-633
- **Risk:** Log injection via broker-controlled string — secondary vector missed by first-pass fix
- **Discovered by:** compound-engineering:review:security-sentinel

## Proposed Solutions

### Apply _SanitizeHeader to logMessage.Reason once at top of _WriteLog
- **Pros**: Single fix covers all 12+ call sites
- **Cons**: None
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Add `var reason = _SanitizeHeader(logMessage.Reason) ?? string.Empty;` at top of _WriteLog and use in all log calls.

## Acceptance Criteria

- [ ] logMessage.Reason sanitized before any log call in _WriteLog
- [ ] Control chars and bidi overrides stripped

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
