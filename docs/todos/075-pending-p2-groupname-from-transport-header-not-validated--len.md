---
status: pending
priority: p2
issue_id: "075"
tags: ["code-review","security"]
dependencies: []
---

# GroupName from transport header not validated — length and control chars

## Problem Statement

transportMessage.GetGroup() returns arbitrary string from broker message header. Flows directly into ConcurrentDictionary keys, log messages, and OTel metrics without length or character validation. Group names with newlines or ANSI escape sequences enable log injection. Before RegisterKnownGroups is armed, unbounded strings create memory growth.

## Findings

- **Location:** IConsumerRegister.cs:302
- **Risk:** Medium — log injection, memory growth from untrusted input
- **Discovered by:** security-sentinel

## Proposed Solutions

_To be analyzed during triage._

## Recommended Action

Validate group names: reject null/empty, max 256 chars, strip control characters. Apply in GetGroup() or at ICircuitBreakerStateManager entry points.

## Acceptance Criteria

- [ ] Group names validated for max length (256 chars)
- [ ] Control characters stripped or rejected
- [ ] Null/empty group names handled gracefully

## Notes

Source: Code review

## Work Log

### 2026-03-21 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
