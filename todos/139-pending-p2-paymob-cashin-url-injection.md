---
status: pending
priority: p2
issue_id: "139"
tags: [code-review, security, paymob, cashin]
dependencies: []
---

# URL Injection / Path Traversal Risk in ID Parameters

## Problem Statement

User-supplied `transactionId`, `orderId`, and `iframeId` are interpolated directly into URL paths without validation. While Flurl's `Url.Combine` provides some encoding, malicious input like `../` or URL-encoded variants could potentially manipulate the request path.

## Findings

- **Location 1**: `PaymobCashInBroker.TransactionsQueries.cs:41`
  ```csharp
  var requestUrl = Url.Combine(_options.ApiBaseUrl, $"acceptance/transactions/{transactionId}");
  ```

- **Location 2**: `PaymobCashInBroker.OrderQueries.cs:41`
  ```csharp
  var requestUrl = Url.Combine(_options.ApiBaseUrl, "ecommerce/orders", orderId);
  ```

- **Location 3**: `PaymobCashInBroker.Payment.cs:15`
  ```csharp
  return Url.Combine(_options.IframeBaseUrl, iframeId).SetQueryParams(...);
  ```

- No input validation before URL construction
- Impact depends on Paymob API's routing and Flurl's encoding

## Proposed Solutions

### Option 1: Validate IDs are Numeric (Recommended)

**Approach:** Validate that IDs are numeric before use.

```csharp
public async Task<CashInTransaction?> GetTransactionAsync(string transactionId)
{
    Argument.IsNotNullOrEmpty(transactionId);
    if (!long.TryParse(transactionId, out _))
    {
        throw new ArgumentException("Transaction ID must be numeric", nameof(transactionId));
    }
    // ...
}
```

**Pros:**
- Simple validation
- Prevents path manipulation
- Clear error message

**Cons:**
- Assumes IDs are always numeric (verify with Paymob docs)

**Effort:** 30 minutes

**Risk:** Low

---

### Option 2: URL Encode All Path Segments

**Approach:** Explicitly URL-encode all dynamic path segments.

```csharp
var requestUrl = Url.Combine(_options.ApiBaseUrl,
    $"acceptance/transactions/{Uri.EscapeDataString(transactionId)}");
```

**Pros:**
- Works for any ID format

**Cons:**
- May double-encode if Flurl already encodes

**Effort:** 30 minutes

**Risk:** Low

## Recommended Action

**To be filled during triage.**

## Technical Details

**Affected files:**
- `src/Framework.Payments.Paymob.CashIn/PaymobCashInBroker.TransactionsQueries.cs:38-57`
- `src/Framework.Payments.Paymob.CashIn/PaymobCashInBroker.OrderQueries.cs:38-57`
- `src/Framework.Payments.Paymob.CashIn/PaymobCashInBroker.Payment.cs:13-16`

## Acceptance Criteria

- [ ] All ID parameters validated before URL construction
- [ ] Clear error messages for invalid IDs
- [ ] Unit tests for path traversal attempts

## Work Log

### 2026-01-11 - Initial Discovery

**By:** Claude Code (Code Review)

**Actions:**
- Identified URL injection risk in ID parameters
- Analyzed Flurl's encoding behavior
- Drafted validation solution
