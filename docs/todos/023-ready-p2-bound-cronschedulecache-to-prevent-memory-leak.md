---
status: ready
priority: p2
issue_id: "023"
tags: ["performance","code-review","scheduling"]
dependencies: []
---

# Bound CronScheduleCache to prevent memory leak

## Problem Statement

CronScheduleCache uses an unbounded ConcurrentDictionary registered as singleton. If users dynamically generate cron expressions (e.g., from dashboard or ScheduleOnceAsync), this grows without bound for the process lifetime.

## Findings

- **Location:** src/Headless.Messaging.Core/Scheduling/CronScheduleCache.cs
- **Risk:** Medium - unbounded memory growth with dynamic cron expressions
- **Reviewer:** strict-dotnet-reviewer

## Proposed Solutions

### Use MemoryCache with sliding expiration
- **Pros**: Auto-eviction; bounded
- **Cons**: Slightly more complex
- **Effort**: Small
- **Risk**: Low

### Add size limit with LRU eviction
- **Pros**: Explicit bound
- **Cons**: Custom eviction logic
- **Effort**: Medium
- **Risk**: Low


## Recommended Action

Switch to MemoryCache with sliding expiration (e.g., 1 hour). Recurring jobs will keep their entries warm; one-time jobs naturally evict.

## Acceptance Criteria

- [ ] Cron cache has bounded size or expiration policy

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
