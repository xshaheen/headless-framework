---
status: pending
priority: p3
issue_id: "010"
tags: [code-review, security, data-grid]
dependencies: []
---

# Dynamic OrderBy Without Property Whitelist

## Problem Statement

The `OrderExtensions` allows ordering by any property name via string without validation. This could allow access to navigation properties that shouldn't be exposed.

## Findings

### Location
- **File:** `src/Framework.Orm.EntityFramework/DataGrid/Ordering/OrderExtensions.cs`
- **Lines:** 97-142

### Evidence
```csharp
body = propertyName
    .Split('.')
    .Aggregate<string, Expression>(parameterExpression, Expression.PropertyOrField);
```

### Security Risk
- Allows access to navigation properties via dot notation (e.g., `"User.PasswordHash"`)
- No whitelist validation of allowed properties
- Could potentially expose sensitive nested properties through sorting-based side-channel attacks

## Proposed Solutions

### Option 1: Add property whitelist mechanism
```csharp
public interface ISortableEntity
{
    static abstract IReadOnlySet<string> AllowedSortProperties { get; }
}
```

**Pros:** Explicit control over sortable properties
**Cons:** Requires entity modification
**Effort:** Medium
**Risk:** Low

### Option 2: Reject navigation property access by default
Only allow direct properties, not dot-separated paths.

**Pros:** Simple fix
**Cons:** May break legitimate use cases
**Effort:** Small
**Risk:** Medium

### Option 3: Add depth limit
Limit nested property access to 1 level.

**Pros:** Reduces attack surface
**Cons:** Arbitrary limit
**Effort:** Small
**Risk:** Low

## Recommended Action
<!-- To be filled during triage -->

## Technical Details

### Affected Files
- `src/Framework.Orm.EntityFramework/DataGrid/Ordering/OrderExtensions.cs`

### Affected Components
- DataGrid sorting functionality

### Database Changes Required
None

## Acceptance Criteria
- [ ] Property access is validated against whitelist or restricted
- [ ] Sensitive properties cannot be accessed via sorting
- [ ] Existing legitimate use cases preserved

## Work Log
| Date | Action | Learnings |
|------|--------|-----------|
| 2026-01-12 | Identified during security review | User input property names need validation |

## Resources
- OWASP Input Validation
