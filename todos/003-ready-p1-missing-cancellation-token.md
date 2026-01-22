---
status: pending
priority: p1
issue_id: "003"
tags: [code-review, dotnet, async, performance]
dependencies: []
---

# Missing CancellationToken Propagation

## Problem Statement

Multiple async methods accept `CancellationToken` but do not propagate it to underlying database operations, preventing graceful shutdown.

## Findings

**File:** `src/Headless.Messaging.SqlServer/SqlServerDataStorage.cs`

Methods accepting but ignoring CancellationToken:
- `AcquireLockAsync:31-51` - `token` parameter ignored
- `ReleaseLockAsync:53-66` - `cancellationToken` ignored
- `RenewLockAsync:68-76` - `token` ignored
- `DeleteExpiresAsync:200-224` - `token` ignored (except in return)

**File:** `src/Headless.Messaging.SqlServer/DbConnectionExtensions.cs`

All methods lack CancellationToken support:
```csharp
public static async Task<int> ExecuteNonQueryAsync(
    this DbConnection connection,
    string sql,
    DbTransaction? transaction = null,
    params object[] sqlParams  // No CancellationToken!
)
```

**Impact:**
- Long-running SQL operations cannot be cancelled
- Application hangs during shutdown waiting for SQL to complete
- No way to timeout operations

## Proposed Solutions

### Option 1: Add CancellationToken to DbConnectionExtensions (Recommended)

**Approach:** Add CancellationToken parameter to all extension methods.

```csharp
public static async Task<int> ExecuteNonQueryAsync(
    this DbConnection connection,
    string sql,
    DbTransaction? transaction = null,
    CancellationToken cancellationToken = default,
    params object[] sqlParams)
{
    await connection.OpenAsync(cancellationToken).AnyContext();
    // ...
    return await command.ExecuteNonQueryAsync(cancellationToken).AnyContext();
}
```

Then update all callers to pass the token through.

**Pros:**
- Enables graceful shutdown
- Supports operation timeouts
- Standard .NET async pattern

**Cons:**
- Breaking change to extension methods
- Many call sites need updating

**Effort:** 2-3 hours

**Risk:** Low

## Recommended Action

Add CancellationToken to `DbConnectionExtensions` methods (with default value for backward compat), then update all callers in `SqlServerDataStorage` and `SqlServerMonitoringApi` to propagate tokens.

## Technical Details

**Affected files:**
- `src/Headless.Messaging.SqlServer/DbConnectionExtensions.cs:11-116` - add parameter
- `src/Headless.Messaging.SqlServer/SqlServerDataStorage.cs` - ~15 call sites
- `src/Headless.Messaging.SqlServer/SqlServerMonitoringApi.cs` - ~10 call sites
- `src/Headless.Messaging.SqlServer/SqlServerStorageInitializer.cs:35-54` - 1 call site

## Acceptance Criteria

- [ ] `DbConnectionExtensions` methods accept CancellationToken
- [ ] All async methods propagate CancellationToken to database calls
- [ ] Tests verify cancellation works
- [ ] Build passes

## Work Log

### 2026-01-22 - Initial Discovery

**By:** Strict .NET Reviewer Agent

**Actions:**
- Identified CancellationToken parameters being ignored
- Found DbConnectionExtensions lacks token support
- Documented all affected call sites
