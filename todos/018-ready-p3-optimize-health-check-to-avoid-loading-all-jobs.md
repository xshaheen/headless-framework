---
status: ready
priority: p3
issue_id: "018"
tags: ["performance","code-review","scheduling"]
dependencies: []
---

# Optimize health check to avoid loading all jobs

## Problem Statement

The scheduling health check loads all jobs to count stale ones. This is wasteful for a health endpoint that should be lightweight.

## Findings

- **Location:** Health check in scheduling module
- **Reviewer:** strict-dotnet-reviewer

## Proposed Solutions

### Add dedicated COUNT query to storage interface
- **Pros**: Minimal DB load; proper health check semantics
- **Cons**: New storage method
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Add GetStaleJobCountAsync to IScheduledJobStorage. Health check calls that instead of loading all jobs.

## Acceptance Criteria

- [ ] Health check uses COUNT query, not full table load

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
