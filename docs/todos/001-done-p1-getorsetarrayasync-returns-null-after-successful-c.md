---
status: done
priority: p1
issue_id: "001"
tags: ["code-review","correctness","dotnet"]
dependencies: []
---

# GetOrSetArrayAsync returns null after successful cache write

## Problem Statement

JobsRedisContext.GetOrSetArrayAsync computes result from factory, writes to Redis cache, then returns null instead of result. Every cache miss silently discards the computed value. Callers always get null on cache-miss path, making the cache write pointless.

## Findings

- **Location:** src/Headless.Jobs.Caching.Redis/JobsRedisContext.cs:167
- **Risk:** Critical — silent data loss on every cache miss
- **Discovered by:** performance-oracle, strict-dotnet-reviewer, pragmatic-dotnet-reviewer

## Proposed Solutions

### Change return null to return result
- **Pros**: One-character fix, zero risk
- **Cons**: None
- **Effort**: Small
- **Risk**: None


## Recommended Action

Change line 167 from 'return null;' to 'return result;'

## Acceptance Criteria

- [ ] GetOrSetArrayAsync returns computed result on cache miss
- [ ] Existing tests still pass

## Notes

Confirmed by 3 independent reviewers. The catch block comment says 'result already computed' making intent clear.

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
