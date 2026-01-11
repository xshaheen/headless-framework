# LogLevel.Critical for Concurrency Exception is Too Severe

**Date:** 2026-01-11
**Status:** pending
**Priority:** P3 - Nice-to-Have
**Tags:** code-review, logging, minimalapi, dotnet

---

## Problem Statement

In `MinimalApiExceptionFilter.cs` lines 90-98, concurrency exceptions are logged at Critical level:

```csharp
[LoggerMessage(
    EventId = 5003,
    EventName = "DbConcurrencyException",
    Level = LogLevel.Critical,  // Too severe!
    Message = "Database concurrency exception occurred",
    SkipEnabledCheck = true
)]
private static partial void LogDbConcurrencyException(ILogger logger, Exception exception);
```

**Why it matters:**
- Critical = "application is about to crash"
- Concurrency conflicts are expected in high-contention scenarios
- May trigger unnecessary alerts/pages
- Should be Warning or Error level

---

## Proposed Solutions

### Option A: Change to Warning
```csharp
Level = LogLevel.Warning,
```
- **Pros:** Appropriate severity for expected operational scenario
- **Cons:** May miss trends if not monitored
- **Effort:** Trivial
- **Risk:** None

### Option B: Change to Error
```csharp
Level = LogLevel.Error,
```
- **Pros:** More visible than Warning
- **Cons:** Still may be noisy in high-contention scenarios
- **Effort:** Trivial
- **Risk:** None

---

## Recommended Action

**Option A** - LogLevel.Warning is appropriate for expected conflicts.

---

## Technical Details

**Affected Files:**
- `src/Framework.Api.MinimalApi/Filters/MinimalApiExceptionFilter.cs` (line 93)

---

## Acceptance Criteria

- [ ] Concurrency exceptions logged at Warning level
- [ ] Consistent with expectations for operational behavior

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From code review - strict-dotnet-reviewer |
