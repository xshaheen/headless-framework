---
status: pending
priority: p2
issue_id: "010"
tags: [code-review, dotnet, performance, database]
dependencies: []
---

# Missing Index for Retry Query

## Problem Statement

The `_GetMessagesOfNeedRetryAsync` query filters on columns not optimally covered by existing indexes.

## Findings

**File:** `src/Headless.Messaging.SqlServer/SqlServerDataStorage.cs:377-379`

```sql
SELECT TOP (200) Id, Content, Retries, Added FROM {tableName} WITH (READPAST)
WHERE Retries < @Retries
  AND Version = @Version
  AND Added < @Added
  AND StatusName IN ('Failed', 'Scheduled')
```

**Existing index** (`SqlServerStorageInitializer.cs:109-110`):
```sql
CREATE NONCLUSTERED INDEX [IX_..._Version_ExpiresAt_StatusName]
ON ... ([Version] ASC, [ExpiresAt] ASC, [StatusName] ASC)
INCLUDE ([Id], [Content], [Retries], [Added])
```

**Gap:**
- Query filters on `Added`, not `ExpiresAt`
- Query filters on `Retries`, not covered in key columns
- Index may not be used efficiently

## Proposed Solutions

### Option 1: Add Covering Index (Recommended)

**Approach:** Add index optimized for retry query.

```sql
CREATE NONCLUSTERED INDEX [IX_{table}_RetryQuery]
ON {table} ([Version] ASC, [StatusName] ASC, [Retries] ASC, [Added] ASC)
INCLUDE ([Id], [Content])
```

Add to `SqlServerStorageInitializer._CreateDbTablesScript`:
```csharp
CREATE NONCLUSTERED INDEX [IX_{GetPublishedTableName()}_RetryQuery]
ON {GetPublishedTableName()} ([Version] ASC, [StatusName] ASC, [Retries] ASC, [Added] ASC)
INCLUDE ([Id], [Content])

CREATE NONCLUSTERED INDEX [IX_{GetReceivedTableName()}_RetryQuery]
ON {GetReceivedTableName()} ([Version] ASC, [StatusName] ASC, [Retries] ASC, [Added] ASC)
INCLUDE ([Id], [Content])
```

**Pros:**
- Query uses index seek
- Covers all needed columns
- Faster retry processing

**Cons:**
- Additional index maintenance overhead
- More storage

**Effort:** 30 minutes

**Risk:** Low

## Recommended Action

Add the covering index for both Published and Received tables. This is a non-breaking change that improves query performance.

## Technical Details

**Affected files:**
- `src/Headless.Messaging.SqlServer/SqlServerStorageInitializer.cs` - add index DDL

**Database changes:**
- New index on Published table
- New index on Received table
- Idempotent creation (check IF NOT EXISTS)

## Acceptance Criteria

- [ ] Index added for Published table
- [ ] Index added for Received table
- [ ] Index creation is idempotent
- [ ] Query execution plan shows index usage
- [ ] Build passes

## Work Log

### 2026-01-22 - Initial Discovery

**By:** Performance Oracle Agent

**Actions:**
- Analyzed retry query vs existing indexes
- Identified coverage gap
- Designed covering index
