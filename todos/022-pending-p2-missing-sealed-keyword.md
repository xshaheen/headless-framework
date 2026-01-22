---
status: pending
priority: p2
issue_id: "022"
tags: [code-review, dotnet, conventions]
dependencies: []
---

# Missing sealed Keyword on Public Classes

## Problem Statement

Several public classes are not marked `sealed`, violating project conventions that require `sealed` by default. This allows unintended inheritance and prevents devirtualization optimizations.

## Findings

**Classes missing `sealed`:**
- `src/Headless.Messaging.PostgreSql/PostgreSqlMonitoringApi.cs:14`
- `src/Headless.Messaging.PostgreSql/PostgreSqlOutboxTransaction.cs:13`
- `src/Headless.Messaging.PostgreSql/PostgreSqlEntityFrameworkDbTransaction.cs:10`

**Already correctly sealed:**
- `PostgreSqlDataStorage` - `sealed`
- `PostgreSqlStorageInitializer` - `sealed`
- `PostgreSqlOptions` - `sealed`

**Note:** `PostgreSqlEntityFrameworkMessagingOptions` cannot be sealed as `PostgreSqlOptions` inherits from it.

## Proposed Solutions

### Option 1: Add sealed to All Applicable Classes (Recommended)

**Approach:** Add `sealed` modifier to classes not designed for inheritance.

```csharp
// PostgreSqlMonitoringApi.cs:14
public sealed class PostgreSqlMonitoringApi(...)

// PostgreSqlOutboxTransaction.cs:13
public sealed class PostgreSqlOutboxTransaction(...)

// PostgreSqlEntityFrameworkDbTransaction.cs:10
internal sealed class PostgreSqlEntityFrameworkDbTransaction(...)
```

**Pros:**
- Follows project conventions
- Enables JIT devirtualization
- Documents design intent

**Cons:**
- Breaking change if anyone inherits (unlikely)

**Effort:** 15 minutes

**Risk:** Low

## Recommended Action

Add `sealed` to all three classes.

## Technical Details

**Affected files:**
- `src/Headless.Messaging.PostgreSql/PostgreSqlMonitoringApi.cs:14`
- `src/Headless.Messaging.PostgreSql/PostgreSqlOutboxTransaction.cs:13`
- `src/Headless.Messaging.PostgreSql/PostgreSqlEntityFrameworkDbTransaction.cs:10`

## Acceptance Criteria

- [ ] All applicable classes marked sealed
- [ ] Build succeeds
- [ ] Tests pass

## Work Log

### 2026-01-22 - Initial Discovery

**By:** Claude Code - Code Review

**Actions:**
- Identified classes missing sealed modifier
- Verified which classes cannot be sealed due to inheritance

**Learnings:**
- Consistent application of sealed improves performance and documents intent
