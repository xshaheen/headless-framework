---
status: done
priority: p3
issue_id: "043"
tags: ["code-review","architecture","dotnet"]
dependencies: []
---

# Add HeadlessRedisScriptsLoader.ResetScripts() for connection restoration

## Problem Statement

If RedisCache is refactored to use HeadlessRedisScriptsLoader, the connection restoration handling needs to reset the shared loader's script state. Currently only RedisCache handles ConnectionRestored events and resets its private _scriptsLoaded flag.

## Findings

- **Location:** src/Headless.Redis/HeadlessRedisScriptsLoader.cs
- **Missing:** Public method to reset _scriptsLoaded flag for reconnection scenarios
- **Related to:** P1 todo for RedisCache refactor
- **Discovered by:** architecture-strategist, code-simplicity-reviewer agents

## Proposed Solutions

### Option 1: Add public ResetScripts() method
- **Pros**: Simple, consumers can call on reconnection
- **Cons**: Consumers must remember to call it
- **Effort**: Small
- **Risk**: Low

### Option 2: HeadlessRedisScriptsLoader subscribes to connection events
- **Pros**: Automatic handling, consumers don't need to manage
- **Cons**: Needs IConnectionMultiplexer in constructor (already has it)
- **Effort**: Medium
- **Risk**: Low


## Recommended Action

Option 2 - Have HeadlessRedisScriptsLoader handle reconnection internally. Centralized handling is cleaner.

## Acceptance Criteria

- [ ] HeadlessRedisScriptsLoader subscribes to ConnectionRestored event
- [ ] On reconnection, _scriptsLoaded is reset to false
- [ ] HeadlessRedisScriptsLoader implements IDisposable to unsubscribe
- [ ] Consumers (RedisCache, RedisResourceLockStorage) don't need individual handling

## Notes

This is a prerequisite for the P1 RedisCache refactor todo. Implement this first.

## Work Log

### 2026-01-29 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-01-29 - Completed

**By:** Agent
**Actions:**
- Status changed: pending → done
