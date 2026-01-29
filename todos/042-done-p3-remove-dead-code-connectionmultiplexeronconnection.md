---
status: done
priority: p3
issue_id: "042"
tags: ["code-review","quality","dotnet"]
dependencies: []
---

# Remove dead code: _ConnectionMultiplexerOnConnectionFailed handler

## Problem Statement

The _ConnectionMultiplexerOnConnectionFailed event handler only logs a warning message. StackExchange.Redis already logs connection failures internally, making this redundant. The handler provides no recovery mechanism.

## Findings

- **Location:** src/Headless.Caching.Redis/RedisCache.cs:1134-1137
- **Code:** Only logs: _logger.LogWarning("Redis connection failed: {FailureType}", e.FailureType)
- **Discovered by:** code-simplicity-reviewer agent

## Proposed Solutions

### Option 1: Remove the handler entirely
- **Pros**: Less code, no redundant logging
- **Cons**: None - StackExchange.Redis logs this already
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Remove the handler and its subscription/unsubscription.

## Acceptance Criteria

- [ ] _ConnectionMultiplexerOnConnectionFailed method removed
- [ ] Event subscription/unsubscription removed from _LoadScriptsAsync and Dispose
- [ ] Keep _ConnectionMultiplexerOnConnectionRestored as it serves a purpose (resetting _scriptsLoaded)

## Notes

If logging is needed for debugging, consider using StackExchange.Redis's TextWriter-based logging instead.

## Work Log

### 2026-01-29 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-01-29 - Completed

**By:** Agent
**Actions:**
- Status changed: pending → done
