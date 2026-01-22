---
status: pending
priority: p2
issue_id: "027"
tags: [code-review, dotnet, code-smell]
dependencies: []
---

# Redundant await using Pattern in PostgreSqlDataStorage

## Problem Statement

Several methods use double `await using` declarations on the same connection variable, which is redundant and confusing.

**Impact:** Code clarity, potential confusion about disposal patterns.

## Findings

- **File:** `src/Headless.Messaging.PostgreSql/PostgreSqlDataStorage.cs:348-349`

```csharp
await using var connection = postgreSqlOptions.Value.CreateConnection();
await using var _ = connection;  // Redundant!
```

**Multiple patterns used inconsistently:**
1. Pattern A (redundant): `var connection = ...; await using var _ = connection;`
2. Pattern B (correct): `await using var connection = ...;`
3. Pattern C (redundant): Both patterns on same variable

**Affected lines:**
- Line 39-40: Pattern A
- Line 56-57: Pattern A
- Line 71-72: Pattern A
- Line 81-82: Pattern A
- Line 348-349: Pattern C (both!)
- Line 273: Pattern B (correct)

## Proposed Solutions

### Option 1: Standardize on Pattern B (Recommended)

**Approach:** Use single `await using var` declaration consistently.

```csharp
// Before (Pattern A - redundant)
var connection = postgreSqlOptions.Value.CreateConnection();
await using var _ = connection;

// After (Pattern B - clean)
await using var connection = postgreSqlOptions.Value.CreateConnection();
```

**Pros:**
- Clean and idiomatic
- Single disposal declaration
- Consistent throughout

**Cons:**
- Many lines to change

**Effort:** 30 minutes

**Risk:** Low

## Recommended Action

Standardize on Pattern B (`await using var connection = ...`) throughout all files.

## Technical Details

**Affected files:**
- `src/Headless.Messaging.PostgreSql/PostgreSqlDataStorage.cs` (multiple locations)
- `src/Headless.Messaging.PostgreSql/PostgreSqlMonitoringApi.cs` (verify consistency)
- `src/Headless.Messaging.PostgreSql/PostgreSqlStorageInitializer.cs` (line 42-43)

**Lines to update in PostgreSqlDataStorage:**
- 39-40, 56-57, 71-72, 81-82, 125-126, 204-205, 240-241, 250-251, 348-349, 360-361, 382-383

## Acceptance Criteria

- [ ] All connection disposal uses `await using var connection = ...` pattern
- [ ] No double disposal declarations
- [ ] Consistent pattern across all files
- [ ] Tests pass

## Work Log

### 2026-01-22 - Initial Discovery

**By:** Claude Code - Code Review

**Actions:**
- Identified three different disposal patterns
- Found double disposal on line 348-349
- Catalogued all instances needing update

**Learnings:**
- Consistency in disposal patterns aids code review
- Double await using is harmless but confusing
