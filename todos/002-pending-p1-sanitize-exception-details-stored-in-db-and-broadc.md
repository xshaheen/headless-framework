---
status: pending
priority: p1
issue_id: "002"
tags: ["security","code-review","scheduling"]
dependencies: []
---

# Sanitize exception details stored in DB and broadcast via SignalR

## Problem Statement

SchedulerBackgroundService.cs:210 stores ex.ToString() (full stack traces with internals) in DB ErrorDetails column. This data is then broadcast via SignalR to dashboard clients, leaking internal paths, assembly versions, and potentially sensitive data.

## Findings

- **Location:** src/Headless.Messaging.Core/Scheduling/SchedulerBackgroundService.cs:210
- **Risk:** High - information disclosure via stack traces in DB + SignalR broadcast
- **Reviewer:** security-sentinel

## Proposed Solutions

### Store ex.Message only, log full trace separately
- **Pros**: Simple, no internal exposure
- **Cons**: Less detail in DB for debugging
- **Effort**: Small
- **Risk**: Low

### Store sanitized summary, full trace only in structured logs
- **Pros**: Best of both worlds
- **Cons**: Slightly more code
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Store ex.Message + ex.GetType().Name in DB ErrorDetails; log full exception via ILogger. Ensure SignalR broadcast only sends sanitized error summary.

## Acceptance Criteria

- [ ] ErrorDetails does not contain full stack traces
- [ ] Full exception still logged via ILogger
- [ ] SignalR broadcast uses sanitized error info

## Notes

PR #170 code review finding.

## Work Log

### 2026-02-08 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
