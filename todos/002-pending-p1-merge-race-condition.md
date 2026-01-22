---
status: pending
priority: p1
issue_id: "002"
tags: [code-review, data-integrity, dotnet, concurrency]
dependencies: []
---

# MERGE Statement Race Condition and NULL Group Issue

## Problem Statement

The `_StoreReceivedMessage` MERGE statement has race condition vulnerabilities and breaks idempotency when Group is NULL.

## Findings

**File:** `src/Headless.Messaging.SqlServer/SqlServerDataStorage.cs:352-368`

```sql
MERGE {_recName} AS target
USING (SELECT @MessageId AS MessageId, @Group AS [Group]) AS source
ON target.MessageId = source.MessageId AND target.[Group] = source.[Group]
WHEN MATCHED THEN
    UPDATE SET StatusName = @StatusName, ...
WHEN NOT MATCHED THEN
    INSERT ...
```

**Issues found:**

1. **No locking hint** - Two concurrent MERGE statements for same MessageId+Group can race, causing either:
   - Duplicate inserts (unique constraint violation)
   - Lost updates

2. **NULL Group comparison** - When `@Group` is NULL, the ON clause `target.[Group] = source.[Group]` evaluates to UNKNOWN, not TRUE. The MATCHED branch never triggers for NULL groups, breaking idempotency.

3. **Unconditional UPDATE** - Updates overwrite StatusName regardless of current state. A `Succeeded` message could be reset to `Scheduled`.

**Schema issue:**
- `src/Headless.Messaging.SqlServer/SqlServerStorageInitializer.cs:70` - Group column allows NULL
- Unique index on `(MessageId, Group)` treats NULL values as distinct

## Proposed Solutions

### Option 1: Add HOLDLOCK and Fix NULL Comparison (Recommended)

**Approach:** Add locking hint and handle NULL comparison.

```sql
MERGE {_recName} WITH (HOLDLOCK) AS target
USING (SELECT @MessageId AS MessageId, @Group AS [Group]) AS source
ON target.MessageId = source.MessageId
   AND (target.[Group] = source.[Group] OR (target.[Group] IS NULL AND source.[Group] IS NULL))
WHEN MATCHED AND target.StatusName NOT IN ('Succeeded', 'Failed') THEN
    UPDATE SET StatusName = @StatusName, ...
WHEN NOT MATCHED THEN
    INSERT ...
```

**Pros:**
- Eliminates race condition
- Fixes NULL group idempotency
- Prevents state regression

**Cons:**
- HOLDLOCK increases lock duration
- Slightly more complex SQL

**Effort:** 1-2 hours

**Risk:** Low

---

### Option 2: Make Group NOT NULL

**Approach:** Change schema to require Group, use empty string as default.

**Pros:**
- Simpler comparison logic
- No NULL handling needed

**Cons:**
- Breaking schema change
- Migration required for existing data

**Effort:** 3-4 hours

**Risk:** Medium (migration)

## Recommended Action

Implement Option 1. The HOLDLOCK ensures serializable isolation for the MERGE. The NULL comparison fix ensures idempotency. The state guard prevents accidental state regression.

## Technical Details

**Affected files:**
- `src/Headless.Messaging.SqlServer/SqlServerDataStorage.cs:352-368`
- `src/Headless.Messaging.SqlServer/SqlServerStorageInitializer.cs:70` (schema reference)

**Database changes:**
- No migration required for Option 1
- Query change only

## Acceptance Criteria

- [ ] MERGE statement has HOLDLOCK hint
- [ ] NULL Group comparison handles IS NULL case
- [ ] State transition guards prevent overwriting Succeeded/Failed
- [ ] Integration tests verify concurrent message handling
- [ ] Build passes

## Work Log

### 2026-01-22 - Initial Discovery

**By:** Data Integrity Guardian Agent

**Actions:**
- Identified MERGE race condition
- Found NULL Group comparison issue
- Identified unconditional UPDATE concern
- Documented solution options
