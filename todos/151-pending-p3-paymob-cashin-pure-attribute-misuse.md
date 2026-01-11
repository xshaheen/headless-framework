---
status: pending
priority: p3
issue_id: "151"
tags: [code-review, conventions, paymob, cashin]
dependencies: []
---

# [Pure] Attribute Misuse on Interface Methods

## Problem Statement

The `IPaymobCashInBroker` interface methods are marked with `[Pure]` attribute, but several methods throw exceptions (`PaymobCashInException`), making them not truly pure. Pure functions should have no side effects and not throw exceptions.

## Findings

- **Location**: `src/Framework.Payments.Paymob.CashIn/IPaymobCashInBroker.cs`
- **Example**:
  ```csharp
  /// <exception cref="PaymobCashInException"></exception>
  [Pure]
  Task<CashInCreateOrderResponse> CreateOrderAsync(CashInCreateOrderRequest request);
  ```
- Methods throw exceptions per documentation
- Pure functions shouldn't throw
- Similar pattern in `IPaymobCashInAuthenticator.cs`

## Proposed Solutions

### Option 1: Remove [Pure] from Throwing Methods (Recommended)

**Approach:** Only keep `[Pure]` on truly pure methods like `Validate` and `CreateIframeSrc`.

**Pros:**
- Semantically correct
- Follows JetBrains annotations meaning

**Cons:**
- None

**Effort:** 15 minutes

**Risk:** Low

---

### Option 2: Document Intent

**Approach:** If `[Pure]` is meant to indicate "no side effects beyond exceptions", add documentation.

**Pros:**
- Preserves static analysis hints

**Cons:**
- Non-standard usage

**Effort:** 15 minutes

**Risk:** Low

## Recommended Action

**To be filled during triage.**

## Technical Details

**Affected files:**
- `src/Framework.Payments.Paymob.CashIn/IPaymobCashInBroker.cs`
- `src/Framework.Payments.Paymob.CashIn/IPaymobCashInAuthenticator.cs`

**Truly pure methods (keep [Pure]):**
- `Validate` overloads (return bool, no side effects)
- `CreateIframeSrc` (returns string, no side effects)

**Not pure (remove [Pure]):**
- All async methods that throw `PaymobCashInException`

## Acceptance Criteria

- [ ] [Pure] only on truly pure methods
- [ ] Documentation clarifies exception behavior

## Work Log

### 2026-01-11 - Initial Discovery

**By:** Claude Code (Code Review)

**Actions:**
- Identified [Pure] misuse on exception-throwing methods
