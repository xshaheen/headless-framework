---
status: done
priority: p1
issue_id: "013"
tags: ["code-review","correctness","dotnet"]
dependencies: []
---

# static fields on generic JobsInMemoryPersistenceProvider cause test isolation failure

## Problem Statement

ConcurrentDictionary fields are static on a generic class. While each closed generic type gets its own statics, the fields are process-global — not per-DI-container. Tests that rebuild the DI container see stale state from previous tests. The class is also not sealed per project convention.

## Findings

- **Location:** src/Headless.Jobs.Core/Src/Provider/JobsInMemoryPersistenceProvider.cs:16-27
- **Risk:** High — test isolation failures, stale state on container rebuild
- **Discovered by:** strict-dotnet-reviewer

## Proposed Solutions

### Make fields instance (non-static)
- **Pros**: Class is registered as Singleton, so no allocation concern. Proper DI scoping.
- **Cons**: None
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Remove static keyword from all 4 ConcurrentDictionary fields. Also seal the class.

## Acceptance Criteria

- [ ] Fields are instance, not static
- [ ] Class is sealed
- [ ] Tests with multiple DI containers have isolated state

## Notes

Also fix: replace new(new Dictionary<>()) with new() on all 4 fields (unnecessary allocation).

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
