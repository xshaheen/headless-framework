---
status: pending
priority: p2
issue_id: "007"
tags: [performance, caching, memory]
dependencies: []
---

# Bound InMemoryCache queue growth under write-heavy workloads

## Problem Statement

`InMemoryCache._lruQueue` and `_expirationQueue` (`PriorityQueue`) grow without bound under write-heavy workloads. Each `Set`/`SetAdd` enqueues a new entry; stale entries are only removed during periodic maintenance. Between maintenance cycles, queues can accumulate unbounded stale entries, consuming memory disproportionate to the actual cache size.

## Findings

- **File:** `src/Headless.Caching.Memory/InMemoryCache.cs`
- **Status:** Identified during PR #216 code review
- **Priority:** p2

## Proposed Solutions

1. **Inline eviction on threshold**: trigger a lightweight queue compaction when `queue.Count > N * _memory.Count` (e.g., N=3), removing entries whose version no longer matches `_memory`.
2. **Bounded queue with overflow spill**: cap queue size and spill oldest entries, accepting slightly less optimal eviction ordering.
3. **Generational compaction**: compact queues during maintenance by rebuilding from `_memory` when staleness ratio exceeds a threshold.

## Recommended Action

Option 1 or 3 — inline threshold check is simplest; generational compaction is more thorough but runs less frequently.

## Acceptance Criteria

- [ ] Queue size is bounded proportional to cache entry count
- [ ] No regression in maintenance cycle latency
- [ ] Stress test with 10x write-to-read ratio shows bounded memory growth
