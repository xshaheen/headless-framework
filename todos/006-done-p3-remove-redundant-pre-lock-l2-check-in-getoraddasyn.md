---
status: done
priority: p3
issue_id: "006"
tags: ["code-review","performance","dotnet"]
dependencies: []
---

# Remove redundant pre-lock L2 check in GetOrAddAsync

## Problem Statement

GetOrAddAsync checks L2 before acquiring the lock, then checks both L1 and L2 again after acquiring the lock. Under cache miss scenarios with contention, this causes unnecessary L2 network calls. The pre-lock L2 check causes N concurrent requests to all hit L2 unnecessarily.

## Findings

- **Location:** src/Headless.Caching.Hybrid/HybridCache.cs:147-155
- **Current flow:** L1 -> L2 -> Lock -> L1 -> L2 -> Factory
- **Optimal flow:** L1 -> Lock -> L1 -> L2 -> Factory
- **Discovered by:** performance-oracle

## Proposed Solutions

### Option 1: Remove pre-lock L2 check
- **Pros**: Eliminates redundant network calls
- **Cons**: Slightly slower for uncontended misses
- **Effort**: Small
- **Risk**: Low

### Option 2: Keep current pattern (document trade-off)
- **Pros**: Avoids lock acquisition for L2 hits
- **Cons**: Network amplification under contention
- **Effort**: None
- **Risk**: None


## Recommended Action

Remove pre-lock L2 check to eliminate redundant network calls under contention.

## Acceptance Criteria

- [ ] Pre-lock L2 check removed
- [ ] Post-lock double-check still includes both L1 and L2
- [ ] Benchmark shows improvement under contention

## Notes

Trade-off: Current pattern is faster for uncontended L2 hits but causes network amplification under stampede.

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
