---
status: pending
priority: p1
issue_id: "003"
tags: [code-review, security, data-integrity, entity-framework, concurrency]
dependencies: []
---

# Concurrency Stamp OriginalValue Override Defeats Optimistic Locking

## Problem Statement

The `_TryUpdateConcurrencyStamp` method modifies `OriginalValue` if it differs from the entity's current value. This breaks optimistic concurrency by allowing stale overwrites when the stamp is manually set.

**Why it matters:** Optimistic concurrency is a critical data integrity mechanism. This bug allows silent data loss when concurrent updates occur.

## Findings

### Location
- **File:** `src/Framework.Orm.EntityFramework/Contexts/HeadlessEntityModelProcessor.cs`
- **Lines:** 546-561

### Evidence
```csharp
private static void _TryUpdateConcurrencyStamp(EntityEntry entry)
{
    if (entry.Entity is not IHasConcurrencyStamp entity)
        return;

    var propertyEntry = entry.Property(nameof(IHasConcurrencyStamp.ConcurrencyStamp));

    // THIS LINE DEFEATS CONCURRENCY CHECK:
    if (!string.Equals(propertyEntry.OriginalValue as string, entity.ConcurrencyStamp, StringComparison.Ordinal))
    {
        propertyEntry.OriginalValue = entity.ConcurrencyStamp;  // <-- Overwrites original!
    }

    ObjectPropertiesHelper.TrySetProperty(entity, x => x.ConcurrencyStamp, () => Guid.NewGuid().ToString("N"));
}
```

### Attack Scenario
1. Entity loaded with stamp "A"
2. Another process updates entity, stamp becomes "B"
3. First process manually sets `entity.ConcurrencyStamp = "B"` (to "fake" having latest version)
4. Code detects mismatch, sets `OriginalValue = "B"`
5. Code generates new stamp "C"
6. EF executes `UPDATE ... WHERE ConcurrencyStamp = 'B'` - succeeds!
7. Concurrent change silently overwritten

## Proposed Solutions

### Option 1: Remove OriginalValue override (Recommended)
Remove lines 555-558. Let EF Core handle concurrency naturally.

```csharp
private static void _TryUpdateConcurrencyStamp(EntityEntry entry)
{
    if (entry.Entity is not IHasConcurrencyStamp entity)
        return;

    // Only generate new stamp, don't modify OriginalValue
    ObjectPropertiesHelper.TrySetProperty(entity, x => x.ConcurrencyStamp, () => Guid.NewGuid().ToString("N"));
}
```

**Pros:** Correct concurrency behavior, simplest fix
**Cons:** May break existing code that relies on this behavior
**Effort:** Small
**Risk:** Medium - need to verify no intentional use cases

### Option 2: Add explicit flag for bypass
```csharp
entity.BypassConcurrencyCheck = true; // Explicit opt-in
```

**Pros:** Preserves backward compatibility
**Cons:** Adds complexity, still allows bypass
**Effort:** Medium
**Risk:** Low

## Recommended Action
<!-- To be filled during triage -->

## Technical Details

### Affected Files
- `src/Framework.Orm.EntityFramework/Contexts/HeadlessEntityModelProcessor.cs`

### Affected Components
- All entities implementing `IHasConcurrencyStamp`
- Concurrent update scenarios

### Database Changes Required
None

## Acceptance Criteria
- [ ] Concurrency conflicts properly detected and thrown as `DbUpdateConcurrencyException`
- [ ] Tests verify concurrent updates are rejected
- [ ] No silent data overwrites possible

## Work Log
| Date | Action | Learnings |
|------|--------|-----------|
| 2026-01-12 | Identified during data integrity review | Never modify OriginalValue for concurrency tokens |

## Resources
- EF Core Concurrency: https://learn.microsoft.com/en-us/ef/core/saving/concurrency
