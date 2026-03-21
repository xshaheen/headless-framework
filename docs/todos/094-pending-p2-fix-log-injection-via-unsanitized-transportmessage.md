---
status: pending
priority: p2
issue_id: "094"
tags: ["code-review","dotnet","messaging","security"]
dependencies: []
---

# Fix log injection via unsanitized transportMessage.GetName() and GetId() in structured logs

## Problem Statement

In IConsumerRegister._RegisterMessageProcessor, transportMessage.GetName() and transportMessage.GetId() are passed directly to _logger.MessageReceived() without sanitization. These values come from broker message headers and can contain control characters, ANSI escape sequences, or Unicode bidi overrides. An attacker who can publish messages can inject malicious content into log entries, potentially poisoning SIEM dashboards.

## Findings

- **Location:** src/Headless.Messaging.Core/Internal/IConsumerRegister.cs (~line 3636)
- **Risk:** Log injection via message headers — OWASP A09 Security Logging and Monitoring Failures
- **Discovered by:** security-sentinel

## Proposed Solutions

### Apply _SanitizeGroupName-equivalent sanitization to GetName() and GetId()
- **Pros**: Consistent sanitization across all header values used in logs
- **Cons**: Minor overhead
- **Effort**: Small
- **Risk**: Low

### Extract a generic _SanitizeHeader(string?) helper and use for all header values in logs
- **Pros**: Reusable, consistent
- **Cons**: Slightly more refactoring
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Extract _SanitizeHeader(string?) and apply to GetName(), GetId(), and any other header-derived values used in log structured properties. Also strip Unicode bidi overrides (U+202E, U+202A-U+202E, U+2066-U+2069) which char.IsControl() misses.

## Acceptance Criteria

- [ ] GetName() and GetId() sanitized before appearing in log structured properties
- [ ] Unicode bidi overrides stripped in sanitization
- [ ] Test verifies control chars and bidi overrides are stripped from log values

## Notes

PR #194.

## Work Log

### 2026-03-21 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
