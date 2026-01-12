---
status: pending
priority: p2
issue_id: "005"
tags: [code-review, bug, dotnet, entity-framework]
dependencies: []
---

# Duplicate TryConfigureDeleteAudit Call

## Problem Statement

`TryConfigureDeleteAudit()` is called twice in `ConfigureFrameworkConvention()`. This is a copy-paste bug that wastes cycles during model building.

## Findings

### Location
- **File:** `src/Framework.Orm.EntityFramework/Extensions/EntityTypeBuilderExtensions.cs`
- **Lines:** 69, 73

### Evidence
```csharp
public static void ConfigureFrameworkConvention(this EntityTypeBuilder builder)
{
    builder.TryConfigureConcurrencyStamp();
    builder.TryConfigureExtraProperties();
    builder.TryConfigureDeleteAudit();      // <-- First call (line 69)
    builder.TryConfigureCreateAudit();
    builder.TryConfigureUpdateAudit();
    builder.TryConfigureSuspendAudit();
    builder.TryConfigureDeleteAudit();      // <-- Duplicate call (line 73)
}
```

## Proposed Solutions

### Option 1: Remove duplicate line (Recommended)
Simply delete line 73.

**Pros:** Simple fix
**Cons:** None
**Effort:** Trivial
**Risk:** None (idempotent method)

## Recommended Action
<!-- To be filled during triage -->

## Technical Details

### Affected Files
- `src/Framework.Orm.EntityFramework/Extensions/EntityTypeBuilderExtensions.cs`

### Affected Components
- Model building performance (minor)

### Database Changes Required
None

## Acceptance Criteria
- [ ] Only one call to TryConfigureDeleteAudit
- [ ] All tests pass

## Work Log
| Date | Action | Learnings |
|------|--------|-----------|
| 2026-01-12 | Identified during code review | Review copy-paste code carefully |

## Resources
- N/A
