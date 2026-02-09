---
status: ready
priority: p3
issue_id: "015"
tags: ["naming","code-review","scheduling"]
dependencies: []
---

# Rename RetryCount to MaxRetries for clarity

## Problem Statement

ScheduledJob.RetryCount is a configuration property (max allowed retries) but reads like runtime state (current retry count). This creates confusion with JobExecution.RetryAttempt.

## Findings

- **Location:** src/Headless.Messaging.Abstractions/Scheduling/ScheduledJob.cs
- **Reviewer:** strict-dotnet-reviewer, pragmatic-dotnet-reviewer

## Proposed Solutions

### Rename to MaxRetries
- **Pros**: Clear intent; matches common conventions
- **Cons**: Breaking change for consumers
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Rename RetryCount to MaxRetries. Update all references.

## Acceptance Criteria

- [ ] Property named MaxRetries
- [ ] XML docs updated

## Notes

PR #170 code review finding.

## Work Log

### 2026-02-08 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-02-09 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready
