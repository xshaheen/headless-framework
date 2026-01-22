---
status: pending
priority: p3
issue_id: "015"
tags: [code-review, dotnet, performance]
dependencies: []
---

# TypeDescriptor Overhead in ExecuteScalarAsync

## Problem Statement

`TypeDescriptor.GetConverter` is called on every scalar query but only `int` is ever used.

## Findings

**File:** `src/Headless.Messaging.SqlServer/DbConnectionExtensions.cs:102-112`

```csharp
var converter = TypeDescriptor.GetConverter(returnType);
if (converter.CanConvertFrom(objValue.GetType()))
{
    result = (T)converter.ConvertFrom(objValue)!;
}
else
{
    result = (T)Convert.ChangeType(objValue, returnType);
}
```

All usages are `ExecuteScalarAsync<int>` for COUNT queries.

**Effort:** 15 minutes

**Risk:** Low

## Proposed Solutions

Simplify to direct cast:
```csharp
result = (T)Convert.ChangeType(objValue, typeof(T));
```

## Acceptance Criteria

- [ ] TypeDescriptor removed
- [ ] Tests pass
- [ ] Build passes

## Work Log

### 2026-01-22 - Initial Discovery

**By:** Code Simplicity Reviewer Agent
