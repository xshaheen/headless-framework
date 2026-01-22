---
status: pending
priority: p3
issue_id: "014"
tags: [code-review, dotnet, quality]
dependencies: []
---

# Magic Numbers in Code

## Problem Statement

Hardcoded numeric values without named constants reduce code clarity.

## Findings

**File:** `src/Headless.Messaging.SqlServer/SqlServerDataStorage.cs:378`

```csharp
var sql = $"SELECT TOP (200) Id, Content, Retries, Added FROM {tableName}..."
```

The `200` should be a named constant or configurable option.

**Effort:** 15 minutes

**Risk:** Low

## Acceptance Criteria

- [ ] Magic numbers replaced with constants
- [ ] Build passes

## Work Log

### 2026-01-22 - Initial Discovery

**By:** Code Simplicity Reviewer Agent
