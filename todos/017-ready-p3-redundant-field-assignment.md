---
status: pending
priority: p3
issue_id: "017"
tags: [code-review, dotnet, quality]
dependencies: []
---

# Redundant Field Assignment in SqlServerStorageInitializer

## Problem Statement

Redundant field assignment when using primary constructors.

## Findings

**File:** `src/Headless.Messaging.SqlServer/SqlServerStorageInitializer.cs:18`

```csharp
public sealed class SqlServerStorageInitializer(
    ILogger<SqlServerStorageInitializer> logger,
    // ...
) : IStorageInitializer
{
    private readonly ILogger _logger = logger;  // Redundant - 'logger' is already a field
```

With primary constructors, `logger` is already captured as a field.

**Effort:** 5 minutes

**Risk:** Low

## Acceptance Criteria

- [ ] Remove `_logger` field
- [ ] Use `logger` directly
- [ ] Build passes

## Work Log

### 2026-01-22 - Initial Discovery

**By:** Code Simplicity Reviewer Agent
