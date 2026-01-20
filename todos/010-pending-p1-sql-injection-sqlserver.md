---
status: pending
priority: p1
issue_id: "010"
tags: [security, sql-injection, sqlserver, storage]
dependencies: []
---

# SQL Injection in SQL Server Storage

## Problem Statement

**CRITICAL SECURITY VULNERABILITY**: SQL Server storage implementation uses string concatenation for SQL queries, mirroring the PostgreSQL vulnerability (issue #009).

Affected file:
- `src/Framework.Messages.SqlServer/SqlServerDataStorage.cs` (multiple methods)

## Findings

**Root Cause**: Same pattern as PostgreSQL - string interpolation for building SQL queries.

**Impact**: Identical to PostgreSQL vulnerability:
- Data exfiltration
- Data deletion/corruption
- Privilege escalation
- Database compromise

## Proposed Solutions

### Option 1: Parameterized Queries (RECOMMENDED)
**Effort**: 2-3 hours
**Risk**: Low

Use SqlParameter for all dynamic values:

```csharp
public async Task ChangePublishStateToDelayedAsync(string[] ids)
{
    var parameters = new List<SqlParameter>
    {
        new("@status", StatusName.Delayed)
    };

    var paramNames = new List<string>();
    for (int i = 0; i < ids.Length; i++)
    {
        var paramName = $"@id{i}";
        parameters.Add(new SqlParameter(paramName, ids[i]));
        paramNames.Add(paramName);
    }

    var sql = $"UPDATE {_pubName} SET StatusName = @status WHERE Id IN ({string.Join(",", paramNames)})";

    using var cmd = _connection.CreateCommand();
    cmd.CommandText = sql;
    cmd.Parameters.AddRange(parameters.ToArray());
    await cmd.ExecuteNonQueryAsync();
}
```

### Option 2: Use Dapper
**Effort**: 1-2 days
**Risk**: Medium

Dapper doesn't support IN clauses with arrays directly for SQL Server - need to generate parameters.

## Recommended Action

**Implement Option 1** - parameterize all queries. Keep implementation consistent with PostgreSQL fix (issue #009).

## Acceptance Criteria

- [ ] All SQL queries use SqlParameter
- [ ] No string concatenation for user values
- [ ] Security tests verify injection prevention
- [ ] Implementation matches PostgreSQL pattern (consistency)
- [ ] Static analysis confirms no vulnerabilities

## Technical Details

**SQL Server Differences from PostgreSQL**:
- No native array support - must generate individual parameters for IN clauses
- Use `SqlParameter` instead of `NpgsqlParameter`
- Table names still need `[]` escaping if using dynamic names

**Testing**:
- Same security test suite as PostgreSQL
- Integration tests with Testcontainers SQL Server

## Notes

**BLOCKING ISSUE** - must be fixed before production deployment.

Fix should be implemented in parallel with PostgreSQL fix (issue #009) to maintain consistency.

## Work Log

### 2026-01-20 - Issue Created

**By:** Claude Code (Code Review Agent)

**Actions:**
- Identified during comprehensive security audit
- Linked to PostgreSQL vulnerability (#009)
- Recommended parallel implementation for consistency
