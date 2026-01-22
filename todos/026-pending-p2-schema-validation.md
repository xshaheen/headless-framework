---
status: pending
priority: p2
issue_id: "026"
tags: [code-review, security, validation, dotnet]
dependencies: []
---

# Schema Name Not Validated Against SQL Injection

## Problem Statement

The `Schema` property is user-configurable and directly interpolated into DDL statements without validation. Malicious schema names could cause SQL issues.

**Impact:** Defense-in-depth violation, potential SQL injection if schema comes from untrusted source.

## Findings

- **File:** `src/Headless.Messaging.PostgreSql/PostgreSqlStorageInitializer.cs:19-31`

```csharp
public string GetPublishedTableName()
{
    return $"\"{options.Value.Schema}\".\"published\"";
}
```

- **File:** `src/Headless.Messaging.PostgreSql/PostgreSqlStorageInitializer.cs:57-58`

```csharp
var batchSql = $"""
    CREATE SCHEMA IF NOT EXISTS "{schema}";
```

**Issues:**
- Schema is quoted but not validated
- Schema like `messages"."published"; DROP TABLE users; --` could cause issues
- Defense-in-depth requires validation even for developer-configured values

## Proposed Solutions

### Option 1: Validate Schema in Options (Recommended)

**Approach:** Add validation in `PostgreSqlEntityFrameworkMessagingOptions` setter.

```csharp
private static readonly Regex ValidIdentifier = new(@"^[a-zA-Z_][a-zA-Z0-9_]{0,62}$", RegexOptions.Compiled);
private string _schema = DefaultSchema;

public string Schema
{
    get => _schema;
    set
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Schema name cannot be empty", nameof(value));
        if (!ValidIdentifier.IsMatch(value))
            throw new ArgumentException(
                "Schema name must start with letter/underscore and contain only letters, numbers, underscores (max 63 chars)",
                nameof(value));
        _schema = value;
    }
}
```

**Pros:**
- Fails fast at configuration
- Clear error message
- PostgreSQL identifier rules enforced

**Cons:**
- Breaking change if anyone uses special characters (unlikely)

**Effort:** 30 minutes

**Risk:** Low

## Recommended Action

Implement Option 1 with PostgreSQL identifier validation rules.

## Technical Details

**Affected files:**
- `src/Headless.Messaging.PostgreSql/PostgreSqlEntityFrameworkMessagingOptions.cs:13`

**PostgreSQL identifier rules:**
- Max 63 characters
- Start with letter or underscore
- Contain only letters, digits, underscores

## Acceptance Criteria

- [ ] Schema property validates against PostgreSQL identifier rules
- [ ] Clear error message for invalid schema names
- [ ] Tests verify validation behavior
- [ ] Build succeeds

## Work Log

### 2026-01-22 - Initial Discovery

**By:** Claude Code - Code Review

**Actions:**
- Identified schema interpolation without validation
- Researched PostgreSQL identifier rules
- Proposed regex validation pattern

**Learnings:**
- Even quoted identifiers should be validated
- Defense-in-depth is important for DDL operations
