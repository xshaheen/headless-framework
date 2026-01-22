---
status: pending
priority: p1
issue_id: "019"
tags: [code-review, dotnet, async, reliability]
dependencies: []
---

# Missing CancellationToken Propagation in DbConnectionExtensions

## Problem Statement

All async database operations in `DbConnectionExtensions` lack `CancellationToken` parameters. This prevents graceful cancellation during shutdown, request timeouts, or long-running queries.

**Impact:** Operations cannot be cancelled, leading to resource leaks and poor shutdown behavior.

## Findings

- **File:** `src/Headless.Messaging.PostgreSql/DbConnectionExtensions.cs:11-111`
- All three methods lack CancellationToken:
  - `ExecuteNonQueryAsync` (lines 11-35)
  - `ExecuteReaderAsync` (lines 37-70)
  - `ExecuteScalarAsync` (lines 72-110)

Additionally, many `IDataStorage` methods accept `CancellationToken` but don't pass it through:
- `AcquireLockAsync` (line 30-50) - accepts `token` but never uses it
- `ReleaseLockAsync` (line 52-65) - accepts `token` but never uses it
- `RenewLockAsync` (line 67-75) - accepts `token` but never uses it
- `ChangePublishStateToDelayedAsync` (line 77) - no token parameter at all

## Proposed Solutions

### Option 1: Add CancellationToken to DbConnectionExtensions (Recommended)

**Approach:** Add `CancellationToken` parameter to all extension methods and propagate to ADO.NET calls.

```csharp
public static async Task<int> ExecuteNonQueryAsync(
    this DbConnection connection,
    string sql,
    DbTransaction? transaction = null,
    CancellationToken cancellationToken = default,
    params object[] sqlParams)
{
    if (connection.State == ConnectionState.Closed)
    {
        await connection.OpenAsync(cancellationToken).AnyContext();
    }
    // ...
    return await command.ExecuteNonQueryAsync(cancellationToken).AnyContext();
}
```

**Pros:**
- Full cancellation support
- Follows async best practices

**Cons:**
- Breaking change to extension method signatures
- Need to update all callers

**Effort:** 2-3 hours

**Risk:** Low

---

### Option 2: Create New Overloads

**Approach:** Add new overloads with CancellationToken, keeping old signatures for compatibility.

**Pros:**
- Non-breaking change

**Cons:**
- API surface bloat
- Old methods still problematic

**Effort:** 2 hours

**Risk:** Low

## Recommended Action

Implement Option 1. Update all callers to pass their CancellationToken through.

## Technical Details

**Affected files:**
- `src/Headless.Messaging.PostgreSql/DbConnectionExtensions.cs` (core fix)
- `src/Headless.Messaging.PostgreSql/PostgreSqlDataStorage.cs` (update all callers)
- `src/Headless.Messaging.PostgreSql/PostgreSqlMonitoringApi.cs` (update all callers)
- `src/Headless.Messaging.PostgreSql/PostgreSqlStorageInitializer.cs` (update callers)
- `src/Headless.Messaging.SqlServer/DbConnectionExtensions.cs` (same issue)

**Methods to update in PostgreSqlDataStorage:**
- Line 48: `ExecuteNonQueryAsync` call in AcquireLockAsync
- Line 64: `ExecuteNonQueryAsync` call in ReleaseLockAsync
- Line 74: `ExecuteNonQueryAsync` call in RenewLockAsync
- Line 83: `ExecuteNonQueryAsync` call in ChangePublishStateToDelayedAsync
- And all other async DB operations

## Acceptance Criteria

- [ ] DbConnectionExtensions methods accept CancellationToken
- [ ] All callers pass their CancellationToken through
- [ ] OpenAsync and ExecuteXxxAsync receive the token
- [ ] Tests pass
- [ ] SqlServer implementation also updated

## Work Log

### 2026-01-22 - Initial Discovery

**By:** Claude Code - Code Review

**Actions:**
- Identified CancellationToken not propagated to any DB operation
- Found multiple IDataStorage methods accept but ignore token
- Analyzed call chain through DbConnectionExtensions

**Learnings:**
- This is a widespread pattern issue affecting all storage implementations
- Should be fixed at the extension method level for maximum impact
