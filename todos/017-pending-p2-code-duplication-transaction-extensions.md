---
status: pending
priority: p2
issue_id: "017"
tags: [code-quality, duplication, refactoring, maintainability]
dependencies: []
---

# High Code Duplication in Transaction Extensions (360+ LOC)

## Problem Statement

Transaction extension methods have 360+ lines of duplicated code across PostgreSQL, SQL Server, and MongoDB implementations.

## Findings

**Duplicated Patterns**:
```csharp
// PostgreSQL
public static OutboxTransaction BeginTransaction(this NpgsqlConnection connection, ...)
{
    return new PostgreSqlOutboxTransaction(connection.BeginTransaction(), ...);
}

// SQL Server
public static OutboxTransaction BeginTransaction(this SqlConnection connection, ...)
{
    return new SqlServerOutboxTransaction(connection.BeginTransaction(), ...);
}

// MongoDB
public static OutboxTransaction BeginTransaction(this IMongoClient client, ...)
{
    return new MongoDbOutboxTransaction(client.StartSession(), ...);
}
```

**Impact**:
- Bug fixes must be applied 3+ times
- Inconsistent behavior across providers
- Higher maintenance cost

## Proposed Solutions

### Option 1: Generic Extension Method (RECOMMENDED)
**Effort**: 2-3 hours

```csharp
public static class OutboxTransactionExtensions
{
    public static TTransaction BeginOutboxTransaction<TConnection, TTransaction>(
        this TConnection connection,
        Func<TConnection, OutboxTransaction> factory,
        IServiceProvider provider)
        where TTransaction : OutboxTransaction
    {
        var transaction = factory(connection);
        // Common logic here
        return transaction;
    }
}

// Usage:
connection.BeginOutboxTransaction(
    conn => new PostgreSqlOutboxTransaction(conn.BeginTransaction(), provider)
);
```

### Option 2: Base Class with Template Method
**Effort**: 3-4 hours

Abstract common logic into base class, override provider-specific parts.

## Recommended Action

Implement Option 1 - extract generic extension.

## Acceptance Criteria

- [ ] Duplication reduced to <50 LOC
- [ ] All providers use shared logic
- [ ] Tests verify consistent behavior
- [ ] No behavioral regressions

## Work Log

### 2026-01-20 - Issue Created

**By:** Claude Code (Pattern Recognition Specialist Agent)

**Actions:**
- Identified 360+ LOC duplication
- Analyzed patterns across providers
- Proposed generic solution
