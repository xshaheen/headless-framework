---
status: done
priority: p3
issue_id: "008"
tags: ["code-review","quality","dotnet"]
dependencies: []
---

# Add missing argument validation consistency

## Problem Statement

Some methods validate arguments while others don't. GetByPrefixAsync, RemoveByPrefixAsync, and GetAllKeysByPrefixAsync don't validate the prefix parameter, unlike other methods that validate their inputs.

## Findings

- **GetByPrefixAsync:** src/Headless.Caching.Hybrid/HybridCache.cs:289-299
- **RemoveByPrefixAsync:** src/Headless.Caching.Hybrid/HybridCache.cs:968-987
- **GetAllKeysByPrefixAsync:** src/Headless.Caching.Hybrid/HybridCache.cs:302-311
- **Discovered by:** strict-dotnet-reviewer, security-sentinel

## Proposed Solutions

### Option 1: Add validation to all prefix methods
- **Pros**: Consistency, prevents empty prefix abuse
- **Cons**: Breaking change if empty prefix was intentional
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Add Argument.IsNotNull(prefix) or similar validation to all prefix methods.

## Acceptance Criteria

- [ ] All prefix methods validate prefix parameter
- [ ] Consider max prefix length validation
- [ ] Consistent with other method validation patterns

## Notes

Inconsistent validation is a code smell and potential security issue.

## Work Log

### 2026-02-04 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-02-04 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready

### 2026-02-04 - Completed

**By:** Agent
**Actions:**
- Status changed: in-progress → done
