---
status: pending
priority: p2
issue_id: "145"
tags: [code-review, testability, paymob, cashin]
dependencies: []
---

# TimeProvider Used Inconsistently

## Problem Statement

The `PaymobCashInAuthenticator` injects `TimeProvider` for testability but doesn't use it consistently. Token expiration is set using `DateTimeOffset.UtcNow` instead of `_timeProvider.GetUtcNow()`.

## Findings

- **Location**: `src/Framework.Payments.Paymob.CashIn/PaymobCashInAuthenticator.cs:53`
- **Code**:
  ```csharp
  _tokenExpiration = DateTimeOffset.UtcNow.AddMinutes(55);  // Should use _timeProvider
  ```
- TimeProvider is injected (line 14) but not used here
- Check uses TimeProvider correctly (line 60): `_tokenExpiration > _timeProvider.GetUtcNow()`
- This inconsistency makes testing harder

## Proposed Solutions

### Option 1: Use TimeProvider Consistently (Recommended)

**Approach:** Replace `DateTimeOffset.UtcNow` with `_timeProvider.GetUtcNow()`.

```csharp
_tokenExpiration = _timeProvider.GetUtcNow().AddMinutes(55);
```

**Pros:**
- Consistent time source
- Enables proper unit testing
- Simple fix

**Cons:**
- None

**Effort:** 5 minutes

**Risk:** Low

## Recommended Action

**To be filled during triage.**

## Technical Details

**Affected files:**
- `src/Framework.Payments.Paymob.CashIn/PaymobCashInAuthenticator.cs:53`

## Acceptance Criteria

- [ ] All time operations use _timeProvider
- [ ] Unit tests can mock time for expiration testing

## Work Log

### 2026-01-11 - Initial Discovery

**By:** Claude Code (Code Review)

**Actions:**
- Identified inconsistent TimeProvider usage
