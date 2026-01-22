---
status: pending
priority: p1
issue_id: "021"
tags: [code-review, data-integrity, postgresql, concurrency]
dependencies: []
---

# FOR UPDATE SKIP LOCKED Without Transaction in MonitoringApi

## Problem Statement

The `_GetMessageAsync` method uses `FOR UPDATE SKIP LOCKED` but executes without an explicit transaction. PostgreSQL auto-commits each statement, so the lock is released immediately after the SELECT completes, providing no actual protection.

**Impact:** False sense of locking, potential concurrent modification issues.

## Findings

- **File:** `src/Headless.Messaging.PostgreSql/PostgreSqlMonitoringApi.cs:292-325`

```csharp
private async Task<MediumMessage?> _GetMessageAsync(string tableName, long id)
{
    var sql =
        $@"SELECT ""Id"" AS ""DbId"", ... FROM {tableName} WHERE ""Id""={id} FOR UPDATE SKIP LOCKED";

    var connection = _options.CreateConnection();
    await using var _ = connection;
    var mediumMessage = await connection.ExecuteReaderAsync(sql, ...);
    // No transaction - lock released immediately after SELECT!
}
```

**Issues:**
1. `FOR UPDATE SKIP LOCKED` is meaningless without a transaction
2. Row is not protected from concurrent modification
3. Similar issue in `_GetMessagesOfNeedRetryAsync` which lacks even `FOR UPDATE`

## Proposed Solutions

### Option 1: Add Transaction Wrapper (Recommended)

**Approach:** Wrap the SELECT in a transaction and return the message for processing.

```csharp
private async Task<MediumMessage?> _GetMessageAsync(string tableName, long id)
{
    await using var connection = _options.CreateConnection();
    await connection.OpenAsync();
    await using var transaction = await connection.BeginTransactionAsync();

    var sql = $@"SELECT ... FROM {tableName} WHERE ""Id""=@Id FOR UPDATE SKIP LOCKED";
    var mediumMessage = await connection.ExecuteReaderAsync(sql, transaction: transaction, ...);

    await transaction.CommitAsync();
    return mediumMessage;
}
```

**Pros:**
- Proper row locking during fetch
- Consistent with other methods

**Cons:**
- Lock is short-lived, may not prevent all race conditions

**Effort:** 1 hour

**Risk:** Low

---

### Option 2: Remove FOR UPDATE (If Not Needed)

**Approach:** If this is a read-only monitoring query, remove the lock hint.

**Pros:**
- Simpler code
- No false expectations

**Cons:**
- May actually need locking for re-processing logic

**Effort:** 15 minutes

**Risk:** Low

---

### Option 3: Add FOR UPDATE to Retry Query

**Approach:** Fix `_GetMessagesOfNeedRetryAsync` to use `FOR UPDATE SKIP LOCKED` with transaction to prevent duplicate processing.

```csharp
// Add transaction and FOR UPDATE SKIP LOCKED
var sql = $"SELECT ... FROM {tableName} WHERE ... FOR UPDATE SKIP LOCKED LIMIT 200;";
```

**Pros:**
- Prevents concurrent workers selecting same messages

**Cons:**
- More complex

**Effort:** 1-2 hours

**Risk:** Medium

## Recommended Action

1. Evaluate if `_GetMessageAsync` actually needs locking - if monitoring only, use Option 2
2. For `_GetMessagesOfNeedRetryAsync`, implement Option 3 to prevent duplicate processing

## Technical Details

**Affected files:**
- `src/Headless.Messaging.PostgreSql/PostgreSqlMonitoringApi.cs:292-325`
- `src/Headless.Messaging.PostgreSql/PostgreSqlDataStorage.cs:365-413` (`_GetMessagesOfNeedRetryAsync`)

**Concurrency concern:**
Multiple workers can select the same messages for retry without proper locking, causing duplicate processing.

## Acceptance Criteria

- [ ] FOR UPDATE only used within explicit transactions
- [ ] Retry message fetch prevents duplicate selection
- [ ] Monitoring queries documented for read-only vs locking behavior
- [ ] Tests verify concurrent access behavior

## Work Log

### 2026-01-22 - Initial Discovery

**By:** Claude Code - Code Review

**Actions:**
- Identified FOR UPDATE SKIP LOCKED without transaction
- Found related issue in retry message fetching
- Analyzed concurrency implications

**Learnings:**
- PostgreSQL FOR UPDATE requires explicit transaction to be effective
- Retry processing is a critical section needing proper locking
