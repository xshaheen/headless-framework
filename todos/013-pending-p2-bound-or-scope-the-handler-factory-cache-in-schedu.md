---
status: pending
priority: p2
issue_id: "013"
tags: ["performance","code-review","scheduling"]
dependencies: []
---

# Bound or scope the handler factory cache in ScheduledJobDispatcher

## Problem Statement

ScheduledJobDispatcher uses a static ConcurrentDictionary _FactoryCache that grows unboundedly. For one-time (fire-and-forget) jobs with unique names, this leaks memory. Being static also causes test pollution across test classes.

## Findings

- **Location:** src/Headless.Messaging.Core/Scheduling/ScheduledJobDispatcher.cs
- **Risk:** Medium - memory leak for one-time jobs; test pollution
- **Reviewer:** performance-oracle, architecture-strategist

## Proposed Solutions

### Make cache instance-level (injected as singleton)
- **Pros**: Fixes test pollution; scoped lifetime
- **Cons**: Slightly different lifecycle
- **Effort**: Small
- **Risk**: Low

### Add bounded cache with LRU eviction
- **Pros**: Prevents unbounded growth
- **Cons**: More complex
- **Effort**: Medium
- **Risk**: Low


## Recommended Action

Make _FactoryCache instance-level. ScheduledJobDispatcher is registered as singleton via DI, so cache lifetime is naturally bounded.

## Acceptance Criteria

- [ ] Factory cache is instance-level, not static
- [ ] One-time job names don't cause unbounded growth

## Notes

PR #170 code review finding.

## Work Log

### 2026-02-08 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
