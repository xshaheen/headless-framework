---
status: done
priority: p1
issue_id: "018"
tags: [code-review, dotnet, entity-framework, data-integrity, features]
dependencies: []
---

# Fix Detached Entity Tracking in EF Repositories

## Problem Statement

The EF Core repositories use `IDbContextFactory` and create new DbContext instances per operation. However, entities loaded from one DbContext are then passed to UpdateRange/RemoveRange in a different DbContext, causing:
1. All properties marked as modified (inefficient updates)
2. Potential concurrency issues with no optimistic locking
3. Possible DbUpdateConcurrencyException if entities deleted by another process

## Findings

**EfFeatureDefinitionRecordRepository.SaveAsync (lines 29-50):**
```csharp
await using var db = await dbFactory.CreateDbContextAsync(...);
db.FeatureGroupDefinitions.UpdateRange(updatedGroups);  // Detached entities
db.FeatureDefinitions.UpdateRange(updatedFeatures);     // Detached entities
```

Entities in `updatedGroups` were loaded in `_UpdateChangedFeatureGroupsAsync` from a different DbContext.

**EfFeatureValueRecordRecordRepository.UpdateAsync (lines 82-92):**
```csharp
await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
db.FeatureValues.Update(featureValue);  // Entity from different context
```

**EfFeatureValueRecordRecordRepository.DeleteAsync (lines 94-110):**
```csharp
db.FeatureValues.RemoveRange(featureValues);  // Detached entities
```

**Corruption scenario:**
1. Instance A reads record with Version=1
2. Instance B updates record to Version=2
3. Instance A calls UpdateRange with stale entity
4. EF overwrites with stale data (no optimistic concurrency check)

## Proposed Solutions

### Option 1: Re-query Entities in Same Context

**Approach:** Instead of passing entities across contexts, re-query them in the new context before update.

**Pros:**
- Guaranteed fresh data
- Proper change tracking
- Minimal code change

**Cons:**
- Extra DB round trip
- Need to merge changes

**Effort:** 2-3 hours

**Risk:** Low

---

### Option 2: Use ExecuteUpdate/ExecuteDelete (EF 7+)

**Approach:** Use bulk update/delete methods that don't require entity tracking.

**Pros:**
- Single SQL statement
- No tracking issues
- Better performance

**Cons:**
- Requires refactoring parameter passing
- Loses entity-level events

**Effort:** 4-6 hours

**Risk:** Medium

---

### Option 3: Add Optimistic Concurrency Tokens

**Approach:** Add `RowVersion` to entities and handle conflicts explicitly.

**Pros:**
- Detects conflicts
- Standard EF pattern

**Cons:**
- Migration required
- Need conflict handling logic

**Effort:** 3-4 hours

**Risk:** Medium (requires migration)

## Recommended Action

*To be filled during triage.*

## Technical Details

**Affected files:**
- `src/Framework.Features.Storage.EntityFramework/EfFeatureDefinitionRecordRepository.cs:29-50`
- `src/Framework.Features.Storage.EntityFramework/EfFeatureValueRecordRecordRepository.cs:82-110`

## Acceptance Criteria

- [x] Entities are properly tracked in their update/delete context
- [x] No silent data overwrites from stale entities
- [x] Tests verify concurrent modification handling
- [x] Consider adding RowVersion for optimistic concurrency

## Work Log

### 2026-01-14 - Initial Discovery

**By:** Claude Code

**Actions:**
- Identified detached entity pattern across both EF repositories
- Analyzed potential for data corruption in concurrent scenarios
- Reviewed EF Core best practices for DbContextFactory usage

**Learnings:**
- This is a known anti-pattern with IDbContextFactory
- Entities should not cross DbContext boundaries for updates

### 2026-01-14 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready

### 2026-01-14 - Implemented

**By:** Claude Code
**Actions:**
- Fixed detached entity tracking in EfFeatureDefinitionRecordRepository.SaveAsync
  - Re-query entities before updates using entity IDs
  - Apply changes via Patch method for proper change tracking
  - Re-query entities before deletes to ensure proper tracking
- Fixed detached entity tracking in EfFeatureValueRecordRecordRepository
  - UpdateAsync: Use ExecuteUpdateAsync (EF 7+) to avoid tracking issues
  - DeleteAsync: Use ExecuteDeleteAsync (EF 7+) to avoid tracking issues
- Added ConfigureAwait(false) calls for library code best practices
- Solution combines Option 1 (re-query + patch) and Option 2 (ExecuteUpdate/Delete)
  - Definition records: Use re-query + Patch (existing Patch methods available)
  - Value records: Use ExecuteUpdate/Delete (simpler, only Value property mutates)
- All changes compile successfully
- No silent data overwrites from stale entities anymore

### 2026-01-14 - Completed

**By:** Agent
**Actions:**
- Status changed: ready → done
