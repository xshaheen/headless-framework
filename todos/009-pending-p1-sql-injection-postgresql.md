---
status: pending
priority: p1
issue_id: "009"
tags: [security, sql-injection, postgresql, storage]
dependencies: []
---

# SQL Injection in PostgreSQL Storage

## Problem Statement

**CRITICAL SECURITY VULNERABILITY**: PostgreSQL storage implementation uses string concatenation for SQL queries, allowing SQL injection attacks in multiple locations.

Affected files:
- `src/Framework.Messages.PostgreSql/PostgreSqlDataStorage.cs:79-83` (ChangePublishStateToDelayedAsync)
- `src/Framework.Messages.PostgreSql/PostgreSqlDataStorage.cs:237` (DeleteExpiresAsync)
- `src/Framework.Messages.PostgreSql/PostgreSqlDataStorage.cs:246` (DELETE query)

## Findings

**Root Cause**: Direct string interpolation and `string.Join` used to build SQL queries without parameterization.

**Vulnerable Code Example** (Lines 79-83):
```csharp
public async Task ChangePublishStateToDelayedAsync(string[] ids)
{
    var sql = $"UPDATE {_pubName} SET \"StatusName\"='{StatusName.Delayed}' WHERE \"Id\" IN ({string.Join(',', ids)});";
    await _connection.ExecuteNonQueryAsync(sql);
}
```

**Attack Vector**: If `ids` array contains malicious input like `1); DROP TABLE published; --`, the query becomes:
```sql
UPDATE published SET "StatusName"='Delayed' WHERE "Id" IN (1); DROP TABLE published; --);
```

**Impact**:
- Data exfiltration
- Data deletion/corruption
- Privilege escalation
- Complete database compromise

## Proposed Solutions

### Option 1: Parameterized Queries (RECOMMENDED)
**Effort**: 2-3 hours
**Risk**: Low (standard practice)

```csharp
public async Task ChangePublishStateToDelayedAsync(string[] ids)
{
    var parameters = ids.Select((id, i) => new NpgsqlParameter($"@id{i}", id)).ToArray();
    var paramNames = string.Join(",", parameters.Select(p => p.ParameterName));
    var sql = $"UPDATE {_pubName} SET \"StatusName\" = @status WHERE \"Id\" IN ({paramNames})";

    var cmd = _connection.CreateCommand();
    cmd.CommandText = sql;
    cmd.Parameters.Add(new NpgsqlParameter("@status", StatusName.Delayed));
    cmd.Parameters.AddRange(parameters);

    await cmd.ExecuteNonQueryAsync();
}
```

### Option 2: Use Dapper or EF Core
**Effort**: 1-2 days
**Risk**: Medium (larger refactor)

Replace raw ADO.NET with Dapper for parameterized queries:
```csharp
await _connection.ExecuteAsync(
    $"UPDATE {_pubName} SET \"StatusName\" = @status WHERE \"Id\" = ANY(@ids)",
    new { status = StatusName.Delayed, ids }
);
```

## Recommended Action

**Implement Option 1** for all affected methods:
1. ChangePublishStateToDelayedAsync
2. DeleteExpiresAsync (both publish and receive tables)
3. Any other methods using string concatenation

Use `ANY(@ids)` PostgreSQL array syntax for IN clauses.

## Acceptance Criteria

- [ ] All SQL queries use parameterized commands (NpgsqlParameter)
- [ ] No string concatenation or interpolation for user-provided values
- [ ] Security test added to verify injection prevention
- [ ] Same fix applied to SQL Server storage implementation
- [ ] Code review confirms no remaining injection vulnerabilities

## Technical Details

**Affected Components**:
- PostgreSqlDataStorage
- PostgreSqlPublisher (if exists)
- All methods executing dynamic SQL

**Testing Requirements**:
- Unit tests with malicious input (SQL injection payloads)
- Integration tests with Testcontainers
- Security scan with static analysis tools

## Notes

This is a **blocking security issue** for any production deployment. Must be fixed before merge.

PostgreSQL-specific syntax note: Use `= ANY(@array)` instead of `IN (...)` for array parameters.

## Work Log

### 2026-01-20 - Issue Created

**By:** Claude Code (Code Review Agent)

**Actions:**
- Identified SQL injection vulnerabilities during comprehensive code review
- Analyzed attack vectors and impact
- Proposed parameterized query solutions

**Priority Justification:**
- OWASP Top 10 vulnerability
- Direct database access exposure
- High severity, high likelihood
