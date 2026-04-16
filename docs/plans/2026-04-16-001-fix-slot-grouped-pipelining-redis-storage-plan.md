---
title: "fix: Implement cluster-safe pipelining in RedisDistributedLockStorage"
type: fix
status: active
date: 2026-04-16
origin: docs/todos/002-pending-p2-implement-slot-grouped-pipelining-for-redis-cluste.md
---

# fix: Implement cluster-safe pipelining in RedisDistributedLockStorage

## Overview

Refactor `RedisDistributedLockStorage` to handle Redis Cluster environments correctly by replacing node-specific `IBatch` usage with standard asynchronous pipelining.

## Problem Frame

`RedisDistributedLockStorage._ProcessBatchWithExpirationAsync` uses `IDatabase.CreateBatch()` to pipeline `GET` and `TTL` commands. In a Redis Cluster, `IBatch` is tied to a single connection/node. If the batch contains keys belonging to different hash slots (which reside on different nodes), the operation will fail because the commands cannot be routed correctly.

Similarly, `_ProcessBatchAsync` uses a multi-key `StringGetAsync` call which can be risky in some sharded configurations. Standardizing on individual async tasks allows the `ConnectionMultiplexer` to handle routing naturally for each key.

## Requirements Trace

- R1. Ensure all batch retrieval operations work correctly in any Redis topology (standalone, sentinel, cluster).
- R2. Maintain high performance by preserving pipelining behavior.
- R3. Eliminate node-specific `IBatch` dependencies in distributed lock storage.

## Scope Boundaries

- This plan affects `RedisDistributedLockStorage.cs`.
- `RedisThrottlingDistributedLockStorage.cs` is already cluster-safe.

## Context & Research

### Relevant Code and Patterns

- `src/Headless.DistributedLocks.Redis/RedisDistributedLockStorage.cs`: The target for the fix.

### Institutional Learnings

- `StackExchange.Redis` pipelines all asynchronous commands by default. `IBatch` is an optimization for specific node-local ordering/batching that often introduces more risk than benefit in modern .NET/Redis environments.

## Key Technical Decisions

- **Remove IBatch**: Replace the manual batch creation with a task-parallel approach.
- **ConcurrentUser Routing**: Use individual `GET` and `TTL` async calls. The `ConnectionMultiplexer` will automatically route each task to the correct node based on its key's hash slot.
- **Fire-then-Await**: Ensure all tasks are launched into the runtime before any are awaited. This preserves the network-level pipelining that makes Redis operations fast.

## Open Questions

### Resolved During Planning

- **Is slot grouping necessary?**: No. For individual `GET`/`TTL` calls, the library handles routing transparently. Explicit grouping adds unjustified complexity.
- **Does StringGetAsync(keys) handle clusters?**: Yes, but using individual tasks is more consistent with the `GET`+`TTL` pattern we need and avoids any ambiguity about library-level sharding behavior.

### Deferred to Implementation

- None.

## Implementation Units

- [ ] **Unit 1: Refactor Batch Processing Logic**

**Goal:** Standardize on cluster-safe async pipelining for both batch processing methods.

**Requirements:** R1, R2, R3

**Files:**
- Modify: `src/Headless.DistributedLocks.Redis/RedisDistributedLockStorage.cs`

**Approach:**
- **Refactor `_ProcessBatchWithExpirationAsync`**:
  - Remove `Db.CreateBatch()`.
  - Create a list of tuples to track `(Key, Task<RedisValue>, Task<TimeSpan?>)`.
  - Iterate through the batch, firing `StringGetAsync` and `KeyTimeToLiveAsync` for each key.
  - Await all tasks using `Task.WhenAll`.
  - Aggregate results into the shared `result` dictionary.
- **Refactor `_ProcessBatchAsync`**:
  - Replace `Db.StringGetAsync(keyArray)` with individual tasks per key.
  - Await all with `Task.WhenAll`.
  - Aggregate results.

**Test scenarios:**
- Happy path: Multiple keys in different slots are correctly fetched (with and without TTLs).
- Edge case: Empty batch.
- Error path: One node fails in a cluster (exception should propagate).

**Verification:**
- `GetAllByPrefixAsync` and `GetAllWithExpirationByPrefixAsync` return correct data in a cluster.

## System-Wide Impact

- **Performance:** Slight increase in task allocations, but significant improvement in reliability and routing correctness in clustered environments.
- **Unchanged invariants:** The public API and functional behavior remain identical.

## Risks & Dependencies

| Risk | Mitigation |
|------|------------|
| Task proliferation | Batch size is 1000, so max 2000 tasks are fired. This is well within the capacity of the .NET ThreadPool and the multiplexer pipeline. |

## Sources & References

- Related Todo: `docs/todos/002-pending-p2-implement-slot-grouped-pipelining-for-redis-cluste.md`
