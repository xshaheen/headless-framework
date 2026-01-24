---
status: ready
priority: p3
issue_id: "028"
tags: [code-review, maintainability, dotnet]
dependencies: []
---

# Magic Numbers in PostgreSqlDataStorage

## Problem Statement

Several magic numbers are hardcoded throughout the codebase without explanation or configuration options.

**Impact:** Poor maintainability, unclear rationale, not tunable.

## Findings

- **File:** `src/Headless.Messaging.PostgreSql/PostgreSqlDataStorage.cs:373`
  - `LIMIT 200` - retry batch size, why 200?

- **File:** `src/Headless.Messaging.PostgreSql/PostgreSqlDataStorage.cs:263`
  - `AddMinutes(2)` - two minutes lookahead for delayed messages

- **File:** `src/Headless.Messaging.PostgreSql/PostgreSqlDataStorage.cs:269`
  - `AddMinutes(-1)` - one minute ago for queued messages

**Undocumented timing constants:**
- Why 2 minutes lookahead?
- Why 1 minute lookback for queued?
- Why 200 message batch limit?

## Proposed Solutions

### Option 1: Extract to Named Constants (Minimum)

**Approach:** Define constants with descriptive names and comments.

```csharp
/// <summary>
/// Maximum messages to fetch in a single retry batch.
/// Higher values process more but increase memory and lock contention.
/// </summary>
private const int RetryBatchSize = 200;

/// <summary>
/// Look ahead window for delayed messages.
/// Messages expiring within this window are pre-fetched for scheduling.
/// </summary>
private static readonly TimeSpan DelayedMessageLookahead = TimeSpan.FromMinutes(2);

/// <summary>
/// Lookback window for queued messages that may have been lost.
/// Messages queued longer than this are re-scheduled.
/// </summary>
private static readonly TimeSpan QueuedMessageLookback = TimeSpan.FromMinutes(1);
```

**Pros:**
- Self-documenting
- Easy to understand rationale

**Cons:**
- Still not configurable

**Effort:** 30 minutes

**Risk:** Low

---

### Option 2: Make Configurable via Options

**Approach:** Add to MessagingOptions for user configuration.

**Pros:**
- Tunable for different workloads

**Cons:**
- More complex
- May not need configuration

**Effort:** 1 hour

**Risk:** Low

## Recommended Action

Implement Option 1 as minimum. Consider Option 2 if users report needing tunability.

## Technical Details

**Affected files:**
- `src/Headless.Messaging.PostgreSql/PostgreSqlDataStorage.cs:263, 269, 373`
- `src/Headless.Messaging.SqlServer/SqlServerDataStorage.cs` (same issue)

## Acceptance Criteria

- [ ] Magic numbers replaced with named constants
- [ ] Constants have XML documentation explaining rationale
- [ ] Both PostgreSql and SqlServer implementations updated
- [ ] Tests pass

## Work Log

### 2026-01-22 - Initial Discovery

**By:** Claude Code - Code Review

**Actions:**
- Identified magic numbers in timing and batch logic
- Noted lack of documentation for rationale
- Found same patterns in SqlServer implementation

**Learnings:**
- Magic numbers obscure system behavior
- Named constants with docs improve maintainability

### 2026-01-24 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready
