# Minor Race Condition in ResetEventWithRefCount Cleanup

---
status: pending
priority: p3
issue_id: "002"
tags: [code-review, dotnet, concurrency, performance]
dependencies: []
---

## Problem Statement

In `_DecrementResetEvent`, there's a theoretical race condition between checking `Decrement() == 0` and calling `TryRemove`. Another thread could increment the ref count after the check but before removal.

## Findings

**Location:** [ResourceLockProvider.cs:181-191](src/Framework.ResourceLocks.Core/RegularLocks/ResourceLockProvider.cs#L181-L191)

```csharp
private void _DecrementResetEvent(ResetEventWithRefCount? autoResetEvent, string resource)
{
    if (autoResetEvent is not null
        && _autoResetEvents.TryGetValue(resource, out var exist)
        && exist == autoResetEvent
        && autoResetEvent.Decrement() == 0)  // Race: after this check...
    {
        _autoResetEvents.TryRemove(resource, out _);  // ...another thread could increment
    }
}
```

**Identified by:** Security Sentinel, Performance Oracle

**Practical Impact:** Low. Worst case is:
1. A newly incremented entry gets removed (waiters briefly lose their event)
2. Next iteration creates a new event
3. Small performance penalty, no correctness issue

## Proposed Solutions

### Option A: Accept as-is (RECOMMENDED)

The identity check `exist == autoResetEvent` and the low-impact nature makes this acceptable.

**Pros:** No change needed
**Cons:** Theoretical race remains
**Effort:** None
**Risk:** None

### Option B: Use atomic TryRemove with value check

```csharp
_autoResetEvents.TryRemove(new KeyValuePair<string, ResetEventWithRefCount>(resource, autoResetEvent));
```

**Pros:** Atomic removal, prevents removing wrong entry
**Cons:** Still doesn't prevent the race entirely
**Effort:** Small
**Risk:** Low

### Option C: Add lock around decrement+remove

**Pros:** Eliminates race
**Cons:** Adds contention, over-engineering for low-impact issue
**Effort:** Medium
**Risk:** Low

## Recommended Action

(To be filled during triage - likely Accept as-is)

## Technical Details

- **Affected files:** `ResourceLockProvider.cs`
- **Components:** ResourceLocks.Core

## Acceptance Criteria

- [ ] Decision documented
- [ ] No regression in concurrent lock tests

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-10 | Created | Code review finding |

## Resources

- PR #138: https://github.com/xshaheen/headless-framework/pull/138
