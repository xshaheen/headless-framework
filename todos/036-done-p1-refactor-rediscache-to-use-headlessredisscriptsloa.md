---
status: done
priority: p1
issue_id: "036"
tags: ["code-review","architecture","performance","dotnet"]
dependencies: []
---

# Refactor RedisCache to use HeadlessRedisScriptsLoader instead of duplicating script loading

## Problem Statement

RedisCache._LoadScriptsAsync() (lines 1084-1125) duplicates the exact same logic that exists in HeadlessRedisScriptsLoader.LoadScriptsAsync(). The project already has a reference to Headless.Redis but doesn't use it. This causes: (1) DRY violation with 52 lines of duplicated code, (2) Inconsistent behavior - HeadlessRedisScriptsLoader loads scripts in parallel with Task.WhenAll while RedisCache loads sequentially (80% slower), (3) Bug divergence risk - fixes in one location won't propagate, (4) Memory waste if both caching and locks are used together.

## Findings

- **Location:** src/Headless.Caching.Redis/RedisCache.cs:1084-1125
- **Duplicate of:** src/Headless.Redis/HeadlessRedisScriptsLoader.cs:30-102
- **Performance impact:** 5 sequential RTTs vs 1 parallel RTT per server (~80% slower cold start)
- **Discovered by:** pattern-recognition-specialist, architecture-strategist, performance-oracle agents
- **Git context:** Duplication introduced in commit bd270947 during Foundatio replacement

## Proposed Solutions

### Option 1: Inject HeadlessRedisScriptsLoader
- **Pros**: Single source of truth, parallel loading, consistent with RedisResourceLockStorage pattern, reduces code by ~52 LOC
- **Cons**: Requires adding parameter to constructor, need to handle connection events centrally
- **Effort**: Medium
- **Risk**: Low

### Option 2: Parallelize existing _LoadScriptsAsync
- **Pros**: Minimal code changes, fixes performance issue
- **Cons**: Still maintains duplication, doesn't address architectural concern
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Option 1 - Inject HeadlessRedisScriptsLoader. This follows the established pattern from DistributedLocks.Redis and eliminates duplication entirely.

## Acceptance Criteria

- [ ] RedisCache constructor accepts HeadlessRedisScriptsLoader parameter
- [ ] Setup.cs registers HeadlessRedisScriptsLoader as singleton
- [ ] _LoadScriptsAsync method removed from RedisCache
- [ ] 5 private LoadedLuaScript fields removed
- [ ] _scriptsLoaded flag removed
- [ ] All existing tests pass
- [ ] Cold start performance improved (parallel script loading)

## Notes

HeadlessRedisScriptsLoader may need a ResetScripts() method added for connection restoration handling. Consider moving connection event subscription to the shared loader.

## Work Log

### 2026-01-29 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-01-29 - Completed

**By:** Agent
**Actions:**
- Status changed: pending → done
