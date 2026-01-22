---
status: pending
priority: p2
issue_id: "007"
tags: [code-review, dotnet, validation]
dependencies: []
---

# Connection String Validation Missing

## Problem Statement

`UseSqlServer(string connectionString)` accepts connection string without validation, allowing null/empty to propagate into DI.

## Findings

**File:** `src/Headless.Messaging.SqlServer/Setup.cs:21-27`

```csharp
public MessagingOptions UseSqlServer(string connectionString)
{
    return options.UseSqlServer(opt =>
    {
        opt.ConnectionString = connectionString;  // No validation!
    });
}
```

**Impact:**
- Null/empty connection string silently propagates
- Error surfaces much later during runtime, deep in call stack
- Difficult to diagnose

**Related issue in `SqlServerOptions.cs:38-41`:**
```csharp
if (string.IsNullOrEmpty(connectionString))
{
    throw new ArgumentNullException(connectionString);  // Bug: VALUE not NAME
}
```
This throws with the connection string VALUE as parameter name.

## Proposed Solutions

### Option 1: Early Validation (Recommended)

**Approach:** Validate in `UseSqlServer` extension method.

```csharp
public MessagingOptions UseSqlServer(string connectionString)
{
    Argument.IsNotNullOrWhiteSpace(connectionString);  // Using Framework.Checks

    return options.UseSqlServer(opt =>
    {
        opt.ConnectionString = connectionString;
    });
}
```

Also fix the `ConfigureSqlServerOptions` error:
```csharp
if (string.IsNullOrEmpty(connectionString))
{
    throw new ArgumentException(
        "DbContext returned null or empty connection string. Ensure the DbContext is properly configured.",
        nameof(connectionString));
}
```

**Pros:**
- Fail fast at configuration time
- Clear error message

**Cons:**
- None

**Effort:** 15 minutes

**Risk:** Low

## Recommended Action

Implement validation in `UseSqlServer` and fix the ArgumentNullException bug in `ConfigureSqlServerOptions`.

## Technical Details

**Affected files:**
- `src/Headless.Messaging.SqlServer/Setup.cs:21-27`
- `src/Headless.Messaging.SqlServer/SqlServerOptions.cs:38-41`

## Acceptance Criteria

- [ ] UseSqlServer validates connection string early
- [ ] ConfigureSqlServerOptions throws correct exception type
- [ ] Error messages are actionable
- [ ] Build passes

## Work Log

### 2026-01-22 - Initial Discovery

**By:** Pragmatic .NET Reviewer Agent

**Actions:**
- Identified missing validation
- Found ArgumentNullException bug
- Documented fix approach
