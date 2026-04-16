---
title: Optimize InMemoryCache Maintenance Algorithm
type: perf
status: active
date: 2026-04-16
origin: docs/todos/001-pending-p1-optimize-inmemorycache-maintenance-algorithm.md
---

# Optimize InMemoryCache Maintenance Algorithm

## Overview

The current `InMemoryCache` maintenance loop iterates over all cache entries every 250ms ($O(N)$), which scales poorly for large caches. This plan replaces the full scan with a more efficient expiration tracking system using a priority queue, making maintenance overhead sub-linear relative to the cache size.

## Problem Frame

The maintenance loop in `_DoMaintenanceAsync` performs an $O(N)$ scan to find expired items. This occurs every 250ms. As the cache grows to millions of items, this background task consumes excessive CPU and can impact application performance.

## Requirements Trace

- R1. Maintenance overhead must be constant or sub-linear relative to cache size.
- R2. All existing functional tests must pass.
- R3. Expiration correctness must be maintained (items removed when expired).

## Scope Boundaries

- **Optimization scope**: Only the `InMemoryCache` maintenance and compaction logic.
- **Out of scope**: Changes to the public `IInMemoryCache` interface or adding new dependencies.

## Context & Research

### Relevant Code and Patterns

- `src/Headless.Caching.Memory/InMemoryCache.cs`: The core implementation of the cache.
- `_DoMaintenanceAsync`: The method performing the $O(N)$ scan.
- `_FindLeastRecentlyUsedOrLargest`: Uses reservoir sampling, also $O(N)$.
- `.NET 10 PriorityQueue<TElement, TPriority>`: Available and ideal for expiration tracking.

### Institutional Learnings

- `docs/solutions/`: No specific solutions found for this cache implementation yet.

## Key Technical Decisions

- **Decision 1: Use PriorityQueue for Expiration Tracking**
  - **Rationale**: A priority queue allows finding the earliest-to-expire item in $O(1)$ (peek) or $O(\log N)$ (dequeue). Adding an item is $O(\log N)$. This is significantly better than $O(N)$ scans.
  - **Concurrency**: `PriorityQueue` is not thread-safe. A lock will be used around queue operations. Since updates are only on writes and maintenance, lock contention is expected to be minimal compared to the $O(N)$ scan.
  - **Handling Updates**: To avoid complex removals from the priority queue (which it doesn't support efficiently), we'll allow "stale" entries to remain in the queue. When an entry is dequeued, we verify its validity against the `ConcurrentDictionary` before removing it.

- **Decision 2: Replace Reservoir Sampling in Compaction**
  - **Rationale**: Reservoir sampling is also $O(N)$. We'll replace it with a more efficient candidate selection.
  - **New Approach**: Use a "Clock" or "FIFO-with-Retry" approach using a `ConcurrentQueue<string> _lruQueue` to track insertion/access order. This provides $O(1)$ candidate selection for eviction.

## Open Questions

### Resolved During Planning

- **Question**: Should we update LRU on every `GetAsync`?
- **Resolution**: No, that would cause high contention on the `_lruQueue`. Instead, we'll only update it on insertions/updates. This makes it more of a FIFO eviction, which is often sufficient for many workloads, or we can use the `LastAccessTicks` in `CacheEntry` to "re-enqueue" hot items when they reach the head of the queue during compaction (SCA - Second Chance Algorithm).

### Deferred to Implementation

- **Question**: How many times should we retry if the head of the LRU queue is still "hot"?
- **Why deferred**: This is a tuning parameter that can be adjusted after initial benchmarks.

## Implementation Units

- [ ] **Unit 1: Expiration Tracking with PriorityQueue**

**Goal:** Replace $O(N)$ expiration scan with priority queue.

**Requirements:** R1, R3

**Files:**
- Modify: `src/Headless.Caching.Memory/InMemoryCache.cs`

**Approach:**
- Add `private readonly PriorityQueue<string, long> _expirationQueue = new();`
- Add `private readonly object _expirationLock = new();`
- In `UpsertAsync` and other write methods:
  - If `expiration` is set: `lock(_expirationLock) { _expirationQueue.Enqueue(key, expiresAt.Ticks); }`
- In `_DoMaintenanceAsync`:
  - Instead of `foreach (var kvp in _memory)`, use a `while` loop:
    ```csharp
    while (true) {
        string key; long expiresAtTicks;
        lock(_expirationLock) {
            if (!_expirationQueue.TryPeek(out key, out expiresAtTicks) || expiresAtTicks > nowTicks) break;
            _expirationQueue.Dequeue();
        }
        if (_memory.TryGetValue(key, out var entry) && entry.ExpiresAt?.Ticks == expiresAtTicks) {
            _memory.TryRemove(key, out _);
            Interlocked.Add(ref _currentMemorySize, -entry.Size);
        }
    }
    ```

**Test scenarios:**
- Happy path: Items expire and are removed by the background loop correctly.
- Edge case: Items updated with new expiration are removed at the correct (new) time.
- Edge case: Items removed manually are correctly handled (no double-removal error).
- Integration: Large cache (e.g., $10^5$ items) does not cause high CPU spikes during maintenance.

**Verification:**
- Run `InMemoryCacheTests.cs` and ensure all pass.

- [ ] **Unit 2: Efficient Compaction with LRU/FIFO Queue**

**Goal:** Replace $O(N)$ reservoir sampling with efficient candidate selection.

**Requirements:** R1

**Files:**
- Modify: `src/Headless.Caching.Memory/InMemoryCache.cs`

**Approach:**
- Add `private readonly ConcurrentQueue<string> _lruQueue = new();`
- In `_SetInternalAsync`:
  - `_lruQueue.Enqueue(key);`
- In `_FindLeastRecentlyUsedOrLargest`:
  - Try to dequeue from `_lruQueue`.
  - If the key is not in `_memory` or is already expired, try again.
  - If `LastAccessTicks` is recent (hot), re-enqueue and try another.
  - Limit the number of retries to avoid infinite loops if all items are hot.

**Test scenarios:**
- Happy path: Eviction still clears space when `maxItems` is reached.
- Integration: Cache stability under heavy load with frequent evictions.

**Verification:**
- Run `InMemoryCacheTests.cs`.

## System-Wide Impact

- **Interaction graph**: No external impact, purely internal optimization.
- **Error propagation**: No change in error behavior.
- **State lifecycle risks**: Small risk of "phantom" items in `_expirationQueue` or `_lruQueue` if not handled correctly, but these are eventually cleared.
- **API surface parity**: No change to public API.
- **Integration coverage**: Ensure `HybridCache` (which uses `InMemoryCache`) still performs well.

## Risks & Dependencies

| Risk | Mitigation |
|------|------------|
| Lock contention on `_expirationLock` | Minimize lock duration by only enqueuing/dequeuing. Keep heavy logic (like `TryRemove` or `TryGetValue`) outside the lock. |
| Memory overhead of extra queues | Each entry in `_expirationQueue` and `_lruQueue` is a reference (string key). For $10^6$ items, this is roughly 16-24MB extra, which is acceptable for a high-performance cache. |

## Sources & References

- **Origin document:** [docs/todos/001-pending-p1-optimize-inmemorycache-maintenance-algorithm.md](docs/todos/001-pending-p1-optimize-inmemorycache-maintenance-algorithm.md)
- Related code: [src/Headless.Caching.Memory/InMemoryCache.cs](src/Headless.Caching.Memory/InMemoryCache.cs)
- Related PRs/issues: #001
