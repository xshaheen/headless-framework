---
status: done
priority: p2
issue_id: "011"
tags: ["code-review","performance","dotnet"]
dependencies: []
---

# N+1 Redis reads and redundant DB queries in dashboard repositories

## Problem Statement

Three performance issues: (1) GetDeadNodesAsync does 1+N sequential Redis reads (1 for registry, N for individual heartbeats). (2) _RemoveNodesFromRegistryAsync re-reads registry that caller already fetched. (3) GetCronJobsOccurrencesGraphDataAsync makes 3 sequential DB queries for past/today/future instead of 1 query + in-memory split.

## Findings

- **N+1 Redis:** src/Headless.Jobs.Caching.Redis/JobsRedisContext.cs:44-74
- **Redundant read:** src/Headless.Jobs.Caching.Redis/JobsRedisContext.cs:76-91
- **3x DB query:** src/Headless.Jobs.Dashboard/Infrastructure/Dashboard/JobsDashboardRepository.cs:492-543
- **Discovered by:** performance-oracle, pragmatic-dotnet-reviewer

## Proposed Solutions

### Parallelize Redis reads + pass registry from caller + merge DB queries
- **Pros**: Reduces latency proportionally to node count
- **Cons**: Slightly more complex code
- **Effort**: Medium
- **Risk**: Low


## Recommended Action

Use Task.WhenAll for Redis reads. Pass nodesList from caller. Fetch once without date filter then split in memory.

## Acceptance Criteria

- [ ] Redis heartbeat checks run concurrently
- [ ] No redundant registry re-reads
- [ ] Graph data uses single DB query

## Notes

Source: Code review

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
