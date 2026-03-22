---
status: pending
priority: p3
issue_id: "056"
tags: ["code-review","security"]
dependencies: []
---

# Broker error e.Message logged unsanitized in ConsumerRegister — log injection vector

## Problem Statement

Three catch blocks in IConsumerRegister.cs log `e.Message` directly without sanitization (lines 219, 265, 271). Exception messages from broker connection failures (RabbitMQ, Kafka, NATS) may contain broker-supplied error strings — external, attacker-controlled input. These bypass LogSanitizer, creating a log injection vector for broker-originated exception messages.

## Findings

- **Locations:** src/Headless.Messaging.Core/Internal/IConsumerRegister.cs:219, 265, 271
- **Discovered by:** security-sentinel (P3)

## Proposed Solutions

### Remove {Message} parameter from log calls (exception object e is already the first arg)
- **Pros**: Simplest fix — structured logging captures the exception separately
- **Cons**: Slightly less explicit message string in text-based sinks
- **Effort**: Small
- **Risk**: Low

### Wrap e.Message in LogSanitizer.Sanitize(e.Message)
- **Pros**: Preserves message in logs, sanitizes injection chars
- **Cons**: Minor allocation
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Simplest: remove the `{Message}` structured parameter — the exception e is already logged and structured sinks capture it. Alternatively wrap with LogSanitizer.Sanitize.

## Acceptance Criteria

- [ ] e.Message not used as an unsanitized log parameter at lines 219, 265, 271
- [ ] Logs still capture sufficient context for debugging

## Notes

Source: Code review

## Work Log

### 2026-03-22 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
