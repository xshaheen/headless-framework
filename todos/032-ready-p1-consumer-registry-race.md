---
status: done
priority: p1
issue_id: "032"
tags: []
dependencies: []
---

# consumer-registry-race

## Problem Statement

ConsumerRegistry.Register() has race between _frozen null check and _consumers assignment. Thread A can pass null check while Thread B freezes, causing NullReferenceException.

## Findings

- **Status:** Resolved
- **Priority:** p1

## Proposed Solutions

### Option 1: Lock-based synchronization
- **Pros**: Simple, correct, no contention in config phase
- **Cons**: Minimal overhead for lock acquisition
- **Effort**: Small
- **Risk**: Low

## Recommended Action

Use System.Threading.Lock for synchronization across Register, Update, and GetAll methods.

## Acceptance Criteria
- [x] Add lock around freeze check and registration
- [x] Use lock or Interlocked for atomic freeze+check
- [x] Add concurrent registration stress tests
- [x] No NullReferenceException under load
- [x] Thread-safe freeze behavior verified

## Notes

Source: Workflow automation

## Work Log

### 2026-01-20 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create

### 2026-01-20 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready

### 2026-01-21 - Resolved

**By:** Claude Code
**Actions:**
- Added System.Threading.Lock field to ConsumerRegistry
- Wrapped Register(), Update(), and GetAll() methods with lock statements
- Used double-checked locking pattern in GetAll() for performance
- Added 2 comprehensive stress tests with Barrier for true concurrent execution
- All 19 ConsumerRegistryTests pass
- Status changed: ready → done
