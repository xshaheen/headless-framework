---
status: pending
priority: p1
issue_id: "001"
tags: [code-review, security, dotnet, sql-injection]
dependencies: []
---

# SQL Injection in ChangePublishStateToDelayedAsync

## Problem Statement

The `ChangePublishStateToDelayedAsync` method directly concatenates a string array into SQL, creating a SQL injection vulnerability.

## Findings

**File:** `src/Headless.Messaging.SqlServer/SqlServerDataStorage.cs:78-84`

```csharp
public async Task ChangePublishStateToDelayedAsync(string[] ids)
{
    var sql = $"UPDATE {_pubName} SET [StatusName]='{StatusName.Delayed}' WHERE [Id] IN ({string.Join(',', ids)});";
    // ...
}
```

- The `ids` array is joined and interpolated directly into SQL
- No validation that IDs are numeric
- Attacker controlling `ids` can execute arbitrary SQL: `ids = ["1); DROP TABLE Published; --"]`
- Query plan cannot be cached due to dynamic SQL

**Similar issues:**
- `DeleteReceivedMessageAsync:238` - `WHERE Id={id}` (lower risk - long type)
- `DeletePublishedMessageAsync:248` - `WHERE Id={id}` (lower risk - long type)
- `SqlServerMonitoringApi._GetMessageAsync:305` - `WHERE Id={id}` (lower risk - long type)

## Proposed Solutions

### Option 1: Validate and Parameterize (Recommended)

**Approach:** Validate all IDs are numeric, use parameterized IN clause.

```csharp
public async Task ChangePublishStateToDelayedAsync(string[] ids)
{
    if (ids.Length == 0) return;

    // Validate all IDs are numeric
    var numericIds = new long[ids.Length];
    for (int i = 0; i < ids.Length; i++)
    {
        if (!long.TryParse(ids[i], out numericIds[i]))
            throw new ArgumentException($"Invalid ID format: {ids[i]}", nameof(ids));
    }

    var parameters = numericIds.Select((id, i) => new SqlParameter($"@Id{i}", id)).ToArray();
    var paramNames = string.Join(",", parameters.Select(p => p.ParameterName));
    var sql = $"UPDATE {_pubName} SET [StatusName]=@Status WHERE [Id] IN ({paramNames});";

    var allParams = parameters.Concat([new SqlParameter("@Status", StatusName.Delayed.ToString("G"))]).ToArray();
    // ...
}
```

**Pros:**
- Eliminates SQL injection risk
- Query plans can be reused for same parameter count
- Clear validation errors

**Cons:**
- More code
- Parameter count varies per call

**Effort:** 1 hour

**Risk:** Low

---

### Option 2: Use Table-Valued Parameter

**Approach:** Use SQL Server TVP for bulk IDs.

**Pros:**
- Single cached query plan
- Handles large ID lists efficiently
- Industry standard for bulk operations

**Cons:**
- Requires creating SQL Server type
- More complex setup

**Effort:** 2-3 hours

**Risk:** Low

## Recommended Action

Implement Option 1 for immediate fix. Also fix the numeric ID interpolation in `Delete*MessageAsync` and `_GetMessageAsync` methods to use parameters consistently.

## Technical Details

**Affected files:**
- `src/Headless.Messaging.SqlServer/SqlServerDataStorage.cs:78-84` - main vulnerability
- `src/Headless.Messaging.SqlServer/SqlServerDataStorage.cs:236-254` - delete methods
- `src/Headless.Messaging.SqlServer/SqlServerMonitoringApi.cs:302-337` - get message

**Related components:**
- `IDataStorage` interface may need signature update if validation throws

## Acceptance Criteria

- [ ] All ID parameters use parameterized queries
- [ ] Validation added for string ID inputs
- [ ] Tests verify SQL injection is blocked
- [ ] Build passes

## Work Log

### 2026-01-22 - Initial Discovery

**By:** Code Review Agent

**Actions:**
- Identified SQL injection vulnerability in `ChangePublishStateToDelayedAsync`
- Found related issues in delete/get methods
- Documented remediation options
