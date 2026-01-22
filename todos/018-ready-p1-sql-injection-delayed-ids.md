---
status: pending
priority: p1
issue_id: "018"
tags: [code-review, security, dotnet, sql-injection]
dependencies: []
---

# SQL Injection in ChangePublishStateToDelayedAsync

## Problem Statement

The `ChangePublishStateToDelayedAsync` method directly concatenates user-provided `ids` array into SQL without parameterization, creating a critical SQL injection vulnerability.

**Impact:** Full database compromise, data exfiltration, data destruction.

## Findings

- **File:** `src/Headless.Messaging.PostgreSql/PostgreSqlDataStorage.cs:77-84`
- The `ids` array is joined and interpolated directly into SQL string
- Same pattern exists in SqlServer implementation

```csharp
public async Task ChangePublishStateToDelayedAsync(string[] ids)
{
    var sql =
        $"UPDATE {_pubName} SET \"StatusName\"='{StatusName.Delayed}' WHERE \"Id\" IN ({string.Join(',', ids)});";
```

**Example attack:** `ids = new[] { "1); DROP TABLE published; --" }`

## Proposed Solutions

### Option 1: Use PostgreSQL ANY(@Ids) Syntax (Recommended)

**Approach:** Use parameterized array query with `ANY`.

```csharp
var sql = $"UPDATE {_pubName} SET \"StatusName\"=@Status WHERE \"Id\" = ANY(@Ids);";
var sqlParams = new object[]
{
    new NpgsqlParameter("@Status", StatusName.Delayed.ToString("G")),
    new NpgsqlParameter("@Ids", ids.Select(long.Parse).ToArray())
};
```

**Pros:**
- Fully parameterized, no injection risk
- Clean PostgreSQL-native syntax

**Cons:**
- Requires parsing IDs to long

**Effort:** 30 minutes

**Risk:** Low

---

### Option 2: Validate IDs are Numeric

**Approach:** Validate all IDs are numeric before concatenation.

```csharp
if (!ids.All(id => long.TryParse(id, out _)))
    throw new ArgumentException("All IDs must be numeric");
```

**Pros:**
- Minimal code change

**Cons:**
- Still uses string interpolation (bad pattern)
- Defense-in-depth not achieved

**Effort:** 15 minutes

**Risk:** Medium (pattern still dangerous)

## Recommended Action

Implement Option 1 using `ANY(@Ids)` syntax. Apply same fix to SqlServer implementation.

## Technical Details

**Affected files:**
- `src/Headless.Messaging.PostgreSql/PostgreSqlDataStorage.cs:77-84`
- `src/Headless.Messaging.SqlServer/SqlServerDataStorage.cs:78-84` (similar issue)

**Related components:**
- Message scheduling system
- Delayed message processing

## Acceptance Criteria

- [ ] SQL uses parameterized query with ANY() or equivalent
- [ ] No string interpolation of user-provided values
- [ ] SqlServer implementation also fixed
- [ ] Tests pass
- [ ] Security review approved

## Work Log

### 2026-01-22 - Initial Discovery

**By:** Claude Code - Code Review

**Actions:**
- Identified SQL injection vulnerability in ChangePublishStateToDelayedAsync
- Confirmed same pattern in SqlServer implementation
- Drafted parameterized solution using ANY()

**Learnings:**
- Pattern recognition found this is the most severe SQL injection in the codebase
- PostgreSQL supports array parameters natively via ANY()
