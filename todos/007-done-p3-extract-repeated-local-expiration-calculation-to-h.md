---
status: done
priority: p3
issue_id: "007"
tags: ["code-review","quality","dotnet"]
dependencies: []
---

# Extract repeated local expiration calculation to helper

## Problem Statement

The expression `_options.DefaultLocalExpiration ?? expiration` is repeated 18 times throughout HybridCache.cs. This violates DRY and makes maintenance harder.

## Findings

- **Occurrences:** 18 times in HybridCache.cs
- **Lines:** 151, 171, 191, 219, 280, 388, 424, 470, 512, 541, 586, 631, 671, 707, 747, 786, 825, 860
- **Discovered by:** code-simplicity-reviewer

## Proposed Solutions

### Option 1: Extract to private helper method
- **Pros**: DRY, single point of change
- **Cons**: Slight overhead (likely inlined)
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Extract to `private TimeSpan? _GetLocalExpiration(TimeSpan? expiration) => _options.DefaultLocalExpiration ?? expiration;`

## Acceptance Criteria

- [ ] Helper method created
- [ ] All 18 occurrences replaced
- [ ] Tests still pass

## Notes

Simple DRY improvement for maintainability.

## Work Log

### 2026-02-04 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-02-04 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready

### 2026-02-04 - Completed

**By:** Agent
**Actions:**
- Status changed: in-progress → done
