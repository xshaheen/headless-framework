---
status: done
priority: p2
issue_id: "002"
tags: ["code-review","performance","dotnet"]
dependencies: []
---

# Fix GetAllAsync N+1 sequential expiration queries

## Problem Statement

In GetAllAsync, for each key found in L2, the code sequentially fetches expiration and upserts to L1. With 100 keys, this is 200 sequential async operations causing severe performance degradation.

## Findings

- **Location:** src/Headless.Caching.Hybrid/HybridCache.cs:271-283
- **Impact:** 100 keys with 50 L2 hits = 50+ extra network round-trips
- **Discovered by:** strict-dotnet-reviewer, performance-oracle

## Proposed Solutions

### Option 1: Use DefaultLocalExpiration always for bulk ops
- **Pros**: Simple, eliminates all extra calls
- **Cons**: Ignores per-key L2 expiration
- **Effort**: Small
- **Risk**: Low

### Option 2: Batch L1 upserts with UpsertAllAsync
- **Pros**: Single L1 operation
- **Cons**: Still needs expiration handling
- **Effort**: Medium
- **Risk**: Low

### Option 3: Parallelize with Task.WhenAll
- **Pros**: Faster than sequential
- **Cons**: Still many operations
- **Effort**: Medium
- **Risk**: Medium


## Recommended Action

Use DefaultLocalExpiration for bulk operations and batch L1 upserts.

## Acceptance Criteria

- [ ] GetAllAsync does not make per-key GetExpirationAsync calls
- [ ] L1 population uses batch operation
- [ ] Performance test shows improvement

## Notes

This is an N+1 anti-pattern that will cause noticeable latency with batch operations.

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
