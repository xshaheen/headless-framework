---
status: pending
priority: p3
issue_id: "147"
tags: [code-review, null-safety, paymob, cashin]
dependencies: []
---

# Null-Forgiving Operator on Deserialization

## Problem Statement

Multiple methods use the null-forgiving operator (`!`) on deserialization results. If the API returns valid JSON that deserializes to `null`, users will get `NullReferenceException` later instead of a clear error.

## Findings

- **Location 1**: `PaymobCashInBroker.CreateOrder.cs:30`
  ```csharp
  return (await response.Content.ReadFromJsonAsync<CashInCreateOrderResponse>(_options.DeserializationOptions))!;
  ```

- **Location 2**: `PaymobCashInBroker.Payment.cs:66`
  ```csharp
  return (await response.Content.ReadFromJsonAsync<TResponse>(_options.DeserializationOptions))!;
  ```

- **Location 3**: `PaymobCashInBroker.RequestPaymentKey.cs:33`
  ```csharp
  return (await response.Content.ReadFromJsonAsync<CashInPaymentKeyResponse>(_options.DeserializationOptions))!;
  ```

- The `!` hides potential null issues

## Proposed Solutions

### Option 1: Validate Explicitly (Recommended)

**Approach:** Throw clear exception on null response.

```csharp
var result = await response.Content
    .ReadFromJsonAsync<TResponse>(_options.DeserializationOptions, cancellationToken)
    .AnyContext();

if (result is null)
{
    throw new PaymobCashInException(
        "Paymob CashIn returned null response body.",
        response.StatusCode,
        null
    );
}

return result;
```

**Pros:**
- Clear error when API returns null
- No hidden NullReferenceException
- Better debugging

**Cons:**
- Slightly more code

**Effort:** 1 hour (across all files)

**Risk:** Low

## Recommended Action

**To be filled during triage.**

## Technical Details

**Affected files:**
- `src/Framework.Payments.Paymob.CashIn/PaymobCashInBroker.CreateOrder.cs`
- `src/Framework.Payments.Paymob.CashIn/PaymobCashInBroker.Payment.cs`
- `src/Framework.Payments.Paymob.CashIn/PaymobCashInBroker.RequestPaymentKey.cs`
- `src/Framework.Payments.Paymob.CashIn/PaymobCashInAuthenticator.cs`

## Acceptance Criteria

- [ ] All null-forgiving operators removed
- [ ] Explicit null checks added
- [ ] Clear exception thrown on null response

## Work Log

### 2026-01-11 - Initial Discovery

**By:** Claude Code (Code Review)

**Actions:**
- Identified null-forgiving operators on deserialization
