---
status: pending
priority: p2
issue_id: "025"
tags: [code-review, dotnet, usability, validation]
dependencies: []
---

# Missing Connection String Validation in PostgreSqlOptions

## Problem Statement

`CreateConnection()` doesn't validate that either `DataSource` or `ConnectionString` is configured. If both are null, a cryptic Npgsql exception is thrown at runtime.

**Impact:** Poor developer experience, confusing error messages at runtime.

## Findings

- **File:** `src/Headless.Messaging.PostgreSql/PostgreSqlOptions.cs:27-30`

```csharp
internal NpgsqlConnection CreateConnection()
{
    return DataSource != null ? DataSource.CreateConnection() : new NpgsqlConnection(ConnectionString);
}
```

**Issues:**
- No validation that ConnectionString is set when DataSource is null
- Runtime error is cryptic: "Invalid connection string"
- Should fail fast at configuration time with clear message

## Proposed Solutions

### Option 1: Validate in CreateConnection (Quick Fix)

**Approach:** Add validation in CreateConnection method.

```csharp
internal NpgsqlConnection CreateConnection()
{
    if (DataSource != null)
        return DataSource.CreateConnection();

    if (string.IsNullOrWhiteSpace(ConnectionString))
        throw new InvalidOperationException(
            "PostgreSQL messaging storage requires either a DataSource or ConnectionString. " +
            "Configure via UsePostgreSql(connectionString) or UsePostgreSql(options => options.ConnectionString = ...)");

    return new NpgsqlConnection(ConnectionString);
}
```

**Pros:**
- Clear error message
- Minimal code change

**Cons:**
- Validation happens at runtime, not configuration time

**Effort:** 15 minutes

**Risk:** Low

---

### Option 2: Add Options Validation (Recommended)

**Approach:** Use IValidateOptions pattern for configuration-time validation.

```csharp
internal class PostgreSqlOptionsValidator : IValidateOptions<PostgreSqlOptions>
{
    public ValidateOptionsResult Validate(string? name, PostgreSqlOptions options)
    {
        if (options.DataSource == null && string.IsNullOrWhiteSpace(options.ConnectionString))
            return ValidateOptionsResult.Fail(
                "PostgreSQL messaging storage requires either DataSource or ConnectionString.");

        return ValidateOptionsResult.Success;
    }
}
```

**Pros:**
- Fails at startup, not runtime
- Standard .NET Options pattern

**Cons:**
- More code
- Need to register validator

**Effort:** 30 minutes

**Risk:** Low

## Recommended Action

Implement Option 2 for fail-fast at configuration time, with Option 1 as defense-in-depth.

## Technical Details

**Affected files:**
- `src/Headless.Messaging.PostgreSql/PostgreSqlOptions.cs:27-30`
- `src/Headless.Messaging.PostgreSql/Setup.cs` (register validator)

## Acceptance Criteria

- [ ] Clear error message when neither DataSource nor ConnectionString configured
- [ ] Validation happens at startup (not first database access)
- [ ] Error message includes configuration guidance
- [ ] Tests verify validation behavior

## Work Log

### 2026-01-22 - Initial Discovery

**By:** Claude Code - Code Review

**Actions:**
- Identified missing validation in CreateConnection
- Noted runtime error is cryptic
- Proposed IValidateOptions pattern

**Learnings:**
- Options validation should fail fast at startup
- Clear error messages improve developer experience
