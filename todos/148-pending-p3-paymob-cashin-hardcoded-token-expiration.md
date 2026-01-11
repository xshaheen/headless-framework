---
status: pending
priority: p3
issue_id: "148"
tags: [code-review, configuration, paymob, cashin]
dependencies: []
---

# Hardcoded Token Expiration (55 minutes)

## Problem Statement

Token expiration is hardcoded to 55 minutes regardless of actual token lifetime from Paymob. If Paymob changes token lifetime, this could cause premature expiration or using expired tokens.

## Findings

- **Location**: `src/Framework.Payments.Paymob.CashIn/PaymobCashInAuthenticator.cs:53`
- **Code**:
  ```csharp
  _tokenExpiration = DateTimeOffset.UtcNow.AddMinutes(55);
  ```
- Magic number without documentation
- No way to configure refresh buffer
- Token lifetime from Paymob response not used

## Proposed Solutions

### Option 1: Make Configurable (Recommended)

**Approach:** Add configuration option for token refresh buffer.

```csharp
// In PaymobCashInOptions
public TimeSpan TokenRefreshBuffer { get; set; } = TimeSpan.FromMinutes(5);

// In PaymobCashInAuthenticator
var tokenLifetime = TimeSpan.FromMinutes(60) - _options.CurrentValue.TokenRefreshBuffer;
_tokenExpiration = _timeProvider.GetUtcNow().Add(tokenLifetime);
```

**Pros:**
- Configurable refresh timing
- Clear documentation
- Adapts to Paymob changes

**Cons:**
- Still assumes 60-minute token lifetime

**Effort:** 30 minutes

**Risk:** Low

---

### Option 2: Parse from Response

**Approach:** If Paymob returns expiration time, use it.

**Pros:**
- Most accurate

**Cons:**
- Depends on API response format

**Effort:** 1 hour (requires API investigation)

**Risk:** Medium

## Recommended Action

**To be filled during triage.**

## Technical Details

**Affected files:**
- `src/Framework.Payments.Paymob.CashIn/PaymobCashInAuthenticator.cs:53`
- `src/Framework.Payments.Paymob.CashIn/Models/PaymobCashInOptions.cs`

## Acceptance Criteria

- [ ] Token refresh buffer is configurable
- [ ] Magic number documented or removed
- [ ] Option validated (must be positive, less than 60 min)

## Work Log

### 2026-01-11 - Initial Discovery

**By:** Claude Code (Code Review)

**Actions:**
- Identified hardcoded token expiration
- Drafted configurable solution
