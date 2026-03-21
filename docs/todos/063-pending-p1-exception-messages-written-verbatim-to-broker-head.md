---
status: pending
priority: p1
issue_id: "063"
tags: ["code-review","security"]
dependencies: []
---

# Exception messages written verbatim to broker headers — sensitive info disclosure

## Problem Statement

Line 360: transportMessage.Headers[Headers.Exception] = e.GetType().Name + "-->" + e.Message. Exception messages routinely contain connection strings (SqlException), file paths (IOException), internal hostnames (SocketException), auth tokens in URIs (HttpRequestException). This string is persisted to DB and visible in broker headers to downstream consumers.

## Findings

- **Location:** IConsumerRegister.cs:360
- **Risk:** Critical — leaks infrastructure secrets via broker headers and DB storage
- **Discovered by:** security-sentinel

## Proposed Solutions

### Store only exception type name in header
- **Pros**: No info leakage, simple change
- **Cons**: Less diagnostic detail in header
- **Effort**: Small
- **Risk**: Low

### Move full diagnostics to structured log entry
- **Pros**: Log redaction policies can apply, full detail available
- **Cons**: Two-step diagnosis (check logs separately)
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Write only e.GetType().Name to Headers.Exception. Log full exception at Warning/Error level where log redaction policies apply.

## Acceptance Criteria

- [ ] Headers.Exception contains only exception type name
- [ ] Full exception details logged at Warning/Error with structured logging
- [ ] No free-form exception text in broker-visible headers

## Notes

Exception.Message can contain connection strings, file paths, tokens.

## Work Log

### 2026-03-21 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
