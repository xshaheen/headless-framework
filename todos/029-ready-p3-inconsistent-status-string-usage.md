---
status: ready
priority: p3
issue_id: "029"
tags: [code-review, dotnet, consistency]
dependencies: []
---

# Inconsistent StatusName String Usage

## Problem Statement

Status names are converted to strings using different methods: `ToString()`, `nameof()`, and `ToString("G")`. This inconsistency could lead to bugs if enum formatting changes.

**Impact:** Code consistency, potential bugs with enum formatting.

## Findings

- **File:** `src/Headless.Messaging.PostgreSql/PostgreSqlDataStorage.cs`

Different patterns used:
```csharp
// Line 80 - direct interpolation
$"SET \"StatusName\"='{StatusName.Delayed}'"

// Line 120 - nameof
new NpgsqlParameter("@StatusName", nameof(StatusName.Scheduled))

// Line 338 - ToString("G")
new NpgsqlParameter("@StatusName", state.ToString("G"))
```

**Issues:**
- `StatusName.Delayed` relies on ToString() implicit call
- `nameof()` returns the enum member name (compile-time)
- `ToString("G")` explicitly requests general format
- All should work the same, but inconsistency is confusing

## Proposed Solutions

### Option 1: Standardize on nameof() (Recommended)

**Approach:** Use `nameof()` for compile-time safety.

```csharp
// For constants
new NpgsqlParameter("@StatusName", nameof(StatusName.Scheduled))

// For variables - use ToString("G") or create extension
new NpgsqlParameter("@StatusName", status.ToString("G"))
```

**Pros:**
- Compile-time safety for constants
- Refactoring-safe

**Cons:**
- Can't use nameof() for variables

**Effort:** 30 minutes

**Risk:** Low

---

### Option 2: Create Extension Method

**Approach:** Add extension method for consistent conversion.

```csharp
internal static string ToDbValue(this StatusName status) => status.ToString("G");
```

**Pros:**
- Single conversion point
- Easy to change format if needed

**Cons:**
- More code

**Effort:** 30 minutes

**Risk:** Low

## Recommended Action

Implement Option 1: use `nameof()` for constants, `ToString("G")` for variables.

## Technical Details

**Affected files:**
- `src/Headless.Messaging.PostgreSql/PostgreSqlDataStorage.cs`
- `src/Headless.Messaging.PostgreSql/PostgreSqlMonitoringApi.cs`
- `src/Headless.Messaging.SqlServer/SqlServerDataStorage.cs` (same issue)

**Lines to review in PostgreSqlDataStorage:**
- 80, 120, 158, 189, 215, 338, 373

## Acceptance Criteria

- [ ] Consistent status name conversion method
- [ ] Use nameof() for compile-time constants
- [ ] Both PostgreSql and SqlServer updated
- [ ] Tests pass

## Work Log

### 2026-01-22 - Initial Discovery

**By:** Claude Code - Code Review

**Actions:**
- Identified three different enum-to-string patterns
- Noted potential confusion and refactoring risk
- Proposed standardization approach

**Learnings:**
- nameof() provides compile-time safety
- Consistency aids code review and maintenance

### 2026-01-24 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready
