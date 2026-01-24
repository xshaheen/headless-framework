---
status: ready
priority: p3
issue_id: "031"
tags: [code-review, dotnet, code-smell]
dependencies: []
---

# Redundant Field Assignment with Primary Constructor

## Problem Statement

`PostgreSqlStorageInitializer` uses a primary constructor but then manually assigns `_logger` field, which is redundant.

**Impact:** Code clarity, unnecessary duplication.

## Findings

- **File:** `src/Headless.Messaging.PostgreSql/PostgreSqlStorageInitializer.cs:11-17`

```csharp
public sealed class PostgreSqlStorageInitializer(
    ILogger<PostgreSqlStorageInitializer> logger,
    IOptions<PostgreSqlOptions> options,
    IOptions<MessagingOptions> messagingOptions
) : IStorageInitializer
{
    private readonly ILogger _logger = logger;  // Redundant!
```

**Issue:**
- Primary constructor parameter `logger` is already captured
- Manual field assignment `_logger = logger` is redundant
- Can use `logger` directly in the class

## Proposed Solutions

### Option 1: Remove Redundant Field (Recommended)

**Approach:** Use the primary constructor parameter directly.

```csharp
public sealed class PostgreSqlStorageInitializer(
    ILogger<PostgreSqlStorageInitializer> logger,
    IOptions<PostgreSqlOptions> options,
    IOptions<MessagingOptions> messagingOptions
) : IStorageInitializer
{
    // Remove: private readonly ILogger _logger = logger;

    // Use logger directly:
    logger.LogDebug("Ensuring all create database tables script are applied.");
```

**Pros:**
- Cleaner code
- Follows primary constructor pattern

**Cons:**
- None

**Effort:** 5 minutes

**Risk:** Low

## Recommended Action

Remove the redundant field assignment and use `logger` directly.

## Technical Details

**Affected files:**
- `src/Headless.Messaging.PostgreSql/PostgreSqlStorageInitializer.cs:17`

**Line to remove:**
```csharp
private readonly ILogger _logger = logger;
```

**Line to update (52):**
```csharp
// Change from:
_logger.LogDebug("Ensuring all create database tables script are applied.");
// To:
logger.LogDebug("Ensuring all create database tables script are applied.");
```

## Acceptance Criteria

- [ ] Redundant field removed
- [ ] Primary constructor parameter used directly
- [ ] Build succeeds
- [ ] Tests pass

## Work Log

### 2026-01-22 - Initial Discovery

**By:** Claude Code - Code Review

**Actions:**
- Identified redundant field assignment pattern
- Verified logger is only used in one place
- Confirmed can use primary constructor parameter directly

**Learnings:**
- Primary constructors capture parameters automatically
- No need for manual field assignment unless renaming

### 2026-01-24 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready
