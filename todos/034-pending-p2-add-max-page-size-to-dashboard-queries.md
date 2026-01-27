---
status: pending
priority: p2
issue_id: "034"
tags: [security, dos, dashboard]
dependencies: []
---

# Add max page size limit to Dashboard query endpoints

## Problem Statement

`RouteActionProvider.cs:352-353` allows unbounded page sizes in message list queries, enabling DoS via memory exhaustion.

**File:** `src/Headless.Messaging.Dashboard/RouteActionProvider.cs`

```csharp
var pageSize = httpContext.Request.Query["perPage"].ToInt32OrDefault(20);
// No upper bound validation - attacker can request perPage=1000000
```

**Risk:** Attackers can request extremely large page sizes, causing:
- Memory exhaustion
- Database performance degradation
- Service denial

## Findings

- **Severity:** Medium (P2)
- **Impact:** DoS via memory/database exhaustion
- **Exploitability:** High

## Proposed Solutions

### Option 1: Add max page size constant (Recommended)
```csharp
const int MaxPageSize = 100;
var pageSize = Math.Min(httpContext.Request.Query["perPage"].ToInt32OrDefault(20), MaxPageSize);
```
- **Pros**: Simple, immediate fix
- **Cons**: May break existing clients expecting larger pages
- **Effort**: Small
- **Risk**: Low

## Recommended Action

Add `MaxPageSize = 100` constant and clamp page size.

## Acceptance Criteria

- [ ] Add `MaxPageSize` constant (suggest 100)
- [ ] Clamp `perPage` to max value
- [ ] Document the limit in API response or headers

## Notes

Source: Security Sentinel agent code review

## Work Log

### 2026-01-25 - Created

**By:** Code Review Agent
**Actions:**
- Created from multi-agent code review findings
