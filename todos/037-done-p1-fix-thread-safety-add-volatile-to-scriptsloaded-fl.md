---
status: done
priority: p1
issue_id: "037"
tags: ["code-review","security","dotnet","concurrency"]
dependencies: []
---

# Fix thread safety: add volatile to _scriptsLoaded flag

## Problem Statement

The _scriptsLoaded boolean at line 29 is read outside the lock (line 1086) and written from an event handler thread (line 1130). Without volatile, CPU/compiler reordering can cause stale reads. Race scenario: Thread A caches _scriptsLoaded=true, event handler sets it to false, Thread A still sees true and skips reload, causing NOSCRIPT errors.

## Findings

- **Location:** src/Headless.Caching.Redis/RedisCache.cs:29, 1086, 1130
- **Risk:** High - can cause NOSCRIPT errors after Redis reconnection under concurrent load
- **Discovered by:** strict-dotnet-reviewer, performance-oracle agents

## Proposed Solutions

### Option 1: Add volatile modifier
- **Pros**: Simple one-line fix, ensures memory visibility
- **Cons**: None
- **Effort**: Small
- **Risk**: Low

### Option 2: Use Volatile.Read/Write explicitly
- **Pros**: More explicit about intent
- **Cons**: More verbose
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Option 1 - Add volatile. Simple and effective.

## Acceptance Criteria

- [ ] _scriptsLoaded declared as volatile bool
- [ ] Same fix applied to _isCluster and _supportsMsetEx if they remain
- [ ] No regression in existing tests

## Notes

The same issue exists in HeadlessRedisScriptsLoader - consider fixing there too for consistency.

## Work Log

### 2026-01-29 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-01-29 - Completed

**By:** Agent
**Actions:**
- Status changed: pending → done
