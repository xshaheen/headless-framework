---
status: pending
priority: p2
issue_id: "011"
tags: [code-review, dotnet, simplification]
dependencies: []
---

# Remove SQL Server 2008 Support

## Problem Statement

SQL Server 2008 compatibility code adds maintenance burden for a dead platform. SQL Server 2008 reached end of extended support on July 9, 2019.

## Findings

**File:** `src/Headless.Messaging.SqlServer/SqlServerEntityFrameworkMessagingOptions.cs:20-31`

```csharp
internal bool IsSqlServer2008 { get; set; }

public SqlServerEntityFrameworkMessagingOptions UseSqlServer2008()
{
    IsSqlServer2008 = true;
    return this;
}
```

**File:** `src/Headless.Messaging.SqlServer/SqlServerMonitoringApi.cs`

Multiple dual query paths:
- Lines 109-113: `sqlQuery2008` vs `sqlQuery` for pagination
- Lines 234-255: `sqlQuery2008` vs `sqlQuery` for timeline stats

**Impact:**
- ~60 lines of code maintained for dead platform
- Microsoft.Data.SqlClient 5.x+ dropped SQL Server 2008 support
- Project targets .NET 10 - incompatible with SQL Server 2008 environments

## Proposed Solutions

### Option 1: Remove All SQL Server 2008 Code (Recommended)

**Approach:** Remove `IsSqlServer2008` property, `UseSqlServer2008()` method, and all conditional query branches.

**Changes:**
1. Delete `IsSqlServer2008` property
2. Delete `UseSqlServer2008()` method
3. Remove all `sqlQuery2008` variables
4. Remove all `_options.IsSqlServer2008 ? sqlQuery2008 : sqlQuery` ternaries

**Pros:**
- ~60 lines of code removed
- Simpler maintenance
- No dead code

**Cons:**
- Breaking change if anyone uses it (unlikely)

**Effort:** 30 minutes

**Risk:** Low

## Recommended Action

Remove all SQL Server 2008 support code. Document the breaking change in release notes if needed.

## Technical Details

**Affected files:**
- `src/Headless.Messaging.SqlServer/SqlServerEntityFrameworkMessagingOptions.cs:20-31`
- `src/Headless.Messaging.SqlServer/SqlServerMonitoringApi.cs:109-113, 140, 234-255, 270`

## Acceptance Criteria

- [ ] IsSqlServer2008 property removed
- [ ] UseSqlServer2008() method removed
- [ ] All sqlQuery2008 variables removed
- [ ] All ternary conditionals simplified
- [ ] Build passes

## Work Log

### 2026-01-22 - Initial Discovery

**By:** Code Simplicity Reviewer Agent

**Actions:**
- Identified dead SQL Server 2008 code
- Documented all affected locations
- Recommended removal
