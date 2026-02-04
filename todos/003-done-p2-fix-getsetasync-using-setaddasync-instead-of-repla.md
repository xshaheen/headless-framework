---
status: done
priority: p2
issue_id: "003"
tags: ["code-review","bug","dotnet"]
dependencies: []
---

# Fix GetSetAsync using SetAddAsync instead of replace

## Problem Statement

When populating L1 from L2 hit in GetSetAsync, the code uses SetAddAsync which ADDS to an existing set rather than REPLACING it. If L1 already has partial/stale set data, this merges rather than replaces, leading to data inconsistency.

## Findings

- **Location:** src/Headless.Caching.Hybrid/HybridCache.cs:390
- **Current code:** await _l1Cache.SetAddAsync(key, cacheValue.Value!, localExpiration, ct)
- **Discovered by:** strict-dotnet-reviewer

## Proposed Solutions

### Option 1: Use UpsertAsync to replace the set
- **Pros**: Clean replacement, no stale data
- **Cons**: May need different serialization
- **Effort**: Small
- **Risk**: Low

### Option 2: Remove then add
- **Pros**: Explicit clear before add
- **Cons**: Two operations, brief window of empty set
- **Effort**: Small
- **Risk**: Medium


## Recommended Action

Use UpsertAsync to replace the entire set rather than merging.

## Acceptance Criteria

- [ ] GetSetAsync uses UpsertAsync or removes before adding
- [ ] Test verifies L1 is replaced, not merged
- [ ] No data inconsistency possible

## Notes

This is a correctness issue that could cause hard-to-debug data inconsistencies.

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
