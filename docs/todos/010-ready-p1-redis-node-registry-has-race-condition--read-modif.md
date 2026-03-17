---
status: ready
priority: p1
issue_id: "010"
tags: ["code-review","correctness","concurrency","dotnet"]
dependencies: []
---

# Redis node registry has race condition — read-modify-write without atomicity

## Problem Statement

_AddNodeToRegistryAsync and _RemoveNodesFromRegistryAsync both read 'nodes:registry' from Redis, modify the list in memory, then write back. In multi-node deployments, concurrent heartbeats from different nodes will clobber each other's writes. Last write wins, causing ghost nodes or missing nodes.

## Findings

- **Location:** src/Headless.Jobs.Caching.Redis/JobsRedisContext.cs:76-108
- **Risk:** High — data corruption in multi-node production deployments
- **Discovered by:** pragmatic-dotnet-reviewer

## Proposed Solutions

### Use Redis Set (SADD/SREM) instead of JSON list
- **Pros**: Inherently atomic, purpose-built for this use case
- **Cons**: Requires StackExchange.Redis IDatabase, not IDistributedCache
- **Effort**: Medium
- **Risk**: Low

### Use Redis WATCH/MULTI/EXEC for optimistic locking
- **Pros**: Works with existing IDistributedCache abstraction
- **Cons**: More complex, retry logic needed
- **Effort**: Medium
- **Risk**: Medium


## Recommended Action

Use Redis Set (SADD/SREM) for atomic add/remove operations.

## Acceptance Criteria

- [ ] Node registry operations are atomic
- [ ] Concurrent heartbeats from multiple nodes don't corrupt registry
- [ ] Integration test with concurrent registration

## Notes

The redundant re-read in _RemoveNodesFromRegistryAsync (line 76) compounds the problem.

## Work Log

### 2026-03-17 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-03-17 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready
