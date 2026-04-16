---
status: pending
priority: p2
issue_id: "006"
tags: [performance, distributed-locks, redis]
dependencies: []
---

# Batch GetExpirationAsync in CacheDistributedLockStorage.GetAllWithExpirationByPrefixAsync

## Problem Statement

`CacheDistributedLockStorage.GetAllWithExpirationByPrefixAsync` issues N+1 sequential `GetExpirationAsync` calls — one per lock entry returned by `GetAllByPrefixAsync`. Under high lock counts this becomes a latency bottleneck.

## Findings

- **File:** `src/Headless.DistributedLocks.Caching/CacheDistributedLockStorage.cs`
- **Status:** Identified during PR #216 code review
- **Priority:** p2

## Proposed Solutions

1. **Pipeline TTL lookups**: batch `GetExpirationAsync` calls using Redis pipelining or `Task.WhenAll` to reduce round-trips.
2. **Add `GetAllWithExpirationByPrefixAsync` to ICache**: introduce a cache-level method that retrieves values and TTLs together, avoiding the N+1 pattern entirely.

## Recommended Action

Option 2 is cleaner long-term but requires an ICache interface addition. Option 1 is a quick win.

## Acceptance Criteria

- [ ] No N+1 sequential TTL queries for lock enumeration
- [ ] Performance test demonstrating improvement with 50+ locks
