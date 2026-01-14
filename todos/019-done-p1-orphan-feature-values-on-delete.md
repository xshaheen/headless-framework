---
status: done
priority: p1
issue_id: "019"
tags: [code-review, data-integrity, features]
dependencies: []
---

# Clean Up Orphan FeatureValueRecords When Features Deleted

## Problem Statement

When feature definitions are deleted from the database (via `DeletedFeatures` or `DeletedFeatureGroups` lists), the associated `FeatureValueRecord` entries remain orphaned in the database. There is no cascade delete or cleanup logic.

## Findings

**DynamicFeatureDefinitionStore._UpdateChangedFeaturesAsync (lines 420-432):**
```csharp
if (_providers.DeletedFeatures.Count != 0)
{
    deletedRecords.AddRange(dbRecordsMap.Values.Where(x =>
        _providers.DeletedFeatures.Contains(x.Name)));
}
```

- Deletes `FeatureDefinitionRecord` but NOT associated `FeatureValueRecord`
- No FK constraint between tables (Name references Name conceptually)
- No cleanup job exists

**Corruption scenario:**
1. Feature "PremiumFeature" defined with tenant overrides
2. Feature removed from code (added to DeletedFeatures)
3. FeatureDefinitionRecord deleted
4. FeatureValueRecord entries with Name="PremiumFeature" remain
5. Data accumulates over time
6. If feature name reused, old values unexpectedly resurrect

## Proposed Solutions

### Option 1: Cascade Delete in SaveAsync

**Approach:** When deleting feature definitions, also delete associated feature values.

**Pros:**
- Immediate cleanup
- Transactional with definition delete

**Cons:**
- May need to delete many records
- Performance impact during sync

**Effort:** 2-3 hours

**Risk:** Low

---

### Option 2: Add FK Constraint + Cascade

**Approach:** Add database FK from FeatureValueRecord.Name to FeatureDefinitionRecord.Name with ON DELETE CASCADE.

**Pros:**
- Database-level enforcement
- Automatic cleanup

**Cons:**
- Requires migration
- FK on string column (not ideal)
- May break if feature definitions stored separately

**Effort:** 3-4 hours

**Risk:** Medium (migration)

---

### Option 3: Background Cleanup Job

**Approach:** Periodic job that finds orphaned FeatureValueRecords and deletes them.

**Pros:**
- No blocking during sync
- Handles historical orphans

**Cons:**
- Delayed cleanup
- More infrastructure

**Effort:** 4-6 hours

**Risk:** Low

## Recommended Action

*To be filled during triage.*

## Technical Details

**Affected files:**
- `src/Framework.Features.Core/Definitions/DynamicFeatureDefinitionStore.cs:420-432`
- `src/Framework.Features.Storage.EntityFramework/EfFeatureDefinitionRecordRepository.cs`
- `src/Framework.Features.Core/Repositories/IFeatureValueRecordRepository.cs` - may need new method

## Acceptance Criteria

- [x] Deleting feature definition also cleans up associated values
- [x] No orphan FeatureValueRecords remain after feature deletion
- [x] Test verifies cleanup behavior
- [x] Consider background job for historical data cleanup - not needed, transactional cleanup sufficient

## Work Log

### 2026-01-14 - Initial Discovery

**By:** Claude Code

**Actions:**
- Identified orphan data accumulation pattern
- Analyzed schema for FK constraints (none exist)
- Reviewed deletion flow in DynamicFeatureDefinitionStore

**Learnings:**
- Loose coupling between definitions and values is intentional but needs cleanup
- Similar pattern exists in permission system (should check for same issue)

### 2026-01-14 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready

### 2026-01-14 - Completed

**By:** Agent
**Actions:**
- Status changed: ready → done
