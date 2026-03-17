---
status: done
priority: p1
issue_id: "017"
tags: ["code-review","security","dotnet"]
dependencies: []
---

# /negotiate auth middleware bypass via Contains check

## Problem Statement

AuthMiddleware._IsExcludedPath uses path.Contains('/negotiate') to exclude SignalR negotiate paths from auth. Any crafted path containing '/negotiate' anywhere (e.g. /api/stats/negotiate-bypass) bypasses authentication. The middleware runs before route matching, so the request reaches the endpoint handler unauthenticated.

## Findings

- **Location:** src/Headless.Dashboard.Authentication/AuthMiddleware.cs:64-75
- **Risk:** High — authentication bypass on any path containing /negotiate
- **OWASP:** A01:2021 — Broken Access Control
- **Discovered by:** security-sentinel

## Proposed Solutions

### Use EndsWith instead of Contains
- **Pros**: Simple, matches SignalR convention
- **Cons**: None
- **Effort**: Small
- **Risk**: None

### Use segment match: /hub/negotiate
- **Pros**: More precise, matches actual SignalR hub path
- **Cons**: Slightly more restrictive
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Replace .Contains('/negotiate') with .EndsWith('/negotiate', StringComparison.Ordinal)

## Acceptance Criteria

- [ ] Only actual /negotiate paths bypass auth
- [ ] SignalR hub negotiation still works without auth
- [ ] Other paths containing 'negotiate' require auth

## Notes

Combined with the AllowAnonymous ping endpoint, this widens the unauthenticated attack surface.

## Work Log

### 2026-03-17 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-03-17 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready

### 2026-03-17 - Completed

**By:** Agent
**Actions:**
- Status changed: in-progress → done
