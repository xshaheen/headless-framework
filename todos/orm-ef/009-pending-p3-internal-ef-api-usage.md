---
status: pending
priority: p3
issue_id: "009"
tags: [code-review, maintenance, entity-framework]
dependencies: []
---

# Internal EF Core API Usage Creates Upgrade Fragility

## Problem Statement

The codebase uses internal EF Core APIs (`GetDependencies()`, `StateManager`, `CoreAnnotationNames`, etc.) which may change without notice in EF Core updates.

## Findings

### Locations
1. **File:** `src/Framework.Orm.EntityFramework/ChangeTrackers/HeadlessEntityFrameworkNavigationModifiedTracker.cs`
   - **Lines:** 124-229
   ```csharp
   #pragma warning disable EF1001 // Internal EF Core API usage.
   var stateManager = entry.Context.GetDependencies().StateManager;
   ```

2. **File:** `src/Framework.Orm.EntityFramework/Extensions/EntityTypeBuilderExtensions.cs`
   - **Lines:** 29-31, 48-50
   ```csharp
   #pragma warning disable EF1001 // Is an internal API
   var queryFilterAnnotation = builder.Metadata.FindAnnotation(CoreAnnotationNames.QueryFilter);
   ```

### Risk
- EF Core minor version updates may break this code
- No compilation warning when internal APIs change

## Proposed Solutions

### Option 1: Abstract behind facade (Recommended)
```csharp
internal interface IEfInternalAccessor
{
    IStateManager GetStateManager(DbContext context);
    object? GetQueryFilterAnnotation(EntityTypeBuilder builder);
}
```

**Pros:** Isolates internal API usage to one place
**Cons:** Additional abstraction layer
**Effort:** Medium
**Risk:** Low

### Option 2: Document and add integration tests
Add tests that fail if internal APIs change signatures.

**Pros:** Early detection of breaking changes
**Cons:** Doesn't prevent the issue
**Effort:** Small
**Risk:** Low

### Option 3: Explore public alternatives
Research if EF Core has added public APIs for these scenarios.

**Pros:** Long-term stability
**Cons:** May not exist
**Effort:** Research
**Risk:** None

## Recommended Action
<!-- To be filled during triage -->

## Technical Details

### Affected Files
- `src/Framework.Orm.EntityFramework/ChangeTrackers/HeadlessEntityFrameworkNavigationModifiedTracker.cs`
- `src/Framework.Orm.EntityFramework/Extensions/EntityTypeBuilderExtensions.cs`

### Affected Components
- Navigation tracking
- Query filter composition

### Database Changes Required
None

## Acceptance Criteria
- [ ] Internal API usage documented with EF Core version compatibility
- [ ] Integration tests detect API changes
- [ ] Upgrade path clear when EF Core updates

## Work Log
| Date | Action | Learnings |
|------|--------|-----------|
| 2026-01-12 | Identified during code review | Internal APIs need isolation layer |

## Resources
- EF Core GitHub Issues
- EF Core roadmap for public API additions
