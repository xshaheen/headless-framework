---
status: ready
priority: p3
issue_id: "010"
tags: ["code-review","security","dotnet"]
dependencies: []
---

# Add startup warning or health check when audit logging is disabled

## Problem Statement

AuditLogOptions.IsEnabled = false completely silences all audit capture with no log output at startup and no monitoring signal ever. A misconfigured environment variable setting IsEnabled=false in production will silently disable auditing for the entire process lifetime. No metric, health check, or log entry indicates this state. Operators cannot detect silent audit loss from dashboards.

## Findings

- **Location:** src/Headless.AuditLog.Abstractions/AuditLogOptions.cs:12
- **Discovered by:** security-sentinel

## Proposed Solutions

_To be analyzed during triage._

## Recommended Action

Log at Warning level on first SaveChanges call when IsEnabled = false, or add a startup health check. At minimum: in EfAuditChangeCapture.CaptureChanges, when IsEnabled is false add `logger.LogWarning("Audit logging is DISABLED. No audit entries are being captured.")` — but only once (use a flag or startup log in Setup.cs).

## Acceptance Criteria

- [ ] IsEnabled=false produces at least one LogWarning at startup or first capture attempt
- [ ] Warning is clear and actionable: 'Audit logging is disabled — set AuditLogOptions.IsEnabled = true to enable'

## Notes

Discovered during PR #187 code review.

## Work Log

### 2026-03-15 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-03-15 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready
