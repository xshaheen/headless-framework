---
status: pending
priority: p2
issue_id: "006"
tags: [code-review, security, dotnet, validation]
dependencies: []
---

# Schema Name Validation Missing

## Problem Statement

The `SqlServerEntityFrameworkMessagingOptions.Schema` property accepts any string without validation, allowing potential SQL injection via configuration.

## Findings

**File:** `src/Headless.Messaging.SqlServer/SqlServerEntityFrameworkMessagingOptions.cs:13`

```csharp
public string Schema { get; set; } = DefaultSchema;
```

**File:** `src/Headless.Messaging.SqlServer/SqlServerStorageInitializer.cs:56-62`

```csharp
var batchSql = $"""
    IF NOT EXISTS (SELECT * FROM sys.schemas WHERE name = '{schema}')
    BEGIN
    	EXEC('CREATE SCHEMA [{schema}]')
    END;
```

**Impact:**
- Schema name is interpolated directly into DDL SQL
- Malicious schema like `messages]; DROP TABLE users; --` could execute arbitrary SQL
- Attack requires access to configuration, but defense-in-depth requires validation

## Proposed Solutions

### Option 1: Regex Validation (Recommended)

**Approach:** Add validation to Schema property setter - validate not empty and max length per SQL Server limits (128 chars).

```csharp
private string _schema = DefaultSchema;
public string Schema
{
    get => _schema;
    set
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Schema name cannot be empty", nameof(value));

        if (value.Length > 128)
            throw new ArgumentException("Schema name cannot exceed 128 characters", nameof(value));

        _schema = value;
    }
}
```

**Pros:**
- Validates at configuration time
- Clear error messages
- Follows SQL Server identifier rules

**Cons:**
- Breaks if someone has unusual but valid schema names

**Effort:** 30 minutes

**Risk:** Low

## Recommended Action

Implement Option 1. Use `QUOTENAME()` in the DDL as additional defense.

## Technical Details

**Affected files:**
- `src/Headless.Messaging.SqlServer/SqlServerEntityFrameworkMessagingOptions.cs:13`
- `src/Headless.Messaging.SqlServer/SqlServerStorageInitializer.cs:56-140` (consider QUOTENAME)

## Acceptance Criteria

- [ ] Schema property validates format
- [ ] Invalid schemas throw ArgumentException
- [ ] Tests verify validation
- [ ] Build passes

## Work Log

### 2026-01-22 - Initial Discovery

**By:** Security Sentinel Agent

**Actions:**
- Identified schema interpolation in DDL
- Documented validation approach
