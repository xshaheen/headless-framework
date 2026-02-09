---
<<<<<<<< HEAD:docs/todos/011-ready-p2-restrict-negotiate-auth-exclusion-scope-in-authmid.md
status: ready
|||||||| 6e93708f:todos/011-pending-p2-restrict-negotiate-auth-exclusion-scope-in-authmid.md
status: pending
========
status: done
>>>>>>>> refs/heads/codex/cache-perf-fixes:docs/todos/011-done-p2-restrict-negotiate-auth-exclusion-scope-in-authmid.md
priority: p2
issue_id: "011"
tags: ["security","code-review","scheduling"]
dependencies: []
---

# Restrict /negotiate auth exclusion scope in AuthMiddleware

## Problem Statement

AuthMiddleware broadly excludes all paths ending in /negotiate from authentication. This is intended for SignalR negotiate endpoint but could accidentally bypass auth on future endpoints matching the pattern.

## Findings

- **Location:** src/Headless.Messaging.Dashboard/ (AuthMiddleware)
- **Risk:** Medium - overly broad auth bypass pattern
- **Reviewer:** security-sentinel

## Proposed Solutions

### Use exact path matching for SignalR negotiate endpoint
- **Pros**: Precise; no accidental bypass
- **Cons**: Needs update if hub path changes
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Match specific path (e.g., /hubs/scheduling/negotiate) instead of wildcard /negotiate suffix.

## Acceptance Criteria

- [ ] Auth exclusion uses specific path, not suffix pattern

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
<<<<<<<< HEAD:docs/todos/011-ready-p2-restrict-negotiate-auth-exclusion-scope-in-authmid.md
|||||||| 6e93708f:todos/011-pending-p2-restrict-negotiate-auth-exclusion-scope-in-authmid.md
========

### 2026-02-09 - Completed

**By:** Agent
**Actions:**
- Status changed: ready → done
>>>>>>>> refs/heads/codex/cache-perf-fixes:docs/todos/011-done-p2-restrict-negotiate-auth-exclusion-scope-in-authmid.md
